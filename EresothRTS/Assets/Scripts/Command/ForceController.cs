using System.Collections.Generic;
using UnityEngine;

namespace Eresoth
{
    /// <summary>战术执行体：把军令翻译成 Unit.CommandMove/CommandAttack 级别的具体操作。
    /// 0.25s 一跳（不每帧跑），所有"半径查敌"用 for 循环避免分配。
    /// 紧急回防走事件订阅通道（≤300ms），不等待 tick。</summary>
    public class ForceController : MonoBehaviour
    {
        public static ForceController I;

        const float TickInterval = 0.25f;
        const float ArriveDist = 5f;        // 质心到达判定
        const float DefendLeash = 15f;      // 防守追击上限（离防守点）
        const float EngageRadius = 12f;     // 防守/集火的索敌半径
        const float MainForceRadius = 15f;  // "发现敌方主力"判定半径
        const int MainForceCount = 8;       // "敌方主力"兵力阈值
        const float LowHpDefault = 0.35f;   // 默认残血线
        const float RejoinHp = 0.9f;        // 撤退后归队血量线

        float tickT;
        readonly HashSet<Unit> retreating = new();   // 残血撤退中的单位（归队前不再接受军团调度）
        readonly HashSet<string> engagedLogged = new(); // 每条军令只上报一次接战

        void OnEnable()
        {
            I = this;
            GameEventBus.OnEvent += OnGameEvent;
        }

        void OnDisable()
        {
            GameEventBus.OnEvent -= OnGameEvent;
            if (I == this) I = null;
        }

        void Update()
        {
            if (Game.I == null || !Game.I.started || Game.I.over) return;
            tickT += Time.deltaTime;
            if (tickT < TickInterval) return;
            float dt = tickT; tickT = 0f;

            TickRetreating();
            if (ForceManager.I == null) return;
            var forces = ForceManager.I.forces;
            for (int i = 0; i < forces.Count; i++)
            {
                forces[i].Prune();
                var o = forces[i].currentOrder;
                if (o == null || o.state != OrderState.Executing) continue;
                TickOrder(forces[i], o);
            }
        }

        // ---------------- 紧急通道：事件驱动，不等 tick ----------------

        void OnGameEvent(GameEvent e)
        {
            if (e.type != GameEventType.BaseUnderAttack) return;
            if (Game.I == null || Game.I.over) return;
            // 主基地遇袭：离基地最近的己方军团立即回防（高优先级 90）
            if (ForceManager.I == null || OrderDispatcher.I == null) return;
            var forces = ForceManager.I.OfTeam(e.team);
            Force best = null; float bd = float.MaxValue;
            for (int i = 0; i < forces.Count; i++)
            {
                if (forces[i].AliveCount == 0) continue;
                var cur = forces[i].currentOrder;
                if (cur != null && !cur.IsTerminal && cur.priority >= 90) continue; // 已有同级/更高紧急命令
                float d = Vector3.Distance(forces[i].Centroid, e.pos);
                if (d < bd) { bd = d; best = forces[i]; }
            }
            if (best == null) return;
            OrderDispatcher.I.SubmitOrder(new Order
            {
                playerText = "[系统紧急] 基地遇袭自动回防",
                forceId = best.id,
                action = OrderAction.Defend,
                targetId = e.team == Game.I.playerTeam ? "own_main_base" : "enemy_main_base",
                stance = ForceStance.Defensive,
                priority = 90,
            });
        }

        // ---------------- 每 tick：驱动执行中的军令 ----------------

        void TickOrder(Force f, Order o)
        {
            // 过期
            if (o.expiresAt > 0f && Time.time > o.expiresAt)
            { OrderDispatcher.I.Complete(o, OrderState.Expired, "超过时限"); return; }

            // 军团覆灭
            if (f.AliveCount == 0)
            { OrderDispatcher.I.Complete(o, OrderState.Failed, "军团已全灭"); return; }

            // 条件触发检查（先判条件，再执行动作）
            if (CheckConditions(f, o)) return;

            // 残血撤退（命令条件或 Cautious stance 驱动）
            ApplyLowHpRetreat(f, o);

            SemanticMap.TryGet(o.targetId, out var sp);
            Vector3 targetPos = sp != null ? sp.pos : f.rallyPoint;

            switch (o.action)
            {
                case OrderAction.Move:      DoMove(f, o, targetPos); break;
                case OrderAction.AttackMove: DoAttackMove(f, o, targetPos); break;
                case OrderAction.Attack:    DoAttack(f, o, targetPos); break;
                case OrderAction.Defend:    DoDefend(f, o, targetPos); break;
                case OrderAction.Retreat:   DoRetreat(f, o, targetPos); break;
                case OrderAction.FocusFire: DoFocusFire(f, o); break;
                case OrderAction.Regroup:   DoRegroup(f, o); break;
                case OrderAction.Hold:      DoHold(f, o); break;
            }
        }

