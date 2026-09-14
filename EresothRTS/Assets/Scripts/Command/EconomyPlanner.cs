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

        /// <summary>登记经济军令（OrderDispatcher 校验通过后调用）。
        /// 同类非训练计划互相替换（只替换同批次；工人调度类"分工/修理"互相顶替），"依次造A和B"的队列不会被普通命令清掉。</summary>
        public void Activate(Order o)
        {
            if (o.action != OrderAction.Train)
                for (int i = plans.Count - 1; i >= 0; i--)
                {
                    var p = plans[i];
                    if (p.IsTerminal || p.batchId != o.batchId) continue;
                    if (p.action == o.action || (IsWorkerDirective(p.action) && IsWorkerDirective(o.action)))
                        p.state = OrderState.Overridden;
                }
            plans.Add(o);
            o.state = OrderState.Executing;
        }

        static bool IsWorkerDirective(OrderAction a)
            => a == OrderAction.AssignWorkers || a == OrderAction.Repair;

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
                if (HasEarlierPendingInBatch(o)) continue;   // "依次造A和B"：等前序计划完成
                Tick(o);
            }
        }

        /// <summary>建造/训练队列：同批次内 sequence 较小的计划未终态时，本计划排队等待。</summary>
        bool HasEarlierPendingInBatch(Order o)
        {
            if (string.IsNullOrEmpty(o.batchId)) return false;
            for (int i = 0; i < plans.Count; i++)
            {
                var p = plans[i];
                if (p == o || p.batchId != o.batchId || p.sequence >= o.sequence) continue;
                if (!p.IsTerminal) return true;
            }
            return false;
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
                    // 完成判定：同类建筑（含施工中）达到 count 座即完成
                    int want = Mathf.Max(1, o.count);
                    int have = 0;
                    foreach (var b in g.buildings)
                        if (b != null && b.Alive && b.team == team && b.kind == o.targetId) have++;
                    if (have >= want) { Done(o); return; }
                    Building spawned = o.targetId == "resource_hub"
                        ? g.TryBuildForwardResourceHub(team)
                        : g.TrySpawnStructure(team, bd);
                    if (spawned != null && o.workerCount > 0)
                        g.PullWorkersToConstruct(team, spawned, o.workerCount);
                    // 失败（资源/空地不足）静默重试，成功则由下一跳的"已存在"判定推进
                    break;
                }
                case OrderAction.AssignWorkers:
                {
                    if (o.resource == "both")
                        RebalanceBothResources(g, team, Mathf.Clamp01(o.ratio <= 0f ? 0.6f : o.ratio), o.workerCount);
                    else
                        RebalanceWorkers(g, team, o.resource == "mana" ? "mana" : "wood",
                            Mathf.Clamp01(o.ratio <= 0f ? 0.5f : o.ratio), o.workerCount);
                    break;   // 持续计划：始终生效直到被替换/取消
                }
                case OrderAction.Repair:
                {
                    // 每跳把空闲工人派往受损最重的建筑（指定种类则只看该 kind）；修满自动收工
                    var rb = g.FindRepairTarget(team, o.targetId);
                    if (rb != null)
                        g.PullWorkersToRepair(team, rb, o.workerCount > 0 ? o.workerCount : 2);
                    break;   // 持续计划：直到被替换/取消
                }
            }
        }

        int TrainedCount(Order o)
            => trainedSoFar.TryGetValue(o.id, out var n) ? n : 0;

        readonly Dictionary<string, int> trainedSoFar = new();
        readonly Dictionary<string, int> researchStart = new();

        /// <summary>把工人按目标比例分配：workerCount=0 只动空闲工人（不打扰在采的）；
        /// workerCount&gt;0 抽 N 个（空闲优先，不足打断最近采集者）；workerCount=-1 全体重排。
        /// targetKind 采 targetRatio，其余采另一种。</summary>
        void RebalanceWorkers(Game g, Team team, string targetKind, float ratio, int workerCount)
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
            // 显式数量：空闲优先，不够则按距离打断采集者
            if (workerCount > 0)
            {
                var cand = new List<Unit>(idle);
                if (cand.Count < workerCount)
                    foreach (var u in g.units)
                    {
                        if (cand.Count >= workerCount) break;
                        if (u == null || !u.Alive || u.team != team || !u.def.worker || cand.Contains(u)) continue;
                        var w = u.GetComponent<Worker>();
                        if (w == null || w.state == Worker.State.Idle
                            || w.state == Worker.State.Constructing || w.state == Worker.State.Building) continue;
                        cand.Add(u);
                    }
                cand.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));   // 稳定顺序，避免抖动
                foreach (var u in cand)
                {
                    if (workerCount <= 0) break;
                    string kind = onTarget < wantTarget ? targetKind : (targetKind == "mana" ? "wood" : "mana");
                    var node = g.NearestNode(kind, u.transform.position) ?? g.NearestNodeAny(u.transform.position);
                    if (node == null) return;
                    u.GetComponent<Worker>().GatherAt(node);
                    if (kind == targetKind) onTarget++;
                    workerCount--;
                }
                return;
            }
            // 未指定数量：只动空闲工人，不打断正在采集的（避免抖动）
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

        /// <summary>"所有农民采资源"：按 manaRatio 采魔法矿、其余采木。
        /// workerCount=-1 时全体重排（超编工种强制改派）；否则只安排空闲工人。</summary>
        void RebalanceBothResources(Game g, Team team, float manaRatio, int workerCount)
        {
            var workers = new List<(Unit u, Worker w, string kind)>();
            foreach (var u in g.units)
            {
                if (u == null || !u.Alive || u.team != team || !u.def.worker) continue;
                var w = u.GetComponent<Worker>();
                if (w == null) continue;
                string kind = w.node != null ? w.node.kind : null;
                workers.Add((u, w, kind));
            }
            if (workers.Count == 0) return;
            int wantMana = Mathf.RoundToInt(workers.Count * manaRatio);
            int onMana = 0;
            foreach (var (_, _, kind) in workers) if (kind == "mana") onMana++;

            foreach (var (u, w, kind) in workers)
            {
                bool isIdle = w.state == Worker.State.Idle;
                if (!isIdle && workerCount != -1) continue;          // 非全体模式：不打扰在采的
                if (isIdle)
                {
                    string assign = onMana < wantMana ? "mana" : "wood";
                    var node = g.NearestNode(assign, u.transform.position) ?? g.NearestNodeAny(u.transform.position);
                    if (node == null) continue;
                    w.GatherAt(node);
                    if (assign == "mana") onMana++;
                }
                else if (kind != null)
                {
                    // 全体重排：超编的工种改派到另一种资源
                    if (kind == "mana" && onMana > wantMana)
                    {
                        var node = g.NearestNode("wood", u.transform.position);
                        if (node != null) { w.GatherAt(node); onMana--; }
                    }
                    else if (kind == "wood" && onMana < wantMana)
                    {
                        var node = g.NearestNode("mana", u.transform.position);
                        if (node != null) { w.GatherAt(node); onMana++; }
                    }
                }
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
