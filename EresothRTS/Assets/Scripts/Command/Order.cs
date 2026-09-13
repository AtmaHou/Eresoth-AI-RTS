using System.Collections.Generic;
using UnityEngine;

namespace Eresoth
{
    /// <summary>军令动作白名单 v2。军事走 ForceController；经济走 EconomyPlanner；编制调整即时执行。
    /// 扩展方式：在此注册枚举 + 在执行器挂钩，解析/校验/Prompt 词表全部自动跟随。</summary>
    public enum OrderAction
    {
        // 军事（作用于军团）
        Move, Attack, AttackMove, Defend, Retreat, FocusFire, Regroup, Hold, Scout,
        // 经济（作用于阵营，持续计划）
        Train, Research, Build, AssignWorkers,
        // 编制（作用于军团成员，提交即执行完毕）
        Reorganize
    }

    public enum OrderState { Created, Validated, Queued, Executing, Completed, Failed, Cancelled, Expired, Overridden }

    /// <summary>触发器比较运算符。</summary>
    public enum ConditionOp { Lt, Le, Gt, Ge }

    /// <summary>触发器谓词指标（通用注册表，求值逻辑在 ConditionEvaluator）。
    /// 组合"metric+op+value+subject"即可表达整类"如果……就……"，不为单条命令写硬规则。</summary>
    public enum ConditionMetric
    {
        EnemyCountNear,   // subject: "force" 或语义点ID；范围内敌战斗单位数
        AllyHealthRatio,  // subject: 忽略；军团平均血量（0~1）
        Resource,         // subject: "wood"/"mana"；资源库存
        BuildingHpRatio,  // subject: 建筑 kind（如 "hall"）；己方该类建筑最低血量比例
        EnemyVisible,     // subject: 语义点ID；该点 10 半径内是否有可见敌人（0/1）
        TimeElapsed       // subject: 忽略；军令生效后秒数
    }

    /// <summary>军令触发器：谓词条件 + 触发后继动作。</summary>
    public class OrderCondition
    {
        public ConditionMetric metric;
        public ConditionOp op;
        public float value;
        public string subject;           // 见各 metric 说明，可空
        public OrderAction then;         // 触发后动作
        public string thenTargetId;      // 触发后目标语义点（空 = 撤退点）

        public string Describe()
        {
            string opCn = op switch { ConditionOp.Lt => "<", ConditionOp.Le => "≤", ConditionOp.Gt => ">", _ => "≥" };
            return $"{metric}{opCn}{value} → {then}";
        }
    }

    /// <summary>军令：指挥层最小指令单元。五个槽位：主体(forceId) + 动作(action) + 目标(targetId)
    /// + 约束(stance/filter/priority/时限) + 触发器(conditions)。玩家原话快照永远保留。</summary>
    public class Order
    {
        public string id;                 // "ord_###" 自增
        public string playerText;         // 玩家原话快照（战报/解释/训练数据锚点）
        public string forceId;            // 目标军团（经济军令为空）
        public OrderAction action;
        public string targetId;           // 语义点 ID 或别名；经济动作用作参数槽（unit_id/tech_id/building_kind）
        public UnitKind? unitFilter;      // 只影响军团内该兵种，null = 全军团
        public ForceStance stance = ForceStance.Defensive;
        public int priority = 50;         // 大者优先；手动接管=100，系统紧急=90
        public readonly List<OrderCondition> conditions = new();
        public float expiresAt;           // Time.time 截止，0 = 不过期
        public OrderState state = OrderState.Created;
        public string failReason;         // 人类可读失败原因
        public float createdAt;

        // ---- 经济动作参数 ----
        public int count;                 // Train/Build：数量
        public float ratio;               // AssignWorkers：目标资源占比（0~1）；Reorganize：抽调比例（0=全部）
        public string resource;           // AssignWorkers："wood"/"mana"/"both"（both=按 ratio 采矿、其余采木）
        public int workerCount;           // Build/AssignWorkers：抽调工人数（0=只动空闲，-1=全体重排）

        // ---- 编制调整参数 ----
        public string sourceId;           // Reorganize：来源军团（"__all__" = 除目标军团外的全部）

        // ---- 模糊目标指代（运行时解析）----
        public string targetRef;          // "hero" / "kind:cavalry" / "bld:<别名>:enemy|own"

        // ---- 建造/训练队列 ----
        public string batchId;            // 同批次的计划按 sequence 依次执行（"依次造A和B"）
        public int sequence;

        // ---- 运行时状态（Scout 用，不入日志）----
        public Unit scoutUnit;
        public List<Vector3> scoutRoute;
        public int scoutIdx;

        public bool IsEconomy => action >= OrderAction.Train && action <= OrderAction.AssignWorkers;

        public bool IsTerminal => state == OrderState.Completed || state == OrderState.Failed
            || state == OrderState.Cancelled || state == OrderState.Expired || state == OrderState.Overridden;

        public static readonly Dictionary<OrderAction, string> ActionNames = new()
        {
            { OrderAction.Move, "移动" }, { OrderAction.Attack, "攻击" },
            { OrderAction.AttackMove, "推进" }, { OrderAction.Defend, "防守" },
            { OrderAction.Retreat, "撤退" }, { OrderAction.FocusFire, "集火" },
            { OrderAction.Regroup, "集结" }, { OrderAction.Hold, "驻守" },
            { OrderAction.Scout, "侦察" },
            { OrderAction.Train, "训练" }, { OrderAction.Research, "研究" },
            { OrderAction.Build, "建造" }, { OrderAction.AssignWorkers, "分配工人" },
            { OrderAction.Reorganize, "编制调整" },
        };

        /// <summary>一行可读描述：HUD/战报/态势摘要共用。</summary>
        public string Describe()
        {
            string act = ActionNames.TryGetValue(action, out var n) ? n : action.ToString();
            switch (action)
            {
                case OrderAction.Train:
                    return $"训练 {targetId}×{Mathf_Max1(count)}";
                case OrderAction.Research:
                    return $"研究 {TechName(targetId)}";
                case OrderAction.Build:
                    return count > 1 ? $"建造 {BuildingName(targetId)}×{Mathf_Max1(count)}" : $"建造 {BuildingName(targetId)}";
                case OrderAction.AssignWorkers:
                    if (resource == "both")
                        return $"全部工人按 {ratio * 100f:0}% 采魔法矿、其余采木";
                    return $"工人 {ratio * 100f:0}% 采{(resource == "mana" ? "魔法矿" : "木头")}";
                case OrderAction.Scout:
                    return "侦察一圈";
                case OrderAction.Reorganize:
                    return $"编制调整 → {forceId}";
                default:
                    string tgt = "";
                    if (!string.IsNullOrEmpty(targetId) && SemanticMap.TryGet(targetId, out var sp))
                        tgt = $" → {sp.displayName}";
                    return $"{act}{tgt}";
            }
        }

        static int Mathf_Max1(int v) => v < 1 ? 1 : v;
        static string TechName(string id)
            => id != null && GameConfig.Techs.TryGetValue(id, out var t) ? t.name : id;
        static string BuildingName(string kind)
            => kind != null && RuntimeConfig.Buildings.TryGetValue(kind, out var b) ? b.name : kind;
    }
}
