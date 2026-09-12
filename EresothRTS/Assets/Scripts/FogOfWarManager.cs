using System.Collections.Generic;
using UnityEngine;

namespace Eresoth
{
    /// <summary>战争迷雾管理器：魔兽式双重迷雾。
    /// 未探索 = 全黑；已探索但当前不可见 = 半暗（地形隐约可见，敌方单位/建筑隐藏）；可见 = 清晰。
    /// 视野由玩家阵营的单位与建筑提供；开关见 MapSettings.fogOfWar（开局面板与游戏内顶栏均可切换）。
    /// 指挥链路（态势摘要 / 条件求值 / 敌方前线语义点）经 VisibleToPlayer 查询：
    /// 迷雾开启时参谋与触发器只感知"看得见"的敌人，关闭时回退为全图感知（旧行为）。</summary>
    public class FogOfWarManager : MonoBehaviour
    {
        public static FogOfWarManager I;

        const int Res = 128;                  // 迷雾网格分辨率（覆盖整张地图）
        const float Interval = 0.25f;         // 视野刷新间隔（秒）
        const float PlaneY = 3.4f;            // 迷雾平面高度：高于地形起伏与装饰，低于多数建筑顶部
        const float ExploredAlpha = 0.45f;    // 已探索不可见区域的暗化程度（0=全透 1=全黑）

        readonly byte[] explored = new byte[Res * Res];
        readonly byte[] visible = new byte[Res * Res];
        readonly Color32[] pixels = new Color32[Res * Res];
        Texture2D tex;
        GameObject plane;
        float timer;
        float worldSize = 130f;

        // 敌方物体渲染开关缓存：instanceId -> 渲染器数组 / 上次状态（避免每帧开关）
        readonly Dictionary<int, Renderer[]> rendCache = new();
        readonly Dictionary<int, bool> visCache = new();

        void OnEnable() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        /// <summary>迷雾是否生效（开关打开且本局已开始）。</summary>
        public static bool Active => MapSettings.fogOfWar && Game.I != null && Game.I.started;

        /// <summary>pos 对玩家阵营当前是否可见（迷雾关闭/未开局时恒 true——调用方无需判空）。</summary>
        public static bool VisibleToPlayer(Vector3 pos)
            => !Active || I == null || I.VisibleAt(pos);

        /// <summary>pos 是否曾被探索过（已探索区域显示暗化地形）。</summary>
        public static bool ExploredToPlayer(Vector3 pos)
            => !Active || I == null || I.ExploredAt(pos);

        public bool VisibleAt(Vector3 pos) { Cell(pos, out int x, out int z); return visible[z * Res + x] == 1; }
        public bool ExploredAt(Vector3 pos) { Cell(pos, out int x, out int z); return explored[z * Res + x] == 1; }

        void Cell(Vector3 pos, out int x, out int z)
        {
            float half = worldSize * .5f;
            x = Mathf.Clamp(Mathf.FloorToInt((pos.x + half) / worldSize * Res), 0, Res - 1);
            z = Mathf.Clamp(Mathf.FloorToInt((pos.z + half) / worldSize * Res), 0, Res - 1);
        }

        /// <summary>开局时调用：按本局地图尺寸重置迷雾并立即刷新一次（避免开局黑屏一瞬）。</summary>
        public void ResetFog()
        {
            worldSize = MapSettings.WorldSize;
            System.Array.Clear(explored, 0, explored.Length);
            System.Array.Clear(visible, 0, visible.Length);
            rendCache.Clear();
            visCache.Clear();
            EnsurePlane();
            plane.transform.localScale = new Vector3(worldSize, 1f, worldSize);
            timer = 0f;
            Refresh();
        }

        void Update()
        {
            if (Game.I == null || !Game.I.started)
            {
                if (plane != null) plane.SetActive(false);
                return;
            }
            timer += Time.deltaTime;
            if (timer < Interval) return;
            timer = 0f;
            Refresh();
        }

        /// <summary>刷新视野网格 → 迷雾贴图 → 敌方物体可见性。</summary>
        public void Refresh()
        {
            var g = Game.I;
            if (g == null || !g.started) return;
            if (!MapSettings.fogOfWar)
            {
                if (plane != null) plane.SetActive(false);
                RestoreAllVisibility();
                return;
            }
            System.Array.Clear(visible, 0, visible.Length);
            Team pt = g.playerTeam;
            foreach (var u in g.units)
                if (u != null && u.Alive && u.team == pt)
                    Stamp(u.transform.position, SightOf(u));
            foreach (var b in g.buildings)
                if (b != null && b.Alive && b.team == pt)
                    Stamp(b.transform.position, SightOf(b));
            Upload();
            UpdateEnemyVisibility();
        }

