using System.Collections.Generic;
using UnityEngine;

namespace Eresoth
{
    /// <summary>游戏总控：建世界、管资源、判胜负。
    /// 场景里只需一个挂 Game 组件的空物体，其余全部运行时生成。</summary>
    public class Game : MonoBehaviour
    {
        public static Game I;

        [Header("资源（0=玩家 1=AI）")]
        public int[] wood = { 120, 120 };
        public int[] mana = { 60, 60 };

        [Header("科技等级（0=玩家 1=AI，每级攻防 +15%）")]
        public int[] atkLevel = { 0, 0 };
        public int[] defLevel = { 0, 0 };

        public readonly List<Unit> units = new();
        public readonly List<Building> buildings = new();
        public readonly List<ResourceNode> nodes = new();
        public readonly Vector3[] baseCenter = new Vector3[2];

        public bool over;
        public bool started;      // 世界是否已生成（开局设置面板确认后开始）
        public int winner = -1;
        public int seed;          // 本局地图种子（固定种子模式可复现布局）
        public string toast = "";
        float toastT;

        void Awake()
        {
            I = this;
            Application.targetFrameRate = 60;
            gameObject.AddComponent<SelectionManager>();
            gameObject.AddComponent<EnemyAI>();
            gameObject.AddComponent<GameHUD>();
        }

        /// <summary>开局设置面板确认后调用：按 MapSettings 生成本局世界。</summary>
        public void StartGame()
        {
            if (started) return;
            started = true;
            BuildWorld();
        }

        void Update()
        {
            if (toastT > 0) toastT -= Time.deltaTime;
        }

        // ---------------- 世界构建 ----------------

        void BuildWorld()
        {
            // 灯光
            var lightGo = new GameObject("Sun");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(50, -30, 0);
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);

            // 地面与边界
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(13, 1, 13); // 130x130
            ground.GetComponent<Renderer>().sharedMaterial = Gfx.Mat(new Color(0.23f, 0.32f, 0.20f));
            Color rock = new Color(0.30f, 0.28f, 0.30f);
            Gfx.Prim(PrimitiveType.Cube, null, new Vector3(0, 3,  68), new Vector3(150, 7, 6), rock).name = "Wall";
            Gfx.Prim(PrimitiveType.Cube, null, new Vector3(0, 3, -68), new Vector3(150, 7, 6), rock).name = "Wall";
            Gfx.Prim(PrimitiveType.Cube, null, new Vector3( 68, 3, 0), new Vector3(6, 7, 150), rock).name = "Wall";
            Gfx.Prim(PrimitiveType.Cube, null, new Vector3(-68, 3, 0), new Vector3(6, 7, 150), rock).name = "Wall";

            baseCenter[0] = new Vector3(-38, 0, -38); // 玩家：左下
            baseCenter[1] = new Vector3( 38, 0,  38); // AI：右上

            // 地图种子与丰富度：随机模式每局不同，关闭则用固定种子可复现
            seed = MapSettings.randomMap ? Random.Range(1, int.MaxValue) : 20240901;
            float richness = MapSettings.Richness;
            var rnd = new System.Random(seed);

            // 树木：丰富度决定数量，位置随机（避开双方基地与中央）
            int treeCount = Mathf.RoundToInt(26 * richness);
            for (int i = 0; i < treeCount; i++)
            {
                Vector3 p = Vector3.zero;
                int guard = 0;
                do
                {
                    p = new Vector3((float)(rnd.NextDouble() * 110 - 55), 0, (float)(rnd.NextDouble() * 110 - 55));
                    guard++;
                }
                while (guard < 60 && (Vector3.Distance(p, baseCenter[0]) < 15
                                    || Vector3.Distance(p, baseCenter[1]) < 15
                                    || p.magnitude < 10
                                    || NearNode(p, 4f)));
                ResourceNode.Spawn("wood", p);
            }

            // 魔法矿：双方基地附近各一半 + 中场争夺点，数量随丰富度、位置随种子
            int manaCount = Mathf.Max(2, Mathf.RoundToInt(4 * richness));
            int perBase = manaCount / 2, center = manaCount - perBase * 2;
            for (int side = 0; side < 2; side++)
                for (int i = 0; i < perBase; i++)
                {
                    double a = rnd.NextDouble() * 6.283, r = 9 + rnd.NextDouble() * 4;
                    ResourceNode.Spawn("mana", baseCenter[side] + new Vector3(
                        (float)(System.Math.Cos(a) * r), 0, (float)(System.Math.Sin(a) * r)));
                }
            for (int i = 0; i < center; i++)
            {
                Vector3 p = Vector3.zero;
                int guard = 0;
                do
                {
                    p = new Vector3((float)(rnd.NextDouble() * 50 - 25), 0, (float)(rnd.NextDouble() * 50 - 25));
                    guard++;
                }
                while (guard < 60 && (p.magnitude < 12 || NearNode(p, 5f)));
                ResourceNode.Spawn("mana", p);
            }

