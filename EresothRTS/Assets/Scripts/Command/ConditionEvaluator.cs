using UnityEngine;

namespace Eresoth
{
    /// <summary>触发器谓词求值器：所有 ConditionMetric 的统一求值入口。
    /// 新增 metric 只需在此加一个 case + 枚举，执行器/解析/Prompt 自动跟随。</summary>
    public static class ConditionEvaluator
    {
        /// <summary>求值某军令的某条件当前是否成立。f 为命令所属军团（经济军令可为 null，team 用玩家阵营）。</summary>
        public static bool Eval(OrderCondition c, Force f, Team team)
        {
            float actual = Measure(c.metric, c.subject, f, team);
            return c.op switch
            {
                ConditionOp.Lt => actual < c.value,
                ConditionOp.Le => actual <= c.value,
                ConditionOp.Gt => actual > c.value,
                _ => actual >= c.value,
            };
        }

        /// <summary>取指标当前值（战报/解释可用："为什么没触发——当前值 X，阈值 Y"）。</summary>
        public static float Measure(ConditionMetric metric, string subject, Force f, Team team)
        {
            var g = Game.I;
            switch (metric)
            {
                case ConditionMetric.EnemyCountNear:
                {
                    // subject="force"：以军团质心为圆心；否则以语义点为圆心
                    Vector3 center;
                    if (subject == "force" || subject == null)
                    {
                        if (f == null) return 0;
                        center = f.Centroid;
                    }
                    else if (SemanticMap.TryGet(subject, out var sp)) center = sp.pos;
                    else return 0;
                    int n = 0;
                    foreach (var u in g.units)
                        if (u != null && u.Alive && u.team != team && !u.def.worker
                            && Vector3.Distance(center, u.transform.position) <= 15f) n++;
                    return n;
                }
                case ConditionMetric.AllyHealthRatio:
                    return f != null ? f.HealthRatio : 0f;
                case ConditionMetric.Resource:
                    return subject == "mana" ? g.mana[(int)team] : g.wood[(int)team];
                case ConditionMetric.BuildingHpRatio:
                {
                    float min = float.MaxValue; bool found = false;
                    foreach (var b in g.buildings)
                    {
                        if (b == null || !b.Alive || b.team != team) continue;
                        if (!string.IsNullOrEmpty(subject) && b.kind != subject) continue;
                        found = true;
                        if (b.Hp01 < min) min = b.Hp01;
                    }
                    return found ? min : 0f;
                }
                case ConditionMetric.EnemyVisible:
                {
                    if (!SemanticMap.TryGet(subject, out var sp)) return 0;
                    foreach (var u in g.units)
                        if (u != null && u.Alive && u.team != team
                            && Vector3.Distance(sp.pos, u.transform.position) <= 10f
                            && FogOfWarManager.VisibleToPlayer(u.transform.position)) return 1f;
                    return 0f;
                }
                case ConditionMetric.TimeElapsed:
                    // 调用方需传入军令创建时间；这里通过 f.currentOrder 取
                    return f != null && f.currentOrder != null ? Time.time - f.currentOrder.createdAt : 0f;
                default:
                    return 0f;
            }
        }

        /// <summary>指标的中文描述（追问/解释用）。</summary>
        public static string MetricName(ConditionMetric m) => m switch
        {
            ConditionMetric.EnemyCountNear => "附近敌军数量",
            ConditionMetric.AllyHealthRatio => "军团血量",
            ConditionMetric.Resource => "资源库存",
            ConditionMetric.BuildingHpRatio => "建筑血量",
            ConditionMetric.EnemyVisible => "目标点敌情",
            ConditionMetric.TimeElapsed => "已执行时间",
            _ => m.ToString()
        };
    }
}