        // ---------------- 各动作实现 ----------------

        void DoMove(Force f, Order o, Vector3 dest)
        {
            Issue(f, o, u => u.CommandMove(dest));
            if (Vector3.Distance(f.Centroid, dest) < ArriveDist)
                OrderDispatcher.I.Complete(o, OrderState.Completed);
        }

        void DoAttackMove(Force f, Order o, Vector3 dest)
        {
            Issue(f, o, u => { if (!u.attackMove) u.CommandAttackMove(dest); });
            if (Vector3.Distance(f.Centroid, dest) < ArriveDist)
                OrderDispatcher.I.Complete(o, OrderState.Completed);
        }

        void DoAttack(Force f, Order o, Vector3 dest)
        {
            // 目标点附近的敌人（5 半径内最近）：有则全军集火该目标，无则推进到目标点
            var tgt = NearestEnemy(f.team, dest, 5f);
            if (tgt != null)
            {
                LogEngaged(f, o);
                Issue(f, o, u => { if (u.target != tgt) u.CommandAttack(tgt); });
            }
            else if (Vector3.Distance(f.Centroid, dest) < ArriveDist + 2f)
            {
                // 到达且无敌情：Attack 无对象可打
                if (o.conditions.Count > 0) { TriggerCondition(f, o, o.conditions[0]); return; }
                GameEventBus.Publish(GameEventType.TargetLost, f.team, dest, EventSeverity.Warning, o.id,
                    $"{f.name} 到达目标点但没有发现敌人");
                OrderDispatcher.I.Complete(o, OrderState.Failed, "目标消失");
                return;
            }
            else Issue(f, o, u => { if (!u.attackMove) u.CommandAttackMove(dest); });
        }

        void DoDefend(Force f, Order o, Vector3 guard)
        {
            var enemy = NearestEnemy(f.team, guard, EngageRadius);
            Issue(f, o, u =>
            {
                float distHome = Vector3.Distance(u.transform.position, guard);
                if (enemy != null && distHome <= DefendLeash)
                { if (u.target != enemy) u.CommandAttack(enemy); }
                else if (distHome > 4f)
                { if (!u.attackMove) u.CommandMove(guard); }   // 追击超 leash 或未接敌：回防守位
            });
            // 防守是持续命令：不自动完成，靠撤销/覆盖/过期结束
        }

        void DoRetreat(Force f, Order o, Vector3 dest)
        {
            Issue(f, o, u => u.CommandMove(dest));
            if (Vector3.Distance(f.Centroid, dest) < ArriveDist)
            {
                GameEventBus.Publish(GameEventType.ForceRetreated, f.team, dest,
                    EventSeverity.Info, f.id, $"{f.name} 已撤到安全位置");
                OrderDispatcher.I.Complete(o, OrderState.Completed);
            }
        }

        void DoFocusFire(Force f, Order o)
        {
            // 军团 12 半径内选最高价值目标：英雄优先，其次魔法矿造价高者
            var c = f.Centroid;
            Unit best = null; float bestScore = -1f;
            foreach (var u in Game.I.units)
            {
                if (u == null || !u.Alive || u.team == f.team || u.def.worker) continue;
                if (Vector3.Distance(c, u.transform.position) > EngageRadius) continue;
                float score = (u.def.hero ? 1000f : 0f) + u.def.mana;
                if (score > bestScore) { bestScore = score; best = u; }
            }
            if (best == null)
            {
                if (o.conditions.Count > 0) { TriggerCondition(f, o, o.conditions[0]); return; }
                GameEventBus.Publish(GameEventType.TargetLost, f.team, c, EventSeverity.Warning, o.id,
                    $"{f.name} 失去集火目标");
                OrderDispatcher.I.Complete(o, OrderState.Failed, "目标消失");
                return;
            }
            LogEngaged(f, o);
            Issue(f, o, u => { if (u.target != best) u.CommandAttack(best); });
        }

        void DoRegroup(Force f, Order o)
        {
            Issue(f, o, u => u.CommandMove(f.rallyPoint));
            if (Vector3.Distance(f.Centroid, f.rallyPoint) < ArriveDist)
                OrderDispatcher.I.Complete(o, OrderState.Completed);
        }

        void DoHold(Force f, Order o)
        {
            // 原地驻守：只下达一次"停在当前位置"，之后不再重复发令
            Issue(f, o, u => { if (!u.attackMove && u.target == null) u.CommandMove(u.transform.position); });
        }

        // ---------------- 通用规则 ----------------

