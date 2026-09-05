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
        [HideInInspector] public ITargetable lastAttacker;  // 自动反击：最近一次攻击者
        Transform visual;          // 身体图元挂点（攻击动作位移用）
        float attackAnimT;         // 攻击动作剩余时间
        float cd;

        const float AttackAnimDur = 0.25f;   // 攻击动作（前冲刺）时长

        // ---------- 生成 ----------

        public static Unit Spawn(Team team, UnitDef def, Vector3 pos)
        {
            var root = new GameObject(def.name);
            root.transform.position = pos;

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            // 身体：按兵种大类组合不同图元（工人维持简单胶囊），英雄额外王冠/武器/光环圈
            BuildBody(visual, def);

            // 阵营标记（头顶小块：蓝=0 号位 红=1 号位）
            float markerY = def.kind == UnitKind.Cavalry ? def.size * 2.5f : def.size * 2.15f;
            Gfx.Prim(PrimitiveType.Cube, visual, new Vector3(0, markerY, 0),
                     Vector3.one * def.size * 0.45f,
                     team == Team.Player ? new Color(.3f, .6f, 1f) : new Color(.85f, .25f, .4f));

            var cap = root.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0, def.size, 0);
            cap.radius = def.size * 0.5f;
            cap.height = def.size * 2f;

            var u = root.AddComponent<Unit>();
            u.team = team; u.def = def; u.hp = def.hp;
            u.visual = visual;
            if (def.worker) root.AddComponent<Worker>();

            // 选中环（贴地）
            var ring = Gfx.Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0, 0.05f, 0),
                     new Vector3(def.size * 1.8f, 0.02f, def.size * 1.8f),
                     team == Team.Player ? new Color(.2f, 1f, .4f) : new Color(1f, .3f, .3f));
            ring.SetActive(false);
            u.ring = ring;

            // 英雄光环圈（贴地，示出光环半径）
            if (def.hero)
                Gfx.Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0, 0.04f, 0),
                         new Vector3(def.auraRadius * 2f, 0.02f, def.auraRadius * 2f),
                         new Color(1f, .88f, .4f));

            Game.I.units.Add(u);
            return u;
        }

        /// <summary>单位造型：步兵=盾+头盔，远程=长弓+箭袋，骑兵=横置马身+四腿+骑手，工人=帽子；英雄加王冠和武器。</summary>
        static void BuildBody(Transform v, UnitDef def)
        {
            float s = def.size;
            Color dark = def.color * 0.7f;
            switch (def.kind)
            {
                case UnitKind.Infantry:
                    Gfx.Prim(PrimitiveType.Capsule, v, new Vector3(0, s, 0), Vector3.one * s, def.color);
                    Gfx.Prim(PrimitiveType.Sphere, v, new Vector3(0, s * 1.9f, 0), Vector3.one * s * 0.42f, dark);   // 头盔
                    Gfx.Prim(PrimitiveType.Cube, v, new Vector3(0, s, s * 0.45f), Vector3.one * s * 0.5f, dark);   // 盾
                    break;
                case UnitKind.Ranged:
                    Gfx.Prim(PrimitiveType.Capsule, v, new Vector3(0, s, 0),
                             new Vector3(s * 0.8f, s, s * 0.8f), def.color);
                    var bow = Gfx.Prim(PrimitiveType.Cube, v, new Vector3(s * 0.45f, s, 0),
                             new Vector3(s * 0.12f, s * 1.4f, s * 0.12f), dark);
                    bow.transform.localRotation = Quaternion.Euler(0, 0, -35f);                                     // 长弓
                    Gfx.Prim(PrimitiveType.Cube, v, new Vector3(0, s, -s * 0.45f),
                             new Vector3(s * 0.3f, s * 0.7f, s * 0.25f), dark);                                   // 箭袋
                    break;
                case UnitKind.Cavalry:
                    var horse = Gfx.Prim(PrimitiveType.Capsule, v, new Vector3(0, s * 0.8f, 0),
                             new Vector3(s * 1.35f, s * 0.7f, s * 0.7f), dark);
                    horse.transform.localRotation = Quaternion.Euler(0, 0, 90f);                                    // 马身（横置）
                    for (int lx = -1; lx <= 1; lx += 2)
                        for (int lz = -1; lz <= 1; lz += 2)
                            Gfx.Prim(PrimitiveType.Cube, v, new Vector3(lx * s * 0.42f, s * 0.22f, lz * s * 0.5f),
                                     Vector3.one * s * 0.2f, dark);                                              // 四条腿
                    Gfx.Prim(PrimitiveType.Capsule, v, new Vector3(0, s * 1.55f, 0), Vector3.one * s * 0.55f, def.color); // 骑手
                    break;
                default:   // Worker
                    Gfx.Prim(PrimitiveType.Capsule, v, new Vector3(0, s, 0), Vector3.one * s, def.color);
                    Gfx.Prim(PrimitiveType.Cube, v, new Vector3(0, s * 1.85f, 0),
                             new Vector3(s * 0.5f, s * 0.2f, s * 0.5f), dark);                                   // 帽子
                    break;
            }

            if (def.hero)
            {
                Gfx.Prim(PrimitiveType.Cube, v, new Vector3(0, markerHeight(def), 0),
                         new Vector3(s * 0.5f, s * 0.18f, s * 0.5f), new Color(1f, .85f, .3f));                  // 王冠
                var blade = Gfx.Prim(PrimitiveType.Cube, v, new Vector3(s * 0.62f, s, 0),
                         new Vector3(s * 0.1f, s * 1.5f, s * 0.1f), new Color(.92f, .92f, .95f));                // 武器
                blade.transform.localRotation = Quaternion.Euler(0, 0, -20f);
            }
        }

        static float markerHeight(UnitDef def)
            => def.kind == UnitKind.Cavalry ? def.size * 2.3f : def.size * 2.15f;

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

            // 攻击动作：身体向前冲刺一小段
            if (visual != null)
            {
                if (attackAnimT > 0)
                {
                    attackAnimT -= dt;
                    float k = Mathf.Sin((1f - attackAnimT / AttackAnimDur) * Mathf.PI);
                    visual.localPosition = Vector3.forward * (k * def.size * 0.45f);
                    if (attackAnimT <= 0) visual.localPosition = Vector3.zero;
                }
                else if (visual.localPosition != Vector3.zero)
                    visual.localPosition = Vector3.zero;
            }

            if (target != null && !target.Alive) target = null;

            // 自动反击/追击：谁打我我打谁——放下手头移动，锁定攻击者
            if (target == null && !def.worker && lastAttacker != null)
            {
                if (lastAttacker.Alive) { target = lastAttacker; hasMoveDest = false; }
                else lastAttacker = null;
            }

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
            if (cd <= 0)
            {
                cd = def.cooldown;
                attackAnimT = AttackAnimDur;
                float dmg = def.dmg * CounterMult(tgt) * Game.I.AtkMult((int)team)
                                  * Game.I.AuraDmgMult(team, transform.position);
                if (def.range >= 3f)
                {
                    Game.I.SpawnProjectile(team, transform.position + Vector3.up * def.size * 1.4f, tgt, dmg, def.aoeRadius, this);
                }
                else
                {
                    tgt.Damage(dmg);
                    if (tgt is Unit tu) tu.lastAttacker = this;
                    if (def.aoeRadius > 0f) Splash(tgt.Pos, dmg);   // 英雄近战 AOE
                }
            }
        }

        /// <summary>英雄 AOE：主目标周围敌方单位受到溅射伤害（SplashFrac）。</summary>
        void Splash(Vector3 center, float dmg)
        {
            foreach (var u in Game.I.units)
            {
                if (u.team == team || !u.Alive) continue;
                if (Vector3.Distance(u.transform.position, center) > def.aoeRadius) continue;
                u.Damage(dmg * GameConfig.SplashFrac);
                u.lastAttacker = this;
            }
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
