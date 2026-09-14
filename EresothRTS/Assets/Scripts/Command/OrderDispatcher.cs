using System.Collections.Generic;
using UnityEngine;

namespace Eresoth
{
    /// <summary>军令调度器：统一入口（玩家自然语言 / 调试 JSON / 系统紧急回防共用）。
    /// 负责校验、冲突仲裁、生命周期管理和命令日志。不直接驱动单位——执行交给 ForceController。</summary>
    public class OrderDispatcher : MonoBehaviour
    {
        public static OrderDispatcher I;

        /// <summary>每军团同时执行的军令槽位（AI 指挥科技预留接口；MVP 近似无限）。</summary>
        public int maxConcurrentOrdersPerForce = 99;

        int nextId = 1;

        /// <summary>命令日志（环形，最近 100 条终态/在途军令）："为什么没执行"与回放的数据源。</summary>
        public readonly List<Order> journal = new();
        const int JournalCapacity = 100;

        void OnEnable() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        /// <summary>提交军令：校验 → 仲裁 → 生效。返回是否受理（拒绝原因写在 failReason）。
        /// 经济军令走 EconomyPlanner（不需要军团）；编制调整即时执行；军事军令绑定军团。</summary>
        public bool SubmitOrder(Order o)
        {
            if (o == null) return false;
            o.createdAt = Time.time;
            o.id = $"ord_{nextId++:000}";

            // --- 经济军令：独立通路，校验参数后交给 EconomyPlanner 持续执行 ---
            if (o.IsEconomy) return SubmitEconomy(o);

            // --- 编制调整：提交即执行完毕 ---
            if (o.action == OrderAction.Reorganize) return SubmitReorganize(o);

            // --- 军事军令：模糊解析军团（"军团一/1队/army_1"均可） ---
            if (ForceManager.I == null) return Reject(o, "军团系统未就绪");
            var force = ForceManager.I.Resolve(o.forceId);
            if (force == null && o.action == OrderAction.Scout)
            {
                // 侦察不绑定既有编队：标准开局没按 F10 也能侦查——先自动编队，侦察兵还会跨军团抽调
                ForceManager.I.AutoForm(Game.I.playerTeam);
                force = ForceManager.I.Resolve(o.forceId);
            }
            if (force == null)
                return Reject(o, $"军团不存在：{o.forceId}（可用：{ListForces()}；先按 F10 或说\"全军集结\"编组）");
            if (force.AliveCount == 0)
            {
                if (o.action == OrderAction.Scout)
                {
                    // 新训练的兵可能还没编组：补一次自动编队，再退而求其次挂到任一非空军团
                    ForceManager.I.AutoForm(Game.I.playerTeam);
                    if (force.AliveCount == 0)
                        force = ForceManager.I.OfTeam(Game.I.playerTeam).Find(x => x.AliveCount > 0) ?? force;
                    if (force.AliveCount == 0)
                        return Reject(o, "没有可派遣的作战单位（先训练士兵）");
                }
                else return Reject(o, $"{force.name} 已经没有可指挥的单位");
            }
            o.forceId = force.id;
            if (!string.IsNullOrEmpty(o.targetId) && !SemanticMap.Exists(o.targetId))
                return Reject(o, $"目标无法识别：{o.targetId}");

            o.state = OrderState.Validated;

            // --- 冲突仲裁：同军团现役命令按优先级比较 ---
            var cur = force.currentOrder;
            if (cur != null && !cur.IsTerminal)
            {
                if (o.priority >= cur.priority)
                {
                    cur.state = OrderState.Overridden;
                    cur.failReason = $"被更高优先级军令 {o.id} 覆盖";
                    Log(cur);
                }
                else return Reject(o, $"被现役高优先级军令 {cur.id}（{cur.Describe()}）占用");
            }

            // --- 生效 ---
            o.state = OrderState.Executing;
            force.currentOrder = o;
            force.stance = o.stance;
            Log(o);
            if (force.team == Game.I.playerTeam)
                Game.I.Toast($"{force.name}：{o.Describe()}");
            return true;
        }

