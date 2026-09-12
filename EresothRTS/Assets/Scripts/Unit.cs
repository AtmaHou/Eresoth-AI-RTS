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
        bool hasMoveOrder;                  // 显式移动期间禁止自动索敌
        public bool attackMove;             // 攻击移动中：有移动命令但仍允许自动索敌
        public Unit lastAttacker;           // 最近攻击者（AI 军令层用）
        public bool busy;                   // 采集中（由 WorkerAI 维护）
        public int holdSlot = -1;           // 驻守法阵索引（-1 = 非驻守）
        public Transform ring;              // 选中高光圈
        public string forceId;              // 所属军团 ID（空 = 未编组；由 ForceManager 维护）
        public float manualOverrideUntil;   // > Time.time 时军团执行体跳过该单位（玩家手动接管期）

        float cd;                           // 攻击冷却
        public float stunT;                 // 瘫痪剩余时间（督军锁链）
        float walkPhase;                    // 行走动画相位
        float atkAnim;                      // 攻击动画进度（0..1）
        float steerT;                       // 绕行剩余时间（被建筑/人群挡住时 > 0）
        float steerSide = 1f;               // 绕行方向 ±1
        Transform visual;                   // 视觉子物体（摆动动画只作用于此层，不影响逻辑朝向）
        BodyRig rig;                        // 动画挂点
        float vfxStepT;                     // 脚步特效节流

        /// <summary>动画挂点只读访问（特效/展示工具用；只允许视觉层读取）。</summary>
        public BodyRig Rig => rig;

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
            ForceManager.I?.OnUnitSpawned(u0);           // 新训练单位自动编入军团（战斗单位）
            return u0;
        }

        static Transform MakeRing(Transform parent, float size, Color c)
        {
            var go = new GameObject("ring");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0, .04f, 0);
            go.transform.localScale = Vector3.one * size * 1.4f;
            var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = Gfx.RingMesh(.42f, .5f, 24);
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
            hasMoveOrder = true;
            attackMove = false;
            var w = GetComponent<Worker>(); if (w != null) w.StopGather();
            busy = false;
        }

        /// <summary>玩家/AI 下达攻击命令。</summary>
        public void CommandAttack(ITargetable t)
        {
            target = t;
            movePos = t.Pos;
            hasMoveOrder = false;
            attackMove = false;
            var w = GetComponent<Worker>(); if (w != null) w.StopGather();
            busy = false;
        }

        /// <summary>攻击移动（军令层用）：向目的地推进，途中允许自动索敌接战。</summary>
        public void CommandAttackMove(Vector3 p)
        {
            target = null;
            movePos = p;
            hasMoveOrder = true;
            attackMove = true;
            var w = GetComponent<Worker>(); if (w != null) w.StopGather();
            busy = false;
        }

        void OnDestroy() { if (Game.I != null) Game.I.units.Remove(this); }

        void Update()
        {
            if (Game.I == null || Game.I.over) return;
            float dt = Time.deltaTime;
            if (stunT > 0) { stunT -= dt; Anim(0, dt); return; } // 瘫痪：静止
            ResolveOverlap();                    // 碰撞体积：单位/建筑间推挤，防穿模
            if (busy) return;   // 采集循环由 WorkerAI 驱动（移动与动画都在 Worker 里）

            // --- 目标决策：缓存目标失效则重寻最近敌（警戒范围）；攻击移动中也允许索敌 ---
            if ((!hasMoveOrder || attackMove) && (target == null || !target.Alive))
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
                        UnitVfx.PlayAttackStartEffect(this);   // 出手前摇提示（纯视觉）
                        bool ranged = def.range > 3f;
                        float dmg = def.dmg * CounterMult(target) * Game.I.AtkMult((int)team) * Game.I.AuraDmgMult(team, transform.position);
                        if (ranged)
                        {
                            // 弹道起点优先取武器挂点（弓口/法杖晶体），视觉更准确；不影响命中结算
                            Vector3 from = rig != null && rig.projectileOrigin != null
                                ? rig.projectileOrigin.position
                                : transform.position + Vector3.up * def.size * 1.5f;
                            Game.I.SpawnProjectile(team, from, target, dmg, def.aoeRadius, this);
                        }
                        else
                        {
                            target.Damage(dmg);
                            if (target is Unit tu) tu.NotifyAttacked(this);
                            if (def.aoeRadius > 0f)
                                foreach (var u in Game.I.units)
                                {
                                    if (u.team == team || !u.Alive || u == target as Unit) continue;
                                    if (Vector3.Distance(u.transform.position, target.Pos) > def.aoeRadius) continue;
                                    u.Damage(def.dmg * GameConfig.SplashFrac);
                                    u.NotifyAttacked(this);
                                }
                        }
                    }
                }
                else { Move(target.Pos, dt); moving = true; }
            }
            else if (hasMoveOrder && Vector3.Distance(transform.position, movePos) > .5f)
            {
                Move(movePos, dt); moving = true;
            }
            else if (hasMoveOrder)
            {
                hasMoveOrder = false;
            }

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
            Vector3 dir = to.normalized;
            // 被建筑/人群挡住时：向一侧偏转绕行（直线顶墙时径向推挤没有切向分力，会原地振荡卡死）
            if (steerT > 0)
            {
                steerT -= dt;
                dir = Quaternion.Euler(0, 75f * steerSide, 0) * dir;
            }
            var before = transform.position;
            transform.position += dir * (def.speed * dt);
            // 贴合起伏地形
            var p = transform.position;
            p.y = Game.TerrainHeight(p.x, p.z);
            transform.position = p;
            // 被挡检测：想动但实际位移远小于预期 → 触发一段绕行；选较空的一侧
            if (steerT <= 0)
            {
                var a = new Vector2(before.x, before.z);
                var b = new Vector2(p.x, p.z);
                if (Vector2.Distance(a, b) < def.speed * dt * .35f)
                {
                    steerT = .55f;
                    steerSide = PickSteerSide(dir);
                }
            }
        }

        /// <summary>选较空的一侧绕行：统计左右两侧建筑/单位的拥挤度，取空隙大的一边。</summary>
        float PickSteerSide(Vector3 dir)
        {
            float left = 0f, right = 0f;
            void Weigh(Vector3 pos, float r)
            {
                var d = pos - transform.position; d.y = 0;
                float dist = d.magnitude;
                if (dist > 7f || dist < .01f) return;
                float w = (7f - dist) * r;
                if (Vector3.Cross(dir, d).y > 0) left += w; else right += w;
            }
            foreach (var b in Game.I.buildings) Weigh(b.transform.position, 2f);
            foreach (var u in Game.I.units) if (u != this && u.Alive) Weigh(u.transform.position, 1f);
            if (Mathf.Abs(left - right) < .5f) return Random.value < .5f ? -1f : 1f;
            return left < right ? -1f : 1f;
        }

        /// <summary>碰撞体积：自己被重叠的单位/建筑挤出，防止互相穿模。
        /// 只移动自己、不推动别人——移动的兵会顺着切向分力绕开挡路者，而不是把前排挤开。
        /// 纯几何推挤（无刚体），每帧由 Unit.Update 调用。</summary>
        void ResolveOverlap()
        {
            var p = transform.position;
            foreach (var o in Game.I.units)
            {
                if (o == this || !o.Alive) continue;
                float min = Radius + o.Radius;
                var d = p - o.transform.position; d.y = 0;
                float m = d.magnitude;
                if (m < min)
                    p += (m > .001f ? d / m : Vector3.right) * (min - m);
            }
            foreach (var b in Game.I.buildings)
            {
                float min = Radius + b.radius;
                var d = p - b.transform.position; d.y = 0;
                float m = d.magnitude;
                if (m < min)
                    p += (m > .001f ? d / m : Vector3.right) * (min - m);
            }
            p.y = Game.TerrainHeight(p.x, p.z);
            transform.position = p;
        }

        /// <summary>被攻击通知：记录攻击者并做"继续任务 vs 反击"的收益判断。
        /// 无任务时交给 Update 的警戒自动索敌，不强制打断移动/撤退指令；工人不还手（战斗收益≈0）。</summary>
        public void NotifyAttacked(Unit attacker)
        {
            if (attacker != null && attacker.team != team) lastAttacker = attacker;
            if (Game.I.over || stunT > 0 || !Alive || attacker == null) return;
            if (attacker.team == team || !attacker.Alive || def.worker) return;
            if (target == null || !target.Alive) return;    // 无任务：由警戒索敌处理
            float dNew = Vector3.Distance(transform.position, attacker.transform.position);
            float dCur = Vector3.Distance(transform.position, target.Pos) - target.Radius;
            // 当前目标已在射程内且不更远：继续当前任务收益更大
            if (dCur <= def.range && dCur <= dNew + 1f) return;
            // 攻击者远在水面外（远超警戒圈）：威胁低，继续赶路
            if (dNew > def.aggro * 1.5f) return;
            CommandAttack(attacker);
        }

        /// <summary>供 Worker 驱动行走动画（Worker 接管移动时 Unit.Update 不再调用 Anim）。</summary>
        public void AnimWalk(bool moving, float dt) => Anim(moving ? 1f : 0f, dt);

        void Face(Vector3 dest)
        {
            Vector3 d = dest - transform.position; d.y = 0;
            if (d.sqrMagnitude > .01f) transform.rotation = Quaternion.LookRotation(d);
        }

        /// <summary>行走/攻击动画：腿部与手臂绕挂点正弦摆动，攻击时右臂前挥。</summary>
        void Anim(float moveBlend, float dt)
        {
            if (rig == null) return;

            // 移动脚步特效（仅 High 档，节流防堆积；纯视觉）
            if (moveBlend > 0 && UnitVfx.quality == VfxQuality.High)
            {
                vfxStepT -= dt;
                if (vfxStepT <= 0f) { vfxStepT = .45f; UnitVfx.PlayMoveEffect(this); }
            }
            else if (moveBlend <= 0f) vfxStepT = 0f;
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

            // 行走时身体轻微起伏（绝对基准赋值：无腿单位若基于 p.y 累加会只加不减、越走越"飞升"）
            if (rig.torso != null && rig.horseLegs == null)
            {
                var p = rig.torso.localPosition;
                p.y = (rig.legL != null ? def.size * .75f : 0f) + Mathf.Abs(Mathf.Sin(walkPhase)) * .05f * moveBlend;
                rig.torso.localPosition = p;
            }

            // 攻击动画：右臂快速前挥后复位
            if (atkAnim > 0)
            {
                atkAnim = Mathf.Max(0, atkAnim - dt * 5);
                float k = Mathf.Sin(atkAnim * Mathf.PI);
                if (rig.armR != null)
                {
                    if (rig.thrustAttack)
                    {
                        // 枪盾短刺：沿 +Z 突刺后收枪复位（位移基于基准快照，不漂移）
                        var hp = rig.armRHome;
                        hp.z += .35f * def.size * k;
                        rig.armR.localPosition = hp;
                        rig.armR.localRotation = Quaternion.Euler(-25 * k, 0, 0);
                    }
                    else rig.armR.localRotation = Quaternion.Euler(-90 * k, 0, 0);
                }
            }
            else if (rig.thrustAttack && rig.armR != null)
            {
                rig.armR.localPosition = rig.armRHome;   // 回到盾后防守姿态
            }

            // 扩展挂点动画（全部 null 安全；旋转/缩放用增量或快照基准，不产生累积漂移）
            float t = Time.time;
            if (rig.capePieces != null)
                for (int i = 0; i < rig.capePieces.Length; i++)
                {
                    var cp = rig.capePieces[i];
                    if (cp == null) continue;
                    cp.localRotation = Quaternion.Euler(
                        10 + Mathf.Sin(t * 1.8f + i * 1.1f) * (4f + 7f * moveBlend), 0, cp.localEulerAngles.z);
                }
            if (rig.soulCore != null)
            {
                rig.soulCore.Rotate(0, dt * 40f, 0, Space.Self);
                rig.soulCore.localScale = rig.soulCoreBase * (1f + .08f * Mathf.Sin(t * 2.5f));
            }
            if (rig.floatingParts != null && rig.floatBase != null)
                for (int i = 0; i < rig.floatingParts.Length && i < rig.floatBase.Length; i++)
                {
                    var fp = rig.floatingParts[i];
                    if (fp == null) continue;
                    fp.localPosition = rig.floatBase[i]
                        + new Vector3(0, Mathf.Sin(t * 1.2f + i * 1.7f) * .04f * def.size, 0);
                    fp.Rotate(0, dt * (18f + i * 6f), 0, Space.Self);
                }
            if (rig.auraOrigin != null) rig.auraOrigin.Rotate(0, dt * 30f, 0, Space.Self);
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
            if (hp <= 0)
            {
                hp = 0;
                UnitVfx.PlayDeathEffect(this);         // 死亡反馈（纯视觉）
                Destroy(gameObject);
            }
            else UnitVfx.PlayHitEffect(this);          // 受击/格挡反馈（纯视觉）
        }
    }
}
