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

        /// <summary>某阵营的所有军团。</summary>
        public List<Force> OfTeam(Team team)
        {
            var list = new List<Force>();
            for (int i = 0; i < forces.Count; i++)
                if (forces[i].team == team) list.Add(forces[i]);
            return list;
        }

        /// <summary>自动编组：把该阵营所有未编组的战斗单位编入军团。
        /// MVP 策略：交替分到两个军团（一军团/二军团），便于演示分兵命令。</summary>
        public void AutoForm(Team team)
        {
            var mine = new List<Unit>();
            foreach (var u in Game.I.units)
                if (u != null && u.Alive && u.team == team && !u.def.worker && string.IsNullOrEmpty(u.forceId))
                    mine.Add(u);
            if (mine.Count == 0) { if (team == Game.I.playerTeam) Game.I.Toast("没有可编组的战斗单位"); return; }

            var olds = OfTeam(team);
            var f1 = olds.Count > 0 ? olds[0] : CreateForce(team, "一军团");
            var f2 = olds.Count > 1 ? olds[1] : CreateForce(team, "二军团");
            for (int i = 0; i < mine.Count; i++) Assign(mine[i], i % 2 == 0 ? f1 : f2);
            if (team == Game.I.playerTeam)
                Game.I.Toast($"编组完成：{f1.name} {f1.AliveCount} 人 / {f2.name} {f2.AliveCount} 人");
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
