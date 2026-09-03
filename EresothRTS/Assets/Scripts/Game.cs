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

        public readonly List<Unit> units = new();
        public readonly List<Building> buildings = new();
        public readonly List<ResourceNode> nodes = new();
        public readonly Vector3[] baseCenter = new Vector3[2];

        public bool over;
        public int winner = -1;
        public string toast = "";
        float toastT;

        void Awake()
        {
            I = this;
            Application.targetFrameRate = 60;
            BuildWorld();
            gameObject.AddComponent<SelectionManager>();
            gameObject.AddComponent<EnemyAI>();
            gameObject.AddComponent<GameHUD>();
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

            // 树木（固定随机种子，布局可复现）
            var rnd = new System.Random(20240901);
            for (int i = 0; i < 26; i++)
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
                                    || p.magnitude < 10));
                ResourceNode.Spawn("wood", p);
            }
            // 魔法矿：双方近点各 2 处 + 中央争夺点 2 处
            ResourceNode.Spawn("mana", baseCenter[0] + new Vector3(10, 0, 2));
            ResourceNode.Spawn("mana", baseCenter[0] + new Vector3(3, 0, 10));
            ResourceNode.Spawn("mana", baseCenter[1] + new Vector3(-10, 0, -2));
            ResourceNode.Spawn("mana", baseCenter[1] + new Vector3(-3, 0, -10));
            ResourceNode.Spawn("mana", new Vector3(0, 0, 16));
            ResourceNode.Spawn("mana", new Vector3(0, 0, -16));

            SpawnBase(Team.Player, GameConfig.Farmer);
            SpawnBase(Team.Enemy, GameConfig.Acolyte);
            BuildCamera();
        }

        void SpawnBase(Team team, UnitDef workerDef)
        {
            var c = baseCenter[(int)team];
            Building.Spawn(team, "hall", c);
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

        // ---------------- 经济 ----------------

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

        public void BuildBarracksPlayer()
        {
            if (Barracks(Team.Player) != null) return;
            if (TrySpend(0, GameConfig.BarracksWood, 0))
                Building.Spawn(Team.Player, "barracks", baseCenter[0] + new Vector3(9, 0, 5));
        }

        // ---------------- 查询 ----------------

        public Building Hall(Team t) => buildings.Find(b => b.kind == "hall" && b.team == t);
        public Building Barracks(Team t) => buildings.Find(b => b.kind == "barracks" && b.team == t);

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