            SpawnBase(Team.Player, GameConfig.Farmer);
            SpawnBase(Team.Enemy, GameConfig.Acolyte);
            BuildCamera();
        }

        bool NearNode(Vector3 p, float dist)
        {
            foreach (var n in nodes)
                if (Vector3.Distance(p, n.transform.position) < dist) return true;
            return false;
        }

        void SpawnBase(Team team, UnitDef workerDef)
        {
            var c = baseCenter[(int)team];
            Building.Spawn(team, GameConfig.Hall, c);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 1.57f;
                Unit.Spawn(team, workerDef, c + new Vector3(Mathf.Cos(a) * 6, 0, Mathf.Sin(a) * 6));
            }
        }

        void BuildCamera()
        {
            var rig = new GameObject("CameraRig");
            rig.transform.position = baseCenter[0] + new Vector3(0, 0, -4);
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            camGo.transform.SetParent(rig.transform, false);
            camGo.transform.localPosition = new Vector3(0, 26, -17);
            camGo.transform.localRotation = Quaternion.Euler(57, 0, 0);
            var ctl = rig.AddComponent<RTSCameraController>();
            ctl.cam = camGo.transform;
        }

        // ---------------- 经济与科技 ----------------

        public bool TrySpend(int team, int w, int m)
        {
            if (wood[team] < w || mana[team] < m)
            {
                if (team == 0) Toast("资源不足");
                return false;
            }
            wood[team] -= w; mana[team] -= m;
            return true;
        }

        public void Deposit(Team team, string kind, int amount)
        {
            if (kind == "wood") wood[(int)team] += amount; else mana[(int)team] += amount;
        }

        public int PopCount(int team)
        {
            int c = 0;
            foreach (var u in units) if ((int)u.team == team) c += u.def.pop;
            return c;
        }

        // 科技乘区：不改单位静态数值，出手/受伤时实时计算
        public float AtkMult(int team) => 1f + 0.15f * atkLevel[team];
        public float DefMult(int team) => Mathf.Pow(1f / 1.15f, defLevel[team]);

        public int TechLevel(Team t, TechEffect e) => (e == TechEffect.Atk ? atkLevel : defLevel)[(int)t];

        public void FinishResearch(Team team, string techId)
        {
            var t = GameConfig.Techs[techId];
            if (t.effect == TechEffect.Atk) atkLevel[(int)team]++; else defLevel[(int)team]++;
            if (team == Team.Player) Toast($"研究完成：{t.name} Lv{TechLevel(team, t.effect)}");
        }

        // ---------------- 建造（固定槽位，不做选点放置） ----------------

        static readonly Vector3[] BuildSlots =
        {
            new Vector3(9, 0, 5), new Vector3(-9, 0, 5), new Vector3(9, 0, -5), new Vector3(-9, 0, -5),
            new Vector3(0, 0, 11), new Vector3(0, 0, -11), new Vector3(13, 0, 0), new Vector3(-13, 0, 0),
        };

        public bool BuildStructure(Team team, BuildingDef def)
        {
            if (BuildingOfKind(team, def.kind) != null)
            { if (team == Team.Player) Toast($"{def.name}已建成"); return false; }

            Vector3 c = baseCenter[(int)team];
            foreach (var off in BuildSlots)
            {
                Vector3 p = c + off;
                bool occupied = false;
                foreach (var b in buildings)
                    if (Vector3.Distance(p, b.transform.position) < 5f) { occupied = true; break; }
                if (occupied) continue;

                if (!TrySpend((int)team, def.wood, def.mana)) return false;
                Building.Spawn(team, def, p);
                return true;
            }
            if (team == Team.Player) Toast("基地周围没有空地");
            return false;
        }

        // ---------------- 查询 ----------------

        public Building BuildingOfKind(Team t, string kind) => buildings.Find(b => b.kind == kind && b.team == t);
        public Building Hall(Team t) => BuildingOfKind(t, "hall");

        public Building NearestHall(Team t, Vector3 p)
        {
            Building best = null; float bd = float.MaxValue;
            foreach (var b in buildings)
            {
                if (b.team != t || b.kind != "hall") continue;
                float d = Vector3.Distance(p, b.transform.position);
                if (d < bd) { bd = d; best = b; }
            }
            return best;
        }

        public ResourceNode NearestNode(string kind, Vector3 p)
        {
            ResourceNode best = null; float bd = float.MaxValue;
            foreach (var n in nodes)
            {
                if (n.kind != kind) continue;
                float d = Vector3.Distance(p, n.transform.position);
                if (d < bd) { bd = d; best = n; }
            }
            return best;
        }

        // ---------------- 胜负与提示 ----------------

        public void CheckEnd()
        {
            if (over) return;
            if (Hall(Team.Enemy) == null) { over = true; winner = 0; }
            else if (Hall(Team.Player) == null) { over = true; winner = 1; }
        }

        public void Toast(string msg) { toast = msg; toastT = 2f; }
        public bool ToastVisible => toastT > 0;
    }
}
