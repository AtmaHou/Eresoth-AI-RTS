using UnityEngine;

namespace Eresoth
{
    /// <summary>单位：移动 / 战斗 / 采集（采集由 WorkerAI 接管）。攻击用当前目标写 lastAttacker 供"刷怪盘"评分。
    /// 视觉由 Models 程序化拼装，自带行走摆动/攻击前摇动画。</summary>
    public class Unit : MonoBehaviour, ITargetable
    {
        public Team team;
        public UnitDef def;
        public float hp;
        public ITargetable target;          // 当前敌人（单位或建筑）
        public Vector3 movePos;             // 玩家右键目的地
        public Unit lastAttacker;           // 最近攻击者（AI 军令层用）
        public bool busy;                   // 采集中（由 WorkerAI 维护）
        public int holdSlot = -1;           // 驻守法阵索引（-1 = 非驻守）
        public Transform ring;              // 选中高光圈

        float cd;                           // 攻击冷却
        public float stunT;                 // 瘫痪剩余时间（督军锁链）
        float walkPhase;                    // 行走动画相位
        float atkAnim;                      // 攻击动画进度（0..1）
        Transform visual;                   // 视觉子物体（摆动动画只作用于此层，不影响逻辑朝向）
        BodyRig rig;                        // 动画挂点

        public static Unit Spawn(Team team, UnitDef def, Vector3 pos)
        {
            var go = new GameObject(def.name);
            go.transform.position = pos;

            // 视觉子物体：程序化模型（骑兵带马），挂在独立子节点上做动画
            var visualGo = new GameObject("visual");
            visualGo.transform.SetParent(go.transform, false);
            var u0 = go.AddComponent<Unit>();
            u0.visual = visualGo.transform;
            if (def.prefab != null)
            {
                var inst = Instantiate(def.prefab, visualGo.transform, false);
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
                // 尝试从 prefab 里找可能的动画器/渲染器；无则留空
            }
            else
            {
                u0.rig = Models.BuildUnit(visualGo.transform, team, def);
            }

            var col = go.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, def.size, 0); col.radius = def.size * .5f; col.height = def.size * 2;

            u0.team = team; u0.def = def; u0.hp = def.hp;
            u0.movePos = pos;

            // 选中高光圈
            u0.ring = MakeRing(u0.transform, def.size, team == Game.I.playerTeam ?
                new Color(.35f, .95f, .35f) : new Color(.95f, .35f, .35f));

            Game.I.units.Add(u0);
            if (def.worker) go.AddComponent<Worker>();   // 采集状态机（EnemyAI / 右键采集共用）
            return u0;
        }

