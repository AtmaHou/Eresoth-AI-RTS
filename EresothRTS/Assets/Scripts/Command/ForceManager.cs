using System.Collections.Generic;
using UnityEngine;

namespace Eresoth
{
    /// <summary>军团管理器：创建/编组/查询军团，新训练单位自动入编，定期更新动态语义点。
    /// 挂在 Game 物体上（Game.OnEnable 中 AddComponent）。</summary>
    public class ForceManager : MonoBehaviour
    {
        public static ForceManager I;

        public readonly List<Force> forces = new();
        int nextId = 1;
        float updateT;

        void OnEnable() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        /// <summary>创建空军团。name 传 null 时自动命名"第N军团"。</summary>
        public Force CreateForce(Team team, string name = null)
        {
            var f = new Force
            {
                id = $"army_{nextId}",
                name = name ?? $"第{nextId}军团",
                team = team,
                rallyPoint = Game.I.baseCenter[(int)team],
            };
            nextId++;
            forces.Add(f);
            return f;
        }

        /// <summary>把单位编入军团。</summary>
        public void Assign(Unit u, Force f)
        {
            if (u == null || f == null) return;
            var old = FindOfUnit(u);
            old?.units.Remove(u);
            if (!f.units.Contains(u)) f.units.Add(u);
            u.forceId = f.id;
        }

        public Force FindOfUnit(Unit u)
        {
            if (u == null || string.IsNullOrEmpty(u.forceId)) return null;
            return Find(u.forceId);
        }

        /// <summary>按 ID 或名称查找军团（"army_1"/"一军团"均可）。</summary>
        public Force Find(string idOrName)
        {
            if (string.IsNullOrEmpty(idOrName)) return null;
            for (int i = 0; i < forces.Count; i++)
                if (forces[i].id == idOrName || forces[i].name == idOrName) return forces[i];
            return null;
        }

        /// <summary>模糊解析军团指代："军团一/一军团/1队/一队/第一军团/army_1"均可；解析失败返回 null。
        /// "全军"类表述返回 null（由调用方展开为 AllForcesId）。</summary>
        public Force Resolve(string idOrName)
        {
            var exact = Find(idOrName);
            if (exact != null) return exact;
            int idx = ReferenceResolver.ForceIndex(idOrName);
            if (idx <= 0) return null;
            var mine = OfTeam(Game.I.playerTeam);
            // 序号对应该阵营军团列表位次（一军团=第 1 支，与命名一致）
            return idx <= mine.Count ? mine[idx - 1] : null;
        }

        /// <summary>把指代展开为军团列表："全军/__all__"=该阵营全部军团；其余解析为单军团。</summary>
        public List<Force> ResolveAll(string idOrName)
        {
            if (ReferenceResolver.IsAllForcesText(idOrName) || idOrName == ReferenceResolver.AllForcesId)
                return OfTeam(Game.I.playerTeam);
            var one = Resolve(idOrName);
            var list = new List<Force>();
            if (one != null) list.Add(one);
            return list;
        }

        /// <summary>编制调整：把来源军团（或除目标外全部）中符合筛选的单位编入目标军团。
        /// ratio(0~1) 为抽调比例（0=全部符合者）；kindFilter/unitRef 进一步限定兵种/英雄。返回调动人数。</summary>
        public int Reorganize(Force dest, string sourceId, float ratio, UnitKind? kindFilter, string unitRef)
        {
            if (dest == null) return 0;
            var sources = new List<Force>();
            if (ReferenceResolver.IsAllForcesText(sourceId) || sourceId == ReferenceResolver.AllForcesId
                || string.IsNullOrEmpty(sourceId))
                sources = OfTeam(dest.team).FindAll(f => f != dest);
            else
            {
                var s = Resolve(sourceId);
                if (s != null && s != dest) sources.Add(s);
            }
            var picked = new List<Unit>();
            foreach (var s in sources)
                foreach (var u in s.units)
                {
                    if (u == null || !u.Alive) continue;
                    if (kindFilter.HasValue && u.def.kind != kindFilter.Value) continue;
                    if (unitRef == "hero" && !u.def.hero) continue;
                    picked.Add(u);
                }
            if (picked.Count == 0) return 0;
            int take = picked.Count;
            if (ratio > 0f && ratio < 1f) take = Mathf.Max(1, Mathf.RoundToInt(picked.Count * ratio));
            take = Mathf.Min(take, picked.Count);
            // 均匀抽取（跨兵种错位取样），避免抽走的一半全是同一兵种
            var chosen = new HashSet<Unit>();
            for (int i = 0; i < take; i++)
            {
                int idx = picked.Count == take ? i : Mathf.Min(picked.Count - 1, Mathf.FloorToInt(i * picked.Count / (float)take));
                chosen.Add(picked[idx]);
            }
            foreach (var u in chosen) Assign(u, dest);
            return chosen.Count;
        }

