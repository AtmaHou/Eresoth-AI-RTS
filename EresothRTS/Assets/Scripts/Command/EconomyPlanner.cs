using System.Collections.Generic;
using UnityEngine;

namespace Eresoth
{
    /// <summary>经济计划执行器：把"训练/研究/建造/工人分配"军令变成持续策略（而非单次点击）。
    /// 1s 一跳；资源不足时等待重试（不刷屏、不无限报错）；计划可取消可替换。</summary>
    public class EconomyPlanner : MonoBehaviour
    {
        public static EconomyPlanner I;

        const float TickInterval = 1f;
        float tickT;

        /// <summary>在途经济计划（按阵营）。训练计划可多条，研究/建造/分工各留最新一条。</summary>
        public readonly List<Order> plans = new();

        void OnEnable() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        /// <summary>登记经济军令（OrderDispatcher 校验通过后调用）。同类非训练计划互相替换。</summary>
        public void Activate(Order o)
        {
            if (o.action != OrderAction.Train)
                for (int i = plans.Count - 1; i >= 0; i--)
                    if (plans[i].action == o.action && !plans[i].IsTerminal)
                        plans[i].state = OrderState.Overridden;
            plans.Add(o);
            o.state = OrderState.Executing;
        }

        public void Cancel(Order o, string reason)
        {
            if (o == null || o.IsTerminal) return;
            o.state = OrderState.Cancelled;
            o.failReason = reason;
        }

        void Update()
        {
            if (Game.I == null || !Game.I.started || Game.I.over) return;
            tickT += Time.deltaTime;
            if (tickT < TickInterval) return;
            tickT = 0f;

            for (int i = plans.Count - 1; i >= 0; i--)
            {
                var o = plans[i];
                if (o.IsTerminal) { plans.RemoveAt(i); continue; }
                if (o.expiresAt > 0f && Time.time > o.expiresAt)
                { o.state = OrderState.Expired; o.failReason = "超过时限"; continue; }
                Tick(o);
            }
        }

        void Tick(Order o)
        {
            var g = Game.I;
            Team team = g.playerTeam;   // MVP：经济计划只服务玩家阵营
            switch (o.action)
            {
                case OrderAction.Train:
                {
                    if (!RuntimeConfig.Units.TryGetValue(o.targetId, out var ud))
                    { Fail(o, $"未知兵种 {o.targetId}"); return; }
                    // 找能训练该兵种的已完工建筑
                    int trained = TrainedCount(o);
                    if (trained >= Mathf.Max(1, o.count)) { Done(o); return; }
                    bool queued = false;
                    foreach (var b in g.buildings)
                    {
                        if (b == null || !b.Alive || b.team != team || b.constructing) continue;
                        if (b.def.train == null || !System.Array.Exists(b.def.train, d => d == ud)) continue;
                        if (b.TryTrain(ud)) { queued = true; break; }
                    }
                    // 资源不足/队列满：静默等下一跳（持续计划的本分）
                    if (!queued && !AffordAny(g, team, ud)) { /* 等待 */ }
                    break;
                }
                case OrderAction.Research:
                {
                    if (!GameConfig.Techs.TryGetValue(o.targetId, out var td))
                    { Fail(o, $"未知科技 {o.targetId}"); return; }
                    int lvl = g.TechLevel(team, td.effect);
                    if (lvl >= td.maxLevel) { Done(o); return; }
                    foreach (var b in g.buildings)
                    {
                        if (b == null || !b.Alive || b.team != team || b.constructing) continue;
                        if (b.def.techs == null || !System.Array.Exists(b.def.techs, t => t == o.targetId)) continue;
                        if (b.research == o.targetId) return;         // 已在研究，等完成
                        if (b.TryResearch(o.targetId)) return;        // 发起研究，等完成事件
                    }
                    break;   // 无可用建筑/资源不足：等待
                }
                case OrderAction.Build:
                {
                    if (!RuntimeConfig.Buildings.TryGetValue(o.targetId, out var bd))
                    { Fail(o, $"未知建筑 {o.targetId}"); return; }
                    // 已存在（含施工中）即视为计划完成
                    foreach (var b in g.buildings)
                        if (b != null && b.Alive && b.team == team && b.kind == o.targetId) { Done(o); return; }
                    if (o.targetId == "resource_hub") g.BuildForwardResourceHub(team);
                    else g.BuildStructure(team, bd);
                    // 失败（资源/空地不足）静默重试，成功则由下一跳的"已存在"判定完成
                    break;
                }
                case OrderAction.AssignWorkers:
                {
                    RebalanceWorkers(g, team, o.resource == "mana" ? "mana" : "wood",
                        Mathf.Clamp01(o.ratio <= 0f ? 0.5f : o.ratio));
                    break;   // 持续计划：始终生效直到被替换/取消
                }
            }
        }

        int TrainedCount(Order o)
            => trainedSoFar.TryGetValue(o.id, out var n) ? n : 0;

        readonly Dictionary<string, int> trainedSoFar = new();
        readonly Dictionary<string, int> researchStart = new();

        /// <summary>把空闲工人按比例分配给两种资源：目标 ratio 采 targetKind，其余采另一种。</summary>
        void RebalanceWorkers(Game g, Team team, string targetKind, float ratio)
        {
            var idle = new List<Unit>();
            int onTarget = 0, total = 0;
            foreach (var u in g.units)
            {
                if (u == null || !u.Alive || u.team != team || !u.def.worker) continue;
                var w = u.GetComponent<Worker>();
                if (w == null) continue;
                total++;
                if (w.state == Worker.State.Idle) idle.Add(u);
                else if (w.node != null && w.node.kind == targetKind) onTarget++;
            }
            if (total == 0) return;
            int wantTarget = Mathf.RoundToInt(total * ratio);
            // 只动空闲工人，不打断正在采集的（避免抖动）
            foreach (var u in idle)
            {
                string kind = onTarget < wantTarget ? targetKind : (targetKind == "mana" ? "wood" : "mana");
                var node = g.NearestNode(kind, u.transform.position);
                if (node == null) node = g.NearestNodeAny(u.transform.position);
                if (node == null) return;
                u.GetComponent<Worker>().GatherAt(node);
                if (kind == targetKind) onTarget++;
            }
        }

        static bool AffordAny(Game g, Team team, UnitDef ud)
            => g.wood[(int)team] >= ud.wood && g.mana[(int)team] >= ud.mana;

        void Done(Order o)
        {
            o.state = OrderState.Completed;
            GameEventBus.Publish(GameEventType.OrderCompleted, Game.I.playerTeam, Vector3.zero,
                EventSeverity.Info, o.id, $"经济计划完成：{o.Describe()}");
            Game.I.Toast($"✔ {o.Describe()}");
        }

        void Fail(Order o, string reason)
        {
            o.state = OrderState.Failed;
            o.failReason = reason;
            GameEventBus.Publish(GameEventType.OrderFailed, Game.I.playerTeam, Vector3.zero,
                EventSeverity.Warning, o.id, $"经济计划失败：{o.Describe()}（{reason}）");
            Game.I.Toast($"✘ {o.Describe()}：{reason}");
        }

        /// <summary>Building.Update 训练完成后回调进度（在 Building 出厂处挂钩）。</summary>
        public void OnUnitTrained(Team team, UnitDef def)
        {
            foreach (var o in plans)
                if (!o.IsTerminal && o.action == OrderAction.Train && o.targetId == def.id)
                    trainedSoFar[o.id] = (trainedSoFar.TryGetValue(o.id, out var n) ? n : 0) + 1;
        }
    }
}