        static Transform MakeRing(Transform parent, float size, Color c)
        {
            var go = new GameObject("ring");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0, .04f, 0);
            go.transform.localScale = Vector3.one * size * 1.4f;
            const int n = 24;
            var verts = new Vector3[n * 2 + 2];
            var tris = new int[n * 6];
            for (int i = 0; i <= n; i++)
            {
                float a = i * Mathf.PI * 2 / n;
                verts[i * 2] = new Vector3(Mathf.Cos(a) * .5f, 0, Mathf.Sin(a) * .5f);
                verts[i * 2 + 1] = new Vector3(Mathf.Cos(a) * .42f, 0, Mathf.Sin(a) * .42f);
                if (i < n)
                {
                    int b = i * 2;
                    tris[i * 6] = b; tris[i * 6 + 1] = b + 2; tris[i * 6 + 2] = b + 1;
                    tris[i * 6 + 3] = b + 1; tris[i * 6 + 4] = b + 2; tris[i * 6 + 5] = b + 3;
                }
            }
            var mesh = new Mesh { name = "ring", vertices = verts, triangles = tris };
            mesh.RecalculateNormals();
            var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Gfx.Mat(c, 0f, .45f, true);
            go.SetActive(false);
            return go.transform;
        }

        /// <summary>外部控制：移动到目标点，停止半径内视为到达。</summary>
        public bool MoveStep(Vector3 dest, float dt, float stopRadius)
        {
            float dist = Vector3.Distance(transform.position, dest) - stopRadius;
            if (dist <= .2f) return true;
            Move(dest, dt);
            return false;
        }

        /// <summary>玩家/AI 下达移动命令。</summary>
        public void CommandMove(Vector3 p)
        {
            target = null;
            movePos = p;
            var w = GetComponent<Worker>(); if (w != null) w.StopGather();
            busy = false;
        }

        /// <summary>玩家/AI 下达攻击命令。</summary>
        public void CommandAttack(ITargetable t)
        {
            target = t;
            movePos = t.Pos;
            var w = GetComponent<Worker>(); if (w != null) w.StopGather();
            busy = false;
        }

        void OnDestroy() { if (Game.I != null) Game.I.units.Remove(this); }

        void Update()
        {
            if (Game.I.over) return;
            float dt = Time.deltaTime;
            if (stunT > 0) { stunT -= dt; Anim(0, dt); return; } // 瘫痪：静止
            if (busy) { Anim(0, dt); return; }   // 采集循环由 WorkerAI 驱动

            // --- 目标决策：缓存目标失效则重寻最近敌（警戒范围） ---
            if (target == null || !target.Alive)
            {
                target = null;
                float best = def.aggro;
                foreach (var u in Game.I.units)
                {
                    if (u.team == team) continue;
                    float d = Vector3.Distance(transform.position, u.transform.position);
                    if (d < best) { best = d; target = u; }
                }
                if (target == null)
                {
                    foreach (var b in Game.I.buildings)
                    {
                        if (b.team == team) continue;
                        float d = Vector3.Distance(transform.position, b.transform.position) - b.radius;
                        if (d < best) { best = d; target = b; }
                    }
                }
            }

            bool moving = false;
            if (target != null)
            {
                float dist = Vector3.Distance(transform.position, target.Pos) - target.Radius;
                if (dist <= def.range)
                {
                    // 攻击：冷却到即出手；远程走 Game.SpawnProjectile 统一弹道
                    Face(target.Pos);
                    cd -= dt;
                    if (cd <= 0)
                    {
                        cd = def.cooldown;
                        atkAnim = 1f;
                        bool ranged = def.range > 3f;
                        float dmg = def.dmg * CounterMult(target) * Game.I.AtkMult((int)team) * Game.I.AuraDmgMult(team, transform.position);
                        if (ranged)
                            Game.I.SpawnProjectile(team, transform.position + Vector3.up * def.size * 1.5f,
                                                   target, dmg, def.aoeRadius, this);
                        else
                        {
                            target.Damage(dmg);
                            if (target is Unit tu) tu.lastAttacker = this;
                            if (def.aoeRadius > 0f)
                                foreach (var u in Game.I.units)
                                {
                                    if (u.team == team || !u.Alive || u == target as Unit) continue;
                                    if (Vector3.Distance(u.transform.position, target.Pos) > def.aoeRadius) continue;
                                    u.Damage(def.dmg * GameConfig.SplashFrac);
                                    u.lastAttacker = this;
                                }
                        }
                    }
                }
                else { Move(target.Pos, dt); moving = true; }
            }
            else if (Vector3.Distance(transform.position, movePos) > .5f) { Move(movePos, dt); moving = true; }

            Anim(moving ? 1f : 0f, dt);
        }

        /// <summary>循环克制：步兵克骑兵、远程克步兵、骑兵克远程，伤害 ×CounterBonus；对建筑/工人无加成。</summary>
        float CounterMult(ITargetable tgt)
        {
            if (tgt is not Unit tu || tu.def.worker) return 1f;
            bool counter =
                (def.kind == UnitKind.Infantry && tu.def.kind == UnitKind.Cavalry) ||
                (def.kind == UnitKind.Ranged   && tu.def.kind == UnitKind.Infantry) ||
                (def.kind == UnitKind.Cavalry  && tu.def.kind == UnitKind.Ranged);
            return counter ? GameConfig.CounterBonus : 1f;
        }

        void Move(Vector3 dest, float dt)
        {
            Vector3 to = dest - transform.position; to.y = 0;
            if (to.magnitude < .1f) return;
            Face(dest);
            transform.position += to.normalized * (def.speed * dt);
            // 贴合起伏地形
            var p = transform.position;
            p.y = Game.TerrainHeight(p.x, p.z);
            transform.position = p;
        }

        void Face(Vector3 dest)
        {
            Vector3 d = dest - transform.position; d.y = 0;
            if (d.sqrMagnitude > .01f) transform.rotation = Quaternion.LookRotation(d);
        }

        /// <summary>行走/攻击动画：腿部与手臂绕挂点正弦摆动，攻击时右臂前挥。</summary>
        void Anim(float moveBlend, float dt)
        {
            if (rig == null) return;
            if (moveBlend > 0)
                walkPhase += dt * def.speed * 1.6f;
            else
                walkPhase = Mathf.Lerp(walkPhase, Mathf.Round(walkPhase / Mathf.PI) * Mathf.PI, dt * 8);

            float sw = Mathf.Sin(walkPhase) * 28f * moveBlend;

            if (rig.legL != null) rig.legL.localRotation = Quaternion.Euler(sw, 0, 0);
            if (rig.legR != null) rig.legR.localRotation = Quaternion.Euler(-sw, 0, 0);
            if (rig.armL != null) rig.armL.localRotation = Quaternion.Euler(-sw * .6f, 0, 0);
            if (rig.armR != null && atkAnim <= 0) rig.armR.localRotation = Quaternion.Euler(sw * .6f, 0, 0);

            // 马腿对角逐摆
            if (rig.horseLegs != null)
                for (int i = 0; i < rig.horseLegs.Length; i++)
                {
                    float ph = walkPhase + (i == 0 || i == 3 ? 0 : Mathf.PI);
                    rig.horseLegs[i].localRotation = Quaternion.Euler(Mathf.Sin(ph) * 22f * moveBlend, 0, 0);
                }

            // 行走时身体轻微起伏
            if (rig.torso != null && rig.horseLegs == null)
            {
                var p = rig.torso.localPosition;
                p.y = (rig.legL != null ? def.size * .75f : p.y) + Mathf.Abs(Mathf.Sin(walkPhase)) * .05f * moveBlend;
                rig.torso.localPosition = p;
            }

            // 攻击动画：右臂快速前挥后复位
            if (atkAnim > 0)
            {
                atkAnim = Mathf.Max(0, atkAnim - dt * 5);
                float k = Mathf.Sin(atkAnim * Mathf.PI);
                if (rig.armR != null) rig.armR.localRotation = Quaternion.Euler(-90 * k, 0, 0);
            }
        }

        // ---------- ITargetable ----------

        public Vector3 Pos => transform.position;
        public float Radius => def.size * .5f;
        public Team Team => team;
        public bool Alive => hp > 0;
        public float Hp01 => hp / def.hp;
        public string DisplayName => def.name;

        public void Damage(float dmg)
        {
            hp -= dmg * Game.I.DefMult((int)team);
            if (hp <= 0) { hp = 0; Destroy(gameObject); }
        }
    }
}