        /// <summary>某阵营的所有军团。</summary>
        public List<Force> OfTeam(Team team)
        {
            var list = new List<Force>();
            for (int i = 0; i < forces.Count; i++)
                if (forces[i].team == team) list.Add(forces[i]);
            return list;
        }

        /// <summary>自动编组：把该阵营所有未编组的战斗单位编入军团。
        /// MVP 策略：交替分到两个军团（一军团/二军团），便于演示分兵命令。
        /// "没有可编组"时区分两种情况：已有军团（只是都编过组了）→ 报告现状；真的一个战斗单位都没有 → 提示先训练。</summary>
        public void AutoForm(Team team)
        {
            var mine = new List<Unit>();
            foreach (var u in Game.I.units)
                if (u != null && u.Alive && u.team == team && !u.def.worker && string.IsNullOrEmpty(u.forceId))
                    mine.Add(u);
            if (mine.Count == 0)
            {
                if (team != Game.I.playerTeam) return;
                var olds = OfTeam(team).FindAll(f => f.AliveCount > 0);
                if (olds.Count > 0)
                {
                    var sb = new System.Text.StringBuilder("已在编组：");
                    for (int i = 0; i < olds.Count; i++)
                    {
                        if (i > 0) sb.Append(" / ");
                        sb.Append($"{olds[i].name} {olds[i].AliveCount} 人");
                    }
                    sb.Append("（新训练的兵会自动入编）");
                    Game.I.Toast(sb.ToString());
                }
                else Game.I.Toast("没有可编组的战斗单位（先训练士兵）");
                return;
            }

            var olds0 = OfTeam(team);
            var f1 = olds0.Count > 0 ? olds0[0] : CreateForce(team, "一军团");
            var f2 = olds0.Count > 1 ? olds0[1] : CreateForce(team, "二军团");
            for (int i = 0; i < mine.Count; i++) Assign(mine[i], i % 2 == 0 ? f1 : f2);
            if (team == Game.I.playerTeam)
                Game.I.Toast($"编组完成：{f1.name} {f1.AliveCount} 人 / {f2.name} {f2.AliveCount} 人");
        }

        /// <summary>有战斗单位但还没编组时自动编组（首次发令前的兜底，保证 digest 的 forces_list 不为空）。
        /// 返回是否新编了组。军团已存在则不动作。</summary>
        public bool EnsureForces(Team team)
        {
            if (OfTeam(team).Count > 0) return false;
            foreach (var u in Game.I.units)
                if (u != null && u.Alive && u.team == team && !u.def.worker)
                {
                    AutoForm(team);
                    return true;
                }
            return false;
        }

        /// <summary>Unit.Spawn 挂钩：新训练的战斗单位自动编入该阵营第一个军团（工人跳过）。</summary>
        public void OnUnitSpawned(Unit u)
        {
            if (u == null || u.def.worker) return;
            if (!Game.I.started || Game.I.over) return;
            var olds = OfTeam(u.team);
            if (olds.Count == 0) return;   // 尚未编组时不动，等 AutoForm
            Assign(u, olds[0]);
        }

        void Update()
        {
            if (Game.I == null || !Game.I.started || Game.I.over) return;
            updateT += Time.deltaTime;
            if (updateT < 2f) return;
            updateT = 0f;
            // 每 2 秒：清理阵亡引用；rallyPoint 默认值跟随质心；更新敌方前线语义点
            for (int i = 0; i < forces.Count; i++)
            {
                forces[i].Prune();
                if (forces[i].AliveCount > 0 && forces[i].currentOrder == null)
                    forces[i].rallyPoint = forces[i].Centroid;
            }
            UpdateEnemyFrontline();
        }

        /// <summary>敌方前线 = 可见敌军（玩家方视角的敌人）质心；迷雾开启时只统计看得见的敌人，无可见敌人时保持不动。</summary>
        void UpdateEnemyFrontline()
        {
            Team enemy = Game.I.playerTeam == Team.Player ? Team.Enemy : Team.Player;
            Vector3 sum = Vector3.zero; int n = 0;
            foreach (var u in Game.I.units)
                if (u != null && u.Alive && u.team == enemy && !u.def.worker
                    && FogOfWarManager.VisibleToPlayer(u.transform.position))
                { sum += u.transform.position; n++; }
            if (n > 0) SemanticMap.Update("enemy_frontline", sum / n);
        }
    }
}
