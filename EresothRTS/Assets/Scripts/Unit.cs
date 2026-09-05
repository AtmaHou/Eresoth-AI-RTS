using UnityEngine;

namespace Eresoth
{
    /// <summary>单位：移动（转向+局部避让）、战斗（自动索敌/显式攻击，循环克制加成）。
    /// 对外只暴露 CommandMove / CommandAttack 两条指令入口 ——
    /// 这正是后续 LLM 军令层的挂载点：LLM 不碰单位，只发指令。</summary>
    public class Unit : MonoBehaviour, ITargetable
    {
        public Team team;
        public UnitDef def;
        public float hp;

        [HideInInspector] public GameObject ring;
        [HideInInspector] public ITargetable target;
        [HideInInspector] public Vector3 moveDest;
        [HideInInspector] public bool hasMoveDest;
        float cd;

        // ---------- 生成 ----------

        public static Unit Spawn(Team team, UnitDef def, Vector3 pos)
        {
            var root = new GameObject(def.name);
            root.transform.position = pos;

            // 身体：按兵种大类组合不同图元（工人维持简单胶囊）
            switch (def.kind)
            {
                case UnitKind.Infantry:   // 胶囊身体 + 胸前盾牌
                    Gfx.Prim(PrimitiveType.Capsule, root.transform, new Vector3(0, def.size, 0),
                             Vector3.one * def.size, def.color);
                    Gfx.Prim(PrimitiveType.Cube, root.transform, new Vector3(0, def.size, def.size * 0.45f),
                             Vector3.one * def.size * 0.5f, def.color * 0.7f);
                    break;
                case UnitKind.Ranged:     // 较细身体 + 斜持长弓
                    Gfx.Prim(PrimitiveType.Capsule, root.transform, new Vector3(0, def.size, 0),
                             new Vector3(def.size * 0.8f, def.size, def.size * 0.8f), def.color);
                    var bow = Gfx.Prim(PrimitiveType.Cube, root.transform, new Vector3(def.size * 0.45f, def.size, 0),
                             new Vector3(def.size * 0.12f, def.size * 1.4f, def.size * 0.12f), def.color * 0.7f);
                    bow.transform.localRotation = Quaternion.Euler(0, 0, -35f);
                    break;
                case UnitKind.Cavalry:    // 大胶囊坐骑 + 上方骑手
                    Gfx.Prim(PrimitiveType.Capsule, root.transform, new Vector3(0, def.size * 0.75f, 0),
                             new Vector3(def.size * 1.2f, def.size * 0.9f, def.size * 1.2f), def.color * 0.7f);
                    Gfx.Prim(PrimitiveType.Capsule, root.transform, new Vector3(0, def.size * 1.7f, 0),
                             Vector3.one * def.size * 0.55f, def.color);
                    break;
                default:                  // Worker
                    Gfx.Prim(PrimitiveType.Capsule, root.transform, new Vector3(0, def.size, 0),
                             Vector3.one * def.size, def.color);
                    break;
            }

            // 阵营标记（头顶小块：蓝=玩家 红=AI）
            Gfx.Prim(PrimitiveType.Cube, root.transform, new Vector3(0, def.size * 2.15f, 0),
                     Vector3.one * def.size * 0.45f,
                     team == Team.Player ? new Color(.3f, .6f, 1f) : new Color(.85f, .25f, .4f));

            var cap = root.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0, def.size, 0);
            cap.radius = def.size * 0.5f;
            cap.height = def.size * 2f;

            var u = root.AddComponent<Unit>();
            u.team = team; u.def = def; u.hp = def.hp;
            if (def.worker) root.AddComponent<Worker>();