        // ---------------- 视野 ----------------

        static float SightOf(Unit u)
        {
            if (u.def.worker) return 9f;
            if (u.def.hero) return 15f;
            return Mathf.Max(u.def.aggro + 3f, 11f);   // 视野略大于索敌半径，保证先看见再交战
        }

        static float SightOf(Building b)
            => b.kind == "hall" ? 17f : b.kind == "tower" ? 15f : 11f;

        void Stamp(Vector3 center, float radius)
        {
            float cellWorld = worldSize / Res;
            int cr = Mathf.CeilToInt(radius / cellWorld);
            Cell(center, out int cx, out int cz);
            float r2 = radius * radius;
            for (int dz = -cr; dz <= cr; dz++)
                for (int dx = -cr; dx <= cr; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= Res || z >= Res) continue;
                    float ox = dx * cellWorld, oz = dz * cellWorld;
                    if (ox * ox + oz * oz > r2) continue;
                    int i = z * Res + x;
                    visible[i] = 1;
                    explored[i] = 1;
                }
        }

        // ---------------- 迷雾渲染（高空黑色平面 + 透明度贴图，双管线兼容） ----------------

        void Upload()
        {
            EnsurePlane();
            byte exploredByte = (byte)(ExploredAlpha * 255);
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(0, 0, 0,
                    visible[i] == 1 ? (byte)0 : explored[i] == 1 ? exploredByte : (byte)255);
            tex.SetPixels32(pixels);
            tex.Apply(false);
            plane.SetActive(true);
        }

        void EnsurePlane()
        {
            if (plane != null) return;
            plane = new GameObject("FogOfWar");
            var mesh = new Mesh
            {
                name = "fog",
                vertices = new[]
                {
                    new Vector3(-.5f, 0, -.5f), new Vector3(-.5f, 0, .5f),
                    new Vector3(.5f, 0, -.5f), new Vector3(.5f, 0, .5f),
                },
                triangles = new[] { 0, 1, 2, 2, 1, 3 },
                uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1) },
            };
            mesh.RecalculateNormals();
            plane.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = plane.AddComponent<MeshRenderer>();

            tex = new Texture2D(Res, Res, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };

            bool urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
            Material mat;
            if (urp)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                mat.SetTexture("_BaseMap", tex);
                mat.SetColor("_BaseColor", Color.white);
                // 运行时把 URP/Unlit 切到透明模式
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = 3000;
            }
            else
            {
                mat = new Material(Shader.Find("Sprites/Default"));
                mat.SetTexture("_MainTex", tex);
            }
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            plane.transform.position = new Vector3(0, PlaneY, 0);
            plane.transform.localScale = new Vector3(worldSize, 1f, worldSize);
        }

        // ---------------- 敌方物体可见性（魔兽式：看不见就整体隐藏） ----------------

        void UpdateEnemyVisibility()
        {
            var g = Game.I;
            Team pt = g.playerTeam;
            foreach (var u in g.units)
            {
                if (u == null) continue;
                ApplyVisibility(u.gameObject, u.team == pt || VisibleAt(u.transform.position));
            }
            foreach (var b in g.buildings)
            {
                if (b == null) continue;
                ApplyVisibility(b.gameObject, b.team == pt || VisibleAt(b.transform.position));
            }
        }

        /// <summary>迷雾关闭时恢复全部渲染（包括本局之前被隐藏的敌人）。</summary>
        void RestoreAllVisibility()
        {
            var g = Game.I;
            foreach (var u in g.units) if (u != null) ApplyVisibility(u.gameObject, true);
            foreach (var b in g.buildings) if (b != null) ApplyVisibility(b.gameObject, true);
        }

        void ApplyVisibility(GameObject go, bool vis)
        {
            int id = go.GetInstanceID();
            if (visCache.TryGetValue(id, out bool last) && last == vis) return;
            visCache[id] = vis;
            if (!rendCache.TryGetValue(id, out var rs))
            {
                rs = go.GetComponentsInChildren<Renderer>(true);
                rendCache[id] = rs;
            }
            foreach (var r in rs) if (r != null) r.enabled = vis;
        }
    }
}