        /// <summary>对军团内可调度的单位下发命令：过滤手动接管、残血撤退中和兵种筛选的单位。</summary>
        void Issue(Force f, Order o, System.Action<Unit> cmd)
        {
            for (int i = 0; i < f.units.Count; i++)
            {
                var u = f.units[i];
                if (u == null || !u.Alive) continue;
                if (u.manualOverrideUntil > Time.time) continue;   // 玩家手动接管中
                if (retreating.Contains(u)) continue;              // 残血撤退中
                if (o.unitFilter.HasValue && u.def.kind != o.unitFilter.Value) continue;
                cmd(u);
            }
        }

        /// <summary>条件检查：谓词求值，触发即完成当前军令并提交后继军令。返回是否已触发。</summary>
        bool CheckConditions(Force f, Order o)
        {
            for (int i = 0; i < o.conditions.Count; i++)
            {
                var c = o.conditions[i];
                if (!ConditionEvaluator.Eval(c, f, f.team)) continue;
                GameEventBus.Publish(GameEventType.ForceEngaged, f.team, f.Centroid, EventSeverity.Warning, o.id,
                    $"{f.name} 触发条件：{ConditionEvaluator.MetricName(c.metric)} {c.Describe()}");
                TriggerCondition(f, o, c);
                return true;
            }
            return false;
        }

        /// <summary>触发条件：当前军令完成，按条件定义提交后继军令（默认撤到撤退点）。</summary>
        void TriggerCondition(Force f, Order o, OrderCondition c)
        {
            OrderDispatcher.I.Complete(o, OrderState.Completed, $"触发条件：{c.when}");
            OrderDispatcher.I.SubmitOrder(new Order
            {
                playerText = o.playerText,
                forceId = f.id,
                action = c.then,
                targetId = string.IsNullOrEmpty(c.thenTargetId) ? "own_retreat_point" : c.thenTargetId,
                stance = ForceStance.Cautious,
                priority = o.priority,
            });
        }

        /// <summary>残血撤退：低于阈值的单位撤到集结点（Cautious stance 或命令自带血量条件）。</summary>
        void ApplyLowHpRetreat(Force f, Order o)
        {
            float threshold = -1f;
            if (f.stance == ForceStance.Cautious) threshold = LowHpDefault;
            for (int i = 0; i < o.conditions.Count; i++)
                if (o.conditions[i].when == "self_health_below")
                    threshold = Mathf.Max(threshold, o.conditions[i].threshold);
            if (threshold <= 0f) return;

            for (int i = 0; i < f.units.Count; i++)
            {
                var u = f.units[i];
                if (u == null || !u.Alive) continue;
                if (u.manualOverrideUntil > Time.time || retreating.Contains(u)) continue;
                if (u.Hp01 < threshold)
                {
                    retreating.Add(u);
                    u.CommandMove(f.rallyPoint);
                }
            }
        }

        /// <summary>撤退单位归队管理：回到 0.9 血以上后回到军团质心。</summary>
        void TickRetreating()
        {
            if (retreating.Count == 0) return;
            var rejoin = new List<Unit>();
            foreach (var u in retreating)
            {
                if (u == null || !u.Alive) { rejoin.Add(u); continue; }
                if (u.Hp01 >= RejoinHp)
                {
                    var f = ForceManager.I != null ? ForceManager.I.FindOfUnit(u) : null;
                    if (f != null) u.CommandMove(f.Centroid);
                    rejoin.Add(u);
                }
            }
            foreach (var u in rejoin) retreating.Remove(u);
        }

        void LogEngaged(Force f, Order o)
        {
            if (!engagedLogged.Add(o.id)) return;
            GameEventBus.Publish(GameEventType.ForceEngaged, f.team, f.Centroid,
                EventSeverity.Info, o.id, $"{f.name} 已与敌人交火");
        }

        // ---------------- 查询工具 ----------------

        /// <summary>pos 半径 r 内距 pos 最近的敌方目标（单位优先，其次建筑）。</summary>
        ITargetable NearestEnemy(Team team, Vector3 pos, float r)
        {
            Unit bestU = null; float bd = r;
            foreach (var u in Game.I.units)
            {
                if (u == null || !u.Alive || u.team == team) continue;
                float d = Vector3.Distance(pos, u.transform.position);
                if (d < bd) { bd = d; bestU = u; }
            }
            if (bestU != null) return bestU;
            Building bestB = null; bd = r;
            foreach (var b in Game.I.buildings)
            {
                if (b == null || !b.Alive || b.team == team) continue;
                float d = Vector3.Distance(pos, b.transform.position);
                if (d < bd) { bd = d; bestB = b; }
            }
            return bestB;
        }

        int CountEnemyNear(Team team, Vector3 pos, float r)
        {
            int n = 0;
            foreach (var u in Game.I.units)
            {
                if (u == null || !u.Alive || u.team == team || u.def.worker) continue;
                if (Vector3.Distance(pos, u.transform.position) <= r) n++;
            }
            return n;
        }
    }
}