            // 选中环（贴地）
            var ring = Gfx.Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0, 0.05f, 0),
                     new Vector3(def.size * 1.8f, 0.02f, def.size * 1.8f),
                     team == Team.Player ? new Color(.2f, 1f, .4f) : new Color(1f, .3f, .3f));
            ring.SetActive(false);
            u.ring = ring;

            Game.I.units.Add(u);
            return u;
        }

        void OnDestroy() { if (Game.I != null) Game.I.units.Remove(this); }

        // ---------- 指令入口（未来由 LLM 军令层调用同一接口） ----------

        public void CommandMove(Vector3 p)
        {
            target = null; moveDest = p; hasMoveDest = true;
            var w = GetComponent<Worker>(); if (w != null) w.StopGather();
        }

        public void CommandAttack(ITargetable t)
        {
            target = t; hasMoveDest = false;
            var w = GetComponent<Worker>(); if (w != null) w.StopGather();
        }

        // ---------- 主循环 ----------

        void Update()
        {
            if (Game.I.over) return;
            float dt = Time.deltaTime;
            cd -= dt;

            if (target != null && !target.Alive) target = null;

            // 非采集单位无目标时自动索敌
            ITargetable tgt = target;
            if (tgt == null && !def.worker && !hasMoveDest) tgt = Acquire();

            if (tgt != null)
            {
                float dist = Vector3.Distance(transform.position, tgt.Pos) - tgt.Radius;
                if (dist > def.range) MoveStep(tgt.Pos, dt, tgt.Radius * 0.5f);
                else FaceAndHit(tgt, dt);
            }
            else if (hasMoveDest)
            {
                if (MoveStep(moveDest, dt, 0)) hasMoveDest = false;
            }
        }

        ITargetable Acquire()
        {
            ITargetable best = null; float bd = def.aggro;
            foreach (var u in Game.I.units)
            {
                if (u.team == team) continue;
                float d = Vector3.Distance(transform.position, u.transform.position);
                if (d < bd) { bd = d; best = u; }
            }
            if (best == null)
                foreach (var b in Game.I.buildings)
                {
                    if (b.team == team) continue;
                    float d = Vector3.Distance(transform.position, b.transform.position) - b.radius;
                    if (d < bd) { bd = d; best = b; }
                }
            return best;
        }

        void FaceAndHit(ITargetable tgt, float dt)
        {
            var dir = tgt.Pos - transform.position; dir.y = 0;
            if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 12f * dt);
            if (cd <= 0) { cd = def.cooldown; tgt.Damage(def.dmg * CounterMult(tgt) * Game.I.AtkMult((int)team)); }
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

        // ---------- 移动（转向 + 局部避让，供战斗/采集/指令共用） ----------

        public bool MoveStep(Vector3 dest, float dt, float extraRadius)
        {
            Vector3 to = dest - transform.position; to.y = 0;
            if (to.magnitude <= 0.7f + extraRadius) return true;
            Vector3 dir = to.normalized + Separation();
            dir.y = 0;
            if (dir.sqrMagnitude < 0.001f) dir = to.normalized;
            dir.Normalize();
            transform.position += dir * def.speed * dt;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 12f * dt);
            return false;
        }

        Vector3 Separation()
        {
            Vector3 push = Vector3.zero;
            var list = Game.I.units;
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i]; if (o == this) continue;
                Vector3 d = transform.position - o.transform.position; d.y = 0;
                float min = (def.size + o.def.size) * 0.45f;
                float dist = d.magnitude;
                if (dist > 0.0001f && dist < min) push += d.normalized * (min - dist) * 2f;
            }
            return push;
        }

        // ---------- ITargetable ----------

        public Vector3 Pos => transform.position;
        public float Radius => def.size * 0.6f;
        public Team Team => team;
        public bool Alive => hp > 0;
        public float Hp01 => hp / def.hp;
        public string DisplayName => def.name;

        public void Damage(float dmg)
        {
            hp -= dmg * Game.I.DefMult((int)team);
            if (hp <= 0)
            {
                hp = 0;
                Destroy(gameObject);
            }
        }
    }
}