        string ListForces()
        {
            if (ForceManager.I == null || Game.I == null) return "无";
            var fs = ForceManager.I.OfTeam(Game.I.playerTeam);
            if (fs.Count == 0) return "尚未编组（按 F10 自动编组）";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < fs.Count; i++)
            {
                if (i > 0) sb.Append('、');
                sb.Append(fs[i].name);
            }
            return sb.ToString();
        }

        bool SubmitEconomy(Order o)
        {
            string err = ValidateEconomy(o);
            if (err != null) return Reject(o, err);
            if (EconomyPlanner.I == null) return Reject(o, "经济计划器未就绪");
            o.state = OrderState.Validated;
            EconomyPlanner.I.Activate(o);
            Log(o);
            Game.I.Toast($"经济计划：{o.Describe()}");
            return true;
        }

        bool SubmitReorganize(Order o)
        {
            if (ForceManager.I == null) return Reject(o, "军团系统未就绪");
            var dest = ForceManager.I.Resolve(o.forceId);
            if (dest == null) return Reject(o, $"目标军团不存在：{o.forceId}");
            int moved = ForceManager.I.Reorganize(dest, o.sourceId, o.ratio, o.unitFilter, o.targetRef);
            if (moved == 0) return Reject(o, "没有符合筛选条件的可调动单位");
            o.forceId = dest.id;
            o.state = OrderState.Completed;
            Log(o);
            Game.I.Toast($"✔ 编制调整：{moved} 个单位编入 {dest.name}");
            return true;
        }

        /// <summary>撤销指定军令。</summary>
        public bool CancelOrder(string orderId, string reason = "玩家撤销")
        {
            var o = FindOrder(orderId);
            if (o == null || o.IsTerminal) return false;
            o.state = OrderState.Cancelled;
            o.failReason = reason;
            ClearForceRef(o);
            Log(o);
            return true;
        }

        /// <summary>撤销某军团全部现役军令（"撤销所有去北路的命令"类指令的基础）。</summary>
        public int CancelForceOrders(string forceId, string reason = "玩家撤销")
        {
            var f = ForceManager.I != null ? ForceManager.I.Find(forceId) : null;
            if (f == null || f.currentOrder == null || f.currentOrder.IsTerminal) return 0;
            return CancelOrder(f.currentOrder.id, reason) ? 1 : 0;
        }

        /// <summary>军令到达终态：由 ForceController 调用，负责清引用、记日志、发事件、弹战报。</summary>
        public void Complete(Order o, OrderState terminal, string reason = null)
        {
            if (o == null || o.IsTerminal) return;
            o.state = terminal;
            o.failReason = reason;
            ClearForceRef(o);
            Log(o);

            var f = ForceManager.I != null ? ForceManager.I.Find(o.forceId) : null;
            string forceName = f != null ? f.name : o.forceId;
            if (terminal == OrderState.Completed)
                GameEventBus.Publish(GameEventType.OrderCompleted, f != null ? f.team : Game.I.playerTeam,
                    f != null ? f.Centroid : Vector3.zero, EventSeverity.Info, o.id, $"{forceName} 完成：{o.Describe()}");
            else if (terminal == OrderState.Failed)
                GameEventBus.Publish(GameEventType.OrderFailed, f != null ? f.team : Game.I.playerTeam,
                    f != null ? f.Centroid : Vector3.zero, EventSeverity.Warning, o.id,
                    $"{forceName} 未能执行：{o.Describe()}（{reason}）");

            if (f != null && f.team == Game.I.playerTeam)
            {
                Game.I.Toast(terminal == OrderState.Completed
                    ? $"✔ {forceName} {o.Describe()}"
                    : terminal == OrderState.Failed
                        ? $"✘ {forceName} {o.Describe()}：{reason}"
                        : $"— {forceName} {o.Describe()}（{reason ?? terminal.ToString()}）");
            }
        }

        /// <summary>按 ID 查军令（含日志）。</summary>
        public Order FindOrder(string orderId)
        {
            for (int i = journal.Count - 1; i >= 0; i--)
                if (journal[i].id == orderId) return journal[i];
            return null;
        }

