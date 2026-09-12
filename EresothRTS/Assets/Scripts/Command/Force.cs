using System.Collections.Generic;
using UnityEngine;

namespace Eresoth
{
    /// <summary>军团 stance：Aggressive=见敌就打，Defensive=守土有责，Cautious=残血主动撤。</summary>
    public enum ForceStance { Aggressive, Defensive, Cautious }

    /// <summary>军团：接受军令的最小作战单位。自然语言命令作用于军团而非单个单位。</summary>
    public class Force
    {
        public string id;                    // "army_1" ...
        public string name;                  // "一军团"
        public Team team;
        public readonly List<Unit> units = new();
        public ForceStance stance = ForceStance.Defensive;
        public Vector3 rallyPoint;           // 集结/撤退点
        public Order currentOrder;           // 当前执行中的军令（可为 null）

        /// <summary>平均生命比例（for 循环计算，避免 LINQ 分配）。</summary>
        public float HealthRatio
        {
            get
            {
                if (units.Count == 0) return 0f;
                float sum = 0f; int n = 0;
                for (int i = 0; i < units.Count; i++)
                    if (units[i] != null && units[i].Alive) { sum += units[i].Hp01; n++; }
                return n == 0 ? 0f : sum / n;
            }
        }

        /// <summary>军团质心（用于到达判定、敌情检测）。</summary>
        public Vector3 Centroid
        {
            get
            {
                Vector3 sum = Vector3.zero; int n = 0;
                for (int i = 0; i < units.Count; i++)
                    if (units[i] != null && units[i].Alive) { sum += units[i].transform.position; n++; }
                return n == 0 ? rallyPoint : sum / n;
            }
        }

        /// <summary>存活战斗单位数。</summary>
        public int AliveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < units.Count; i++)
                    if (units[i] != null && units[i].Alive) n++;
                return n;
            }
        }

        /// <summary>兵种构成统计（HUD/态势摘要用）。</summary>
        public Dictionary<UnitKind, int> Composition()
        {
            var d = new Dictionary<UnitKind, int>();
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u == null || !u.Alive) continue;
                d[u.def.kind] = d.TryGetValue(u.def.kind, out var c) ? c + 1 : 1;
            }
            return d;
        }

        /// <summary>清理已销毁单位引用（每 tick 调用）。</summary>
        public void Prune() => units.RemoveAll(u => u == null || !u.Alive);
    }
}
