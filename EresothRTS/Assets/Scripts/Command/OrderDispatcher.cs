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

        /// <summary>提交军令：校验 → 仲裁 → 生效。返回是否受理（拒绝原因写在 failReason）。</summary>
        public bool SubmitOrder(Order o)
        {
            if (o == null) return false;
            o.createdAt = Time.time;
            o.id = $"ord_{nextId++:000}";

            // --- 校验 ---
            var force = ForceManager.I != null ? ForceManager.I.Find(o.forceId) : null;
            if (force == null) return Reject(o, $"军团不存在：{o.forceId}");
            if (force.AliveCount == 0) return Reject(o, $"{force.name} 已经没有可指挥的单位");
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
                    return RuntimeConfig.Units.ContainsKey(o.targetId) ? null : $"未知兵种：{o.targetId}";
                case OrderAction.Research:
                    if (string.IsNullOrEmpty(o.targetId)) return "未指定研究科技";
                    return GameConfig.Techs.ContainsKey(o.targetId) ? null : $"未知科技：{o.targetId}";
                case OrderAction.Build:
                    if (string.IsNullOrEmpty(o.targetId)) return "未指定建筑";
                    return RuntimeConfig.Buildings.ContainsKey(o.targetId) ? null : $"未知建筑：{o.targetId}";
                case OrderAction.AssignWorkers:
                    if (o.resource != "wood" && o.resource != "mana") return $"未知资源类型：{o.resource}";
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
