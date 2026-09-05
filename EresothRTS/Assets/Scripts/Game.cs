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
        public int winner = -1;   // 0=玩家胜 1=AI胜
        public int seed;          // 本局地图种子（固定种子模式可复现布局）
        public Team playerTeam = Team.Player;   // 玩家操控的阵营（另一方归 AI）
        public string toast = "";
        float toastT;

        readonly List<Projectile> projs = new();   // 飞行中的弹道

        class Projectile
        {
            public Team team; public Unit shooter; public ITargetable target;
            public Vector3 pos; public float dmg, aoe; public GameObject gfx;
        }

        void Awake()
        {
            I = this;
            Application.targetFrameRate = 60;
            // 相机在开局前就要存在，否则开始界面 "no game rendering" 报错；开局时再按所选阵营对准基地
            baseCenter[0] = new Vector3(-38, 0, -38); // 0 号位：左下
            baseCenter[1] = new Vector3( 38, 0,  38); // 1 号位：右上
            BuildCamera();
            gameObject.AddComponent<SelectionManager>();
            gameObject.AddComponent<EnemyAI>();
            gameObject.AddComponent<GameHUD>();
        }

        /// <summary>开局设置面板确认后调用：按 MapSettings 生成本局世界。</summary>
        public void StartGame()
        {
            if (started) return;
            started = true;
            // 阵营在开局面板里选，必须在此时才读取（Awake 早于玩家选择，先读会拿到默认值）
            playerTeam = MapSettings.playerTeam;
            var rig = GameObject.Find("CameraRig");
            if (rig != null) rig.transform.position = baseCenter[(int)playerTeam] + new Vector3(0, 0, -4);
            BuildWorld();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (toastT > 0) toastT -= dt;
            ProjStep(dt);
        }

        // ---------------- 弹道（远程兵种/英雄/箭塔共用） ----------------

        /// <summary>发射一颗弹道：飞向目标，命中结算伤害；aoe > 0 时（英雄）在落点溅射。</summary>
        public void SpawnProjectile(Team team, Vector3 from, ITargetable target, float dmg, float aoe = 0f, Unit shooter = null)
        {
            var gfx = Gfx.Prim(PrimitiveType.Sphere, null, from, Vector3.one * 0.35f,
                     team == Team.Player ? new Color(1f, .85f, .3f) : new Color(.8f, .45f, 1f));
            projs.Add(new Projectile { team = team, shooter = shooter, target = target,
                                       pos = from, dmg = dmg, aoe = aoe, gfx = gfx });
        }

        void ProjStep(float dt)
        {
            for (int i = projs.Count - 1; i >= 0; i--)
            {
                var p = projs[i];
                if (p.target == null || !p.target.Alive) { Destroy(p.gfx); projs.RemoveAt(i); continue; }
                Vector3 dest = p.target.Pos + Vector3.up * 0.8f;
                Vector3 to = dest - p.pos;
                float step = 28f * dt;
                if (to.magnitude <= step + 0.25f)
                {
                    p.target.Damage(p.dmg);
                    if (p.target is Unit tu && p.shooter != null) tu.NotifyAttacked(p.shooter);
                    if (p.aoe > 0f)
                    {
                        // 英雄远程 AOE：落点溅射
                        foreach (var u in units)
                        {
                            if (u.team == p.team || !u.Alive) continue;
                            if (Vector3.Distance(u.transform.position, p.target.Pos) > p.aoe) continue;
                            u.Damage(p.dmg * GameConfig.SplashFrac);
                            if (p.shooter != null) u.NotifyAttacked(p.shooter);
                        }
                    }
                    Destroy(p.gfx); projs.RemoveAt(i);
                    continue;
                }
                p.pos += to.normalized * step;
                p.gfx.transform.position = p.pos;
            }
        }

        /// <summary>英雄攻击光环：pos 附近存在己方英雄时，伤害 ×(1+bonus)。</summary>
        public float AuraDmgMult(Team team, Vector3 pos)
        {
            foreach (var u in units)
                if (u.def.hero && u.team == team && Vector3.Distance(u.transform.position, pos) < u.def.auraRadius)
                    return 1f + u.def.auraBonus;
            return 1f;
        }

        // ---------------- 世界构建 ----------------

        void BuildWorld()
        {
            // 灯光：暖阳 + 柔和环境光 + 程序化天空盒，营造午后魔幻森林氛围
            var lightGo = new GameObject("Sun");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.color = new Color(1f, .96f, .88f);
            light.shadows = LightShadows.Soft;
            light.shadowStrength = .85f;
            lightGo.transform.rotation = Quaternion.Euler(48, -35, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.55f, .62f, .75f);
            RenderSettings.ambientEquatorColor = new Color(.48f, .50f, .48f);
            RenderSettings.ambientGroundColor = new Color(.30f, .28f, .22f);
            BuildSky();

            // 地面：低多边形起伏地形（噪声位移网格），不再是平板
            BuildTerrain();
            Color rock = new Color(0.34f, 0.32f, 0.35f);
            BuildCliffs(rock);

            // 地图种子与丰富度：随机模式每局不同，关闭则用固定种子可复现
            seed = MapSettings.randomMap ? Random.Range(1, int.MaxValue) : 20240901;
            float richness = MapSettings.Richness;
            var rnd = new System.Random(seed);

            // ---- 资源生成：只为 0 号基地一侧布局，1 号取中心对称点（-p），从布局上保证双方公平 ----
            int treeCount = Mathf.RoundToInt(26 * richness);
            const int homeTrees = 9;   // 每方基地旁保底的"家树"数量，距离基地 16~26

            void PlaceWood(Vector3 p) { p.y = TerrainHeight(p.x, p.z); ResourceNode.Spawn("wood", p); }
            void PlaceMana(Vector3 p) { p.y = TerrainHeight(p.x, p.z); ResourceNode.Spawn("mana", p); }

            // 家树：围绕 0 号基地环带生成，镜像到 1 号（间距校验对两侧都做）
            for (int i = 0; i < homeTrees; i++)
            {
                Vector3 p = Vector3.zero;
                int guard = 0;
                do
                {
                    double a = rnd.NextDouble() * 6.283, r = 16 + rnd.NextDouble() * 10;
                    p = baseCenter[0] + new Vector3((float)(System.Math.Cos(a) * r), 0, (float)(System.Math.Sin(a) * r));
                    guard++;
                }
                while (guard < 60 && (Mathf.Abs(p.x) > 55 || Mathf.Abs(p.z) > 55
                                    || NearNode(p, 4f) || NearNode(-p, 4f)));
                PlaceWood(p); PlaceWood(-p);
            }

            // 野树：中场随机，成对镜像（避开双方基地 15 格与中央 10 格）
            int wild = Mathf.Max(0, (treeCount - homeTrees * 2) / 2);
            for (int i = 0; i < wild; i++)
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
                                    || NearNode(p, 4f) || NearNode(-p, 4f)));
                PlaceWood(p); PlaceWood(-p);
            }

            // 魔法矿：近矿（9~13 环带）围绕 0 号基地生成并镜像 → 双方"起始矿"距离结构完全一致
            int manaCount = Mathf.Max(2, Mathf.RoundToInt(4 * richness));
            int perBase = manaCount / 2, center = manaCount - perBase * 2;
            for (int i = 0; i < perBase; i++)
            {
                Vector3 p = Vector3.zero;
                int guard = 0;
                do
                {
                    double a = rnd.NextDouble() * 6.283, r = 9 + rnd.NextDouble() * 4;
                    p = baseCenter[0] + new Vector3((float)(System.Math.Cos(a) * r), 0, (float)(System.Math.Sin(a) * r));
                    guard++;
                }
                while (guard < 60 && (NearNode(p, 5f) || NearNode(-p, 5f)));
                PlaceMana(p); PlaceMana(-p);
            }
            // 中场争夺矿：成对镜像；奇数时最后一座放在对称轴上（同样公平）
            for (int i = 0; i < center / 2; i++)
            {
                Vector3 p = Vector3.zero;
                int guard = 0;
                do
                {
                    p = new Vector3((float)(rnd.NextDouble() * 44 - 22), 0, (float)(rnd.NextDouble() * 44 - 22));
                    guard++;
                }
                while (guard < 60 && (p.magnitude < 12 || NearNode(p, 5f) || NearNode(-p, 5f)));
                PlaceMana(p); PlaceMana(-p);
            }
            if (center % 2 == 1)
            {
                float zz = (float)(14 + rnd.NextDouble() * 8) * (rnd.NextDouble() < .5 ? 1 : -1);
                var p = new Vector3(0, 0, zz);
                if (NearNode(p, 5f)) p.x = 9;
                PlaceMana(p);
            }

            // 纯装饰植被：草丛/灌木/岩石/花/倒木，提升地面细节密度
            ScatterDecor(rnd, richness);

            // 双方基地：工人按"该阵营所属玩家选择的种族"配置（玩家选了不死则 0 号位用侍僧体系）
            SpawnBase(Team.Player, playerTeam == Team.Player ? GameConfig.Farmer : GameConfig.Acolyte);
            SpawnBase(Team.Enemy, playerTeam == Team.Player ? GameConfig.Acolyte : GameConfig.Farmer);
        }

        // ---------------- 天空 / 地形 / 悬崖 / 装饰 ----------------

        /// <summary>程序化渐变天空盒：大反转球体 + 渐变贴图（天顶蓝→地平线暖白），零外部资源。
        /// 注：URP/Unlit 不乘顶点色，所以颜色走贴图而非顶点色。</summary>
        void BuildSky()
        {
            const int rings = 10, segs = 24;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            var zenith = new Color(.28f, .5f, .82f);
            var horizon = new Color(.88f, .88f, .92f);
            for (int r = 0; r <= rings; r++)
            {
                float t = r / (float)rings;            // 0=天顶 1=地平线之下
                float ang = t * Mathf.PI * .62f;
                float y = Mathf.Cos(ang), rad = Mathf.Sin(ang);
                for (int s = 0; s < segs; s++)
                {
                    float a = s * Mathf.PI * 2 / segs;
                    verts.Add(new Vector3(Mathf.Cos(a) * rad, y, Mathf.Sin(a) * rad) * 400f);
                    uvs.Add(new Vector2(0f, t));
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segs; s++)
                {
                    int a = r * segs + s, b = r * segs + (s + 1) % segs;
                    int c = a + segs, d = b + segs;
                    tris.AddRange(new[] { a, b, c, b, d, c });
                }
            var mesh = new Mesh { name = "sky", vertices = verts.ToArray(), triangles = tris.ToArray(), uv = uvs.ToArray() };
            mesh.RecalculateNormals();
            var go = new GameObject("Sky");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();

            // 渐变贴图（V 轴：0=天顶 1=地平线）
            const int texRes = 256;
            var tex = new Texture2D(1, texRes, TextureFormat.RGB24, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int i = 0; i < texRes; i++)
                tex.SetPixel(0, i, Color.Lerp(zenith, horizon, Mathf.Pow(i / (float)(texRes - 1), 1.4f)));
            tex.Apply();

            bool urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
            var mat = new Material(Shader.Find(urp ? "Universal Render Pipeline/Unlit" : "Sprites/Default"));
            if (urp)
            {
                mat.SetTexture("_BaseMap", tex);
                mat.SetColor("_BaseColor", Color.white);
            }
            else mat.SetTexture("_MainTex", tex);
            mr.sharedMaterial = mat;
        }

        /// <summary>给定世界坐标采样地形高度（与 BuildTerrain 使用同一噪声算法）。</summary>
        public static float TerrainHeight(float x, float z)
        {
            float h = Mathf.PerlinNoise(x * .04f + 7, z * .04f + 3) * 2.6f
                    + Mathf.PerlinNoise(x * .11f + 2, z * .11f + 9) * .8f - 1.2f;
            float flat = Mathf.Min(
                Vector2.Distance(new Vector2(x, z), new Vector2(-38, -38)),
                Vector2.Distance(new Vector2(x, z), new Vector2(38, 38)));
            float flatK = Mathf.InverseLerp(14f, 24f, flat);
            float centerK = Mathf.InverseLerp(8f, 16f, new Vector2(x, z).magnitude);
            return h * Mathf.Min(flatK, centerK);
        }

        /// <summary>低多边形起伏地形：Perlin 噪声位移的网格，基地与中央区域自动整平。
        /// 颜色烘成小尺寸贴图（URP Lit/Simple Lit 不乘顶点色，直接赋顶点色会渲成白色）。</summary>
        void BuildTerrain()
        {
            const int size = 130, res = 52;
            float half = size * .5f;
            var verts = new Vector3[(res + 1) * (res + 1)];
            var uvs = new Vector2[verts.Length];
            var tris = new List<int>();
            for (int z = 0; z <= res; z++)
                for (int x = 0; x <= res; x++)
                {
                    int i = z * (res + 1) + x;
                    float wx = x / (float)res * size - half;
                    float wz = z / (float)res * size - half;
                    verts[i] = new Vector3(wx, TerrainHeight(wx, wz), wz);
                    uvs[i] = new Vector2(x / (float)res, z / (float)res);
                }
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    int a = z * (res + 1) + x, b = a + 1, c = a + res + 1, d = c + 1;
                    tris.AddRange(new[] { a, c, b, b, c, d });
                }
            var mesh = new Mesh { name = "terrain", vertices = verts, triangles = tris.ToArray(), uv = uvs };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("Terrain");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();

            // 颜色烘焙贴图：与顶点着色同一函数，任意管线下都可见
            const int texRes = 256;
            var tex = new Texture2D(texRes, texRes, TextureFormat.RGB24, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int z = 0; z < texRes; z++)
                for (int x = 0; x < texRes; x++)
                {
                    float wx = x / (float)(texRes - 1) * size - half;
                    float wz = z / (float)(texRes - 1) * size - half;
                    tex.SetPixel(x, z, TerrainColor(wx, wz, TerrainHeight(wx, wz)));
                }
            tex.Apply();

            bool urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
            Shader sh = urp ? Shader.Find("Universal Render Pipeline/Simple Lit") : Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Standard");
            var mat = new Material(sh);
            mat.SetTexture(urp ? "_BaseMap" : "_MainTex", tex);
            if (urp) mat.SetColor("_BaseColor", Color.white); else mat.color = Color.white;
            mr.sharedMaterial = mat;
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
        }

        /// <summary>地形顶点/贴图颜色：低处偏泥土，高处草色变化，叠加细噪声。</summary>
        static Color TerrainColor(float wx, float wz, float h)
        {
            var grassA = new Color(.30f, .42f, .22f);
            var grassB = new Color(.38f, .50f, .26f);
            var dirt = new Color(.42f, .36f, .24f);
            float g = Mathf.PerlinNoise(wx * .07f, wz * .07f);
            var c = Color.Lerp(grassA, grassB, g);
            if (h < -.5f) c = Color.Lerp(c, dirt, Mathf.InverseLerp(-.5f, -1.6f, h));
            return c * (.95f + Mathf.PerlinNoise(wx * .5f, wz * .5f) * .1f);
        }

        /// <summary>边界悬崖：不规则岩壁 + 顶部长草 + 远山剪影，取代平板墙。</summary>
        void BuildCliffs(Color rock)
        {
            var rnd = new System.Random(seed + 99);
            for (int side = 0; side < 4; side++)
            {
                bool horiz = side < 2;
                float sign = side % 2 == 0 ? 1 : -1;
                for (int i = 0; i < 14; i++)
                {
                    float t = -65f + i * 10f + (float)rnd.NextDouble() * 4f;
                    float w = 8f + (float)rnd.NextDouble() * 6f;
                    float h = 6f + (float)rnd.NextDouble() * 4f;
                    float d = 5f + (float)rnd.NextDouble() * 3f;
                    Vector3 pos = horiz ? new Vector3(t, h * .35f, sign * (66 + d * .2f))
                                        : new Vector3(sign * (66 + d * .2f), h * .35f, t);
                    var c = rock * (.85f + (float)rnd.NextDouble() * .3f);
                    var cliff = Gfx.MeshGo(Gfx.Frustum(.55f + (float)rnd.NextDouble() * .3f, 5 + rnd.Next(3)),
                                   null, pos, new Vector3(w, h, d), c, 0f, .2f);
                    cliff.transform.rotation = Quaternion.Euler(0, (float)rnd.NextDouble() * 360, 0);
                    // 崖顶草盖
                    Gfx.Prim(PrimitiveType.Cylinder, cliff.transform, Vector3.up * 1.02f,
                             new Vector3(1.02f, .06f, 1.02f), new Color(.32f, .45f, .24f) * (.8f + (float)rnd.NextDouble() * .4f), 0f, .2f);
                }
            }
            // 远山剪影（四角之外，两倍距离）
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 2 / 8 + .3f;
                float dist = 120 + (float)rnd.NextDouble() * 40;
                var pos = new Vector3(Mathf.Cos(a) * dist, 0, Mathf.Sin(a) * dist);
                float w = 30 + (float)rnd.NextDouble() * 26;
                float h = 18 + (float)rnd.NextDouble() * 16;
                var c = Color.Lerp(new Color(.45f, .5f, .6f), new Color(.6f, .65f, .75f), (float)rnd.NextDouble());
                var m = Gfx.MeshGo(Gfx.Frustum(.12f, 5), null, pos, new Vector3(w, h, w * .8f), c, 0f, .15f);
                m.transform.rotation = Quaternion.Euler(0, (float)rnd.NextDouble() * 360, 0);
                Gfx.MeshGo(Gfx.Frustum(.1f, 5), m.transform, Vector3.up * .72f, new Vector3(.32f, .3f, .26f),
                           new Color(.92f, .94f, .98f), 0f, .4f);
            }
        }

        /// <summary>纯装饰植被：草簇/灌木/岩石/小花/倒木，丰富地面细节。</summary>
        void ScatterDecor(System.Random rnd, float richness)
        {
            int n = Mathf.RoundToInt(90 * richness);
            for (int i = 0; i < n; i++)
            {
                var p = new Vector3((float)(rnd.NextDouble() * 116 - 58), 0, (float)(rnd.NextDouble() * 116 - 58));
                if (Vector3.Distance(p, baseCenter[0]) < 13 || Vector3.Distance(p, baseCenter[1]) < 13) continue;
                if (NearNode(p, 2.5f)) continue;
                p.y = TerrainHeight(p.x, p.z);
                var holder = new GameObject("decor").transform;
                holder.position = p;
                holder.rotation = Quaternion.Euler(0, (float)rnd.NextDouble() * 360, 0);
                double roll = rnd.NextDouble();
                if (roll < .38) Models.GrassTuft(holder, rnd);
                else if (roll < .58) Models.Bush(holder, rnd);
                else if (roll < .78) Models.Rock(holder, rnd);
                else if (roll < .92) Models.Flower(holder, rnd);
                else Models.FallenLog(holder, rnd);
            }
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
            c.y = TerrainHeight(c.x, c.z);
            Building.Spawn(team, GameConfig.Hall, c);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 1.57f;
                var up = c + new Vector3(Mathf.Cos(a) * 6, 0, Mathf.Sin(a) * 6);
                up.y = TerrainHeight(up.x, up.z);
                Unit.Spawn(team, workerDef, up);
            }
        }

        void BuildCamera()
        {
            var rig = new GameObject("CameraRig");
            rig.transform.position = baseCenter[(int)playerTeam] + new Vector3(0, 0, -4);
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
                if (team == (int)playerTeam) Toast("资源不足");
                return false;
            }
            wood[team] -= w; mana[team] -= m;
            return true;
        }

        public void Deposit(Team team, string kind, int amount)
        {
            if (kind == "wood") wood[(int)team] += amount; else mana[(int)team] += amount;
        }

        /// <summary>工人单次采集量；建有伐木场时 +50%。</summary>
        public int GatherAmt(Team team)
            => Mathf.RoundToInt(GameConfig.GatherAmount * (BuildingOfKind(team, "lumber") != null ? 1.5f : 1f));

        public int PopCount(int team)
        {
            int c = 0;
            foreach (var u in units) if ((int)u.team == team) c += u.def.pop;
            return c;
        }

        /// <summary>人口上限：主基地 40 + 每座民居 15，封顶 100。</summary>
        public int PopCap(Team t)
            => Mathf.Min(GameConfig.MaxPopCap,
                GameConfig.BasePop + GameConfig.HousePop * buildings.FindAll(b => b.team == t && b.kind == "house").Count);

        // 科技乘区：不改单位静态数值，出手/受伤时实时计算
        public float AtkMult(int team) => 1f + 0.15f * atkLevel[team];
        public float DefMult(int team) => Mathf.Pow(1f / 1.15f, defLevel[team]);

        public int TechLevel(Team t, TechEffect e) => (e == TechEffect.Atk ? atkLevel : defLevel)[(int)t];

        public void FinishResearch(Team team, string techId)
        {
            var t = GameConfig.Techs[techId];
            if (t.effect == TechEffect.Atk) atkLevel[(int)team]++; else defLevel[(int)team]++;
            if (team == playerTeam) Toast($"研究完成：{t.name} Lv{TechLevel(team, t.effect)}");
        }

        // ---------------- 建造（AI 用固定槽位；玩家自由选址） ----------------

        public const float BuildRadius = 32f;   // 玩家建筑需距己方主基地此范围内
        public const int TowerCap = 8;          // 箭塔建造上限；民居可建多座；其余建筑全场唯一

        static readonly Vector3[] BuildSlots =
        {
            new Vector3(9, 0, 5), new Vector3(-9, 0, 5), new Vector3(9, 0, -5), new Vector3(-9, 0, -5),
            new Vector3(0, 0, 11), new Vector3(0, 0, -11), new Vector3(13, 0, 0), new Vector3(-13, 0, 0),
        };

        /// <summary>AI 建建筑：固定槽位；民居/箭塔可建多座，其余同 kind 全场一座。</summary>
        public bool BuildStructure(Team team, BuildingDef def)
        {
            bool multi = def.kind == "tower" || def.kind == "house";
            if (!multi && BuildingOfKind(team, def.kind) != null)
            { if (team == playerTeam) Toast($"{def.name}已建成"); return false; }

            Vector3 c = baseCenter[(int)team];
            foreach (var off in BuildSlots)
            {
                Vector3 p = c + off;
                p.y = TerrainHeight(p.x, p.z);
                bool occupied = false;
                foreach (var b in buildings)
                    if (Vector3.Distance(p, b.transform.position) < 5f) { occupied = true; break; }
                if (occupied) continue;

                if (!TrySpend((int)team, def.wood, def.mana)) return false;
                Building.Spawn(team, def, p);
                return true;
            }
            if (team == playerTeam) Toast("基地周围没有空地");
            return false;
        }

        /// <summary>校验玩家自由选址：地图边界、主基地半径、与其他建筑/资源点不重叠。err 为失败原因。</summary>
        public bool CanPlaceAt(Team team, BuildingDef def, Vector3 p, out string err)
        {
            err = null;
            if (Mathf.Abs(p.x) > 58f || Mathf.Abs(p.z) > 58f) { err = "超出地图边界"; return false; }
            var hall = Hall(team);
            if (hall == null || Vector3.Distance(p, hall.transform.position) > BuildRadius)
            { err = $"需在主基地 {BuildRadius:0} 格范围内"; return false; }
            foreach (var b in buildings)
                if (Vector3.Distance(p, b.transform.position) < b.radius + def.size * 0.8f + 1f)
                { err = "与其他建筑重叠"; return false; }
            if (NearNode(p, 3f)) { err = "离资源点太近"; return false; }
            return true;
        }

        /// <summary>玩家在指定位置建建筑：民居/箭塔可建多座（箭塔有上限），其余同 kind 全场唯一。</summary>
        public bool BuildAt(Team team, BuildingDef def, Vector3 p)
        {
            if (def.kind == "tower")
            {
                int towers = buildings.FindAll(b => b.team == team && b.kind == "tower").Count;
                if (towers >= TowerCap) { if (team == playerTeam) Toast($"箭塔最多 {TowerCap} 座"); return false; }
            }
            else if (def.kind != "house" && BuildingOfKind(team, def.kind) != null)
            { if (team == playerTeam) Toast($"{def.name}已建成"); return false; }

            if (!CanPlaceAt(team, def, p, out string err))
            { if (team == playerTeam) Toast(err); return false; }
            if (!TrySpend((int)team, def.wood, def.mana)) return false;
            Building.Spawn(team, def, p);
            return true;
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

        /// <summary>不限种类：离 p 最近的资源点（建筑出厂判定集结点是否压在资源上）。</summary>
        public ResourceNode NearestNodeAny(Vector3 p)
        {
            ResourceNode best = null; float bd = float.MaxValue;
            foreach (var n in nodes)
            {
                float d = Vector3.Distance(p, n.transform.position);
                if (d < bd) { bd = d; best = n; }
            }
            return best;
        }

        // ---------------- 胜负与提示 ----------------

        public void CheckEnd()
        {
            if (over) return;
            var aiTeam = playerTeam == Team.Player ? Team.Enemy : Team.Player;
            if (Hall(aiTeam) == null) { over = true; winner = 0; }
            else if (Hall(playerTeam) == null) { over = true; winner = 1; }
        }

        public void Toast(string msg) { toast = msg; toastT = 2f; }
        public bool ToastVisible => toastT > 0;
    }
}