        /// <summary>回答"某军团为什么没执行/在执行什么"（命令 41 雏形）。</summary>
        public string ExplainForce(string forceId)
        {
            var f = ForceManager.I != null ? ForceManager.I.Find(forceId) : null;
            if (f == null) return $"找不到军团 {forceId}";
            for (int i = journal.Count - 1; i >= 0; i--)
            {
                var o = journal[i];
                if (o.forceId != f.id) continue;
                string stateCn = o.state switch
                {
                    OrderState.Executing => "正在执行",
                    OrderState.Completed => "已完成",
                    OrderState.Failed => $"失败：{o.failReason}",
                    OrderState.Overridden => $"被覆盖：{o.failReason}",
                    OrderState.Cancelled => $"已撤销：{o.failReason}",
                    OrderState.Expired => "已过期",
                    _ => o.state.ToString()
                };
                return $"{f.name} 最近一条军令 [{o.id}] {o.Describe()} —— {stateCn}";
            }
            return $"{f.name} 还没有收到过军令";
        }

        /// <summary>经济军令参数校验：未知即拒绝，给出可读原因（禁止静默映射）。</summary>
        string ValidateEconomy(Order o)
        {
            switch (o.action)
            {
                case OrderAction.Train:
                    if (string.IsNullOrEmpty(o.targetId)) return "未指定训练兵种";
                    if (!RuntimeConfig.Units.ContainsKey(o.targetId))
                    {
                        // 别名容错："弓箭手"等常见叫法归一到本局兵种 id
                        string aliasId = ReferenceResolver.ResolveTrainableUnitId(Game.I.playerTeam, o.targetId);
                        if (aliasId == null) return $"未知兵种：{o.targetId}";
                        o.targetId = aliasId;
                    }
                    return null;
                case OrderAction.Research:
                    if (string.IsNullOrEmpty(o.targetId)) return "未指定研究科技";
                    return GameConfig.Techs.ContainsKey(o.targetId) ? null : $"未知科技：{o.targetId}";
                case OrderAction.Build:
                    if (string.IsNullOrEmpty(o.targetId)) return "未指定建筑";
                    if (!RuntimeConfig.Buildings.ContainsKey(o.targetId))
                    {
                        string aliasKind = ReferenceResolver.ResolveBuildableKind(Game.I.playerTeam, o.targetId);
                        if (aliasKind == null) return $"未知建筑：{o.targetId}";
                        o.targetId = aliasKind;
                    }
                    return null;
                case OrderAction.AssignWorkers:
                    if (o.resource != "wood" && o.resource != "mana" && o.resource != "both")
                        return $"未知资源类型：{o.resource}";
                    if (o.ratio < 0f || o.ratio > 1f) return $"占比非法：{o.ratio}";
                    return null;
                case OrderAction.Repair:
                    if (string.IsNullOrEmpty(o.targetId) || o.targetId == "all") return null;
                    if (!RuntimeConfig.Buildings.ContainsKey(o.targetId))
                    {
                        string aliasKind = ReferenceResolver.ResolveBuildableKind(Game.I.playerTeam, o.targetId);
                        if (aliasKind == null) return $"未知建筑：{o.targetId}";
                        o.targetId = aliasKind;
                    }
                    return null;
                default:
                    return null;
            }
        }

        bool Reject(Order o, string reason)
        {
            o.state = OrderState.Failed;
            o.failReason = reason;
            Log(o);
            GameEventBus.Publish(GameEventType.OrderFailed, Game.I.playerTeam, Vector3.zero,
                EventSeverity.Warning, o.id, $"军令被拒绝：{reason}");
            Game.I.Toast($"军令被拒绝：{reason}");
            return false;
        }

        void ClearForceRef(Order o)
        {
            var f = ForceManager.I != null ? ForceManager.I.Find(o.forceId) : null;
            if (f != null && f.currentOrder == o) f.currentOrder = null;
        }

        void Log(Order o)
        {
            // 已在日志中（状态流转重复记录）则只更新，不重复占位
            for (int i = 0; i < journal.Count; i++)
                if (journal[i] == o) return;
            if (journal.Count >= JournalCapacity) journal.RemoveAt(0);
            journal.Add(o);
        }
    }
}
