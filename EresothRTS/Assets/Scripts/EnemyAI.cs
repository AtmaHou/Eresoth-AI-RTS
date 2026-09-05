using System.Collections.Generic;
using UnityEngine;

namespace Eresoth
{
    /// <summary>第一阶段脚本 AI：采集 → 建地穴 → 攀科技 → 混编暴兵 → 分波进攻。
    /// 后续将被"执行体 AI + LLM 参谋"双层结构替换（设计文档 5.1）。</summary>
    public class EnemyAI : MonoBehaviour
    {
        float t;
        int wave;

        void Update()
        {
            var g = Game.I;
            if (g.over) return;
            t -= Time.deltaTime;
            if (t > 0) return;
            t = 2f; // 每 2 秒一轮决策

            var hall = g.Hall(Team.Enemy);
            if (hall == null) return;

            // 1. 采集分配：空闲工人去伐木，缺矿时派 2 个采水晶
            var workers = g.units.FindAll(u => u.team == Team.Enemy && u.def.worker);
            bool needMana = g.mana[1] < 40;
            int toMana = 0;
            foreach (var w in workers)
            {
                var wk = w.GetComponent<Worker>();
                if (wk.node == null && wk.state == Worker.State.Idle)
                {
                    string kind = (needMana && toMana < 2) ? "mana" : "wood";
                    if (kind == "mana") toMana++;
                    var n = g.NearestNode(kind, w.transform.position);
                    if (n != null) wk.GatherAt(n);
                }
            }

            // 2. 补工人
            if (workers.Count < 8) hall.TryTrain(GameConfig.Acolyte);

            // 3. 建筑顺序：地穴 → 诅咒神殿 → 死亡马厩（预留 50 木暴兵）
            if (g.BuildingOfKind(Team.Enemy, "crypt") == null
                && g.wood[1] >= GameConfig.Crypt.wood + 50)
                g.BuildStructure(Team.Enemy, GameConfig.Crypt);
            else if (g.BuildingOfKind(Team.Enemy, "dark_temple") == null
                && g.wood[1] >= GameConfig.DarkTemple.wood + 50)
                g.BuildStructure(Team.Enemy, GameConfig.DarkTemple);
            else if (g.BuildingOfKind(Team.Enemy, "death_stable") == null
                && g.wood[1] >= GameConfig.DeathStable.wood + 50)
                g.BuildStructure(Team.Enemy, GameConfig.DeathStable);

            // 4. 攀科技：木富余时轮流研究攻防（地穴，单研究槽）
            var crypt = g.BuildingOfKind(Team.Enemy, "crypt");
            if (crypt != null && crypt.research == null && g.wood[1] > 250)
            {
                int atk = g.TechLevel(Team.Enemy, TechEffect.Atk);
                int def = g.TechLevel(Team.Enemy, TechEffect.Def);
                crypt.TryResearch(atk <= def ? "undead_atk" : "undead_def");
            }

            // 5. 暴兵：地穴主力步兵；富矿时神殿补远程、马厩补骑兵（步:弓:骑 ≈ 3:2:2）
            if (crypt != null) crypt.TryTrain(GameConfig.Skeleton);
            var temple = g.BuildingOfKind(Team.Enemy, "dark_temple");
            if (temple != null && g.mana[1] > 30)
            {
                int ranged = g.units.FindAll(u => u.team == Team.Enemy && u.def.kind == UnitKind.Ranged).Count;
                if (ranged * 3 < ArmyCount(g)) temple.TryTrain(GameConfig.DarkArcher);
            }
            var stable = g.BuildingOfKind(Team.Enemy, "death_stable");
            if (stable != null && g.mana[1] > 60)
            {
                int cav = g.units.FindAll(u => u.team == Team.Enemy && u.def.kind == UnitKind.Cavalry).Count;
                if (cav * 3 < ArmyCount(g)) stable.TryTrain(GameConfig.DeathKnight);
            }

            // 6. 兵力到阈值就压一波，每波规模递增
            var force = g.units.FindAll(u => u.team == Team.Enemy && !u.def.worker);
            int need = 6 + wave * 2;
            var targetHall = g.Hall(Team.Player);
            if (targetHall != null && force.Count >= need)
            {
                wave++;
                foreach (var u in force) u.CommandAttack(targetHall);
            }
        }
        // 战斗兵种总数（不含工人），用于 步:弓:骑 ≈ 3:2:2 的比例控制
        static int ArmyCount(Game g) => g.units.FindAll(u => u.team == Team.Enemy && !u.def.worker).Count;
    }
}
