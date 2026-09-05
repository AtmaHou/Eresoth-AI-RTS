using System.Collections.Generic;
using UnityEngine;

namespace Eresoth
{
    /// <summary>第一阶段脚本 AI：采集 → 建军 → 攀科技 → 混编暴兵（含英雄）→ 分波进攻。
    /// 操控与玩家相对的阵营；后续将被"执行体 AI + LLM 参谋"双层结构替换（设计文档 5.1）。</summary>
    public class EnemyAI : MonoBehaviour
    {
        float t;
        int wave;

        // AI 阵营 = 玩家的对手方；其兵种/建筑按该阵营种族取对应定义
        static Team Ai => Game.I.playerTeam == Team.Player ? Team.Enemy : Team.Player;

        static bool IsAi(Unit u) => u.team == Ai;

        void Update()
        {
            var g = Game.I;
            if (g.over) return;
            t -= Time.deltaTime;
            if (t > 0) return;
            t = 2f; // 每 2 秒一轮决策

            var ai = Ai;
            int aiIdx = (int)ai;
            var humanSide = ai == Team.Player;   // AI 是否使用人类种族（玩家选了不死）

            var hall = g.Hall(ai);
            if (hall == null) return;

            // 该阵营的三个兵营定义与 kind
            var infB = humanSide ? GameConfig.Barracks : GameConfig.Crypt;
            var rngB = humanSide ? GameConfig.Archery : GameConfig.DarkTemple;
            var cavB = humanSide ? GameConfig.Stable : GameConfig.DeathStable;
            var infKind = humanSide ? "barracks" : "crypt";
            var rngKind = humanSide ? "archery" : "dark_temple";
            var cavKind = humanSide ? "stable" : "death_stable";
            var workerDef = humanSide ? GameConfig.Farmer : GameConfig.Acolyte;
            var infUnit = humanSide ? GameConfig.Footman : GameConfig.Skeleton;
            var rngUnit = humanSide ? GameConfig.Archer : GameConfig.DarkArcher;
            var cavUnit = humanSide ? GameConfig.Knight : GameConfig.DeathKnight;
            var heroUnit = humanSide ? GameConfig.LordKnight : GameConfig.DeathRanger;
            var atkTech = humanSide ? "human_atk" : "undead_atk";
            var defTech = humanSide ? "human_def" : "undead_def";

            // 1. 采集分配：空闲工人去伐木，缺矿时派 2 个采水晶
            var workers = g.units.FindAll(u => IsAi(u) && u.def.worker);
            bool needMana = g.mana[aiIdx] < 40;
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
            if (workers.Count < 8) hall.TryTrain(workerDef);

            // 3. 建筑顺序：步兵兵营 → 远程兵营 → 骑兵兵营 → 伐木场（预留 50 木暴兵）
            if (g.BuildingOfKind(ai, infKind) == null
                && g.wood[aiIdx] >= infB.wood + 50)
                g.BuildStructure(ai, infB);
            else if (g.BuildingOfKind(ai, rngKind) == null
                && g.wood[aiIdx] >= rngB.wood + 50)
                g.BuildStructure(ai, rngB);
            else if (g.BuildingOfKind(ai, cavKind) == null
                && g.wood[aiIdx] >= cavB.wood + 50)
                g.BuildStructure(ai, cavB);
            else if (g.BuildingOfKind(ai, "lumber") == null
                && g.wood[aiIdx] >= GameConfig.Lumber.wood + 50)
                g.BuildStructure(ai, GameConfig.Lumber);   // 伐木场：采集 +50%

            // 4. 攀科技：木富余时轮流研究攻防（步兵兵营，单研究槽）
            var inf = g.BuildingOfKind(ai, infKind);
            if (inf != null && inf.research == null && g.wood[aiIdx] > 250)
            {
                int atk = g.TechLevel(ai, TechEffect.Atk);
                int def = g.TechLevel(ai, TechEffect.Def);
                inf.TryResearch(atk <= def ? atkTech : defTech);
            }

            // 5. 暴兵：步兵兵营出步兵（富余时训练英雄，全场唯一）；富矿补远程/骑兵（步:弓:骑 ≈ 3:2:2）
            if (inf != null)
            {
                inf.TryTrain(infUnit);
                bool heroAlive = g.units.Exists(u => IsAi(u) && u.def.hero);
                if (!heroAlive && g.wood[aiIdx] > heroUnit.wood + 100 && g.mana[aiIdx] > heroUnit.mana)
                    inf.TryTrain(heroUnit);
            }
            var rng = g.BuildingOfKind(ai, rngKind);
            if (rng != null && g.mana[aiIdx] > 30)
            {
                int ranged = g.units.FindAll(u => IsAi(u) && u.def.kind == UnitKind.Ranged).Count;
                if (ranged * 3 < ArmyCount(g)) rng.TryTrain(rngUnit);
            }
            var cav = g.BuildingOfKind(ai, cavKind);
            if (cav != null && g.mana[aiIdx] > 60)
            {
                int count = g.units.FindAll(u => IsAi(u) && u.def.kind == UnitKind.Cavalry).Count;
                if (count * 3 < ArmyCount(g)) cav.TryTrain(cavUnit);
            }

            // 6. 兵力到阈值就压一波，每波规模递增
            var force = g.units.FindAll(u => IsAi(u) && !u.def.worker);
            int need = 6 + wave * 2;
            var targetHall = g.Hall(g.playerTeam);
            if (targetHall != null && force.Count >= need)
            {
                wave++;
                foreach (var u in force) u.CommandAttack(targetHall);
            }
        }

        // 战斗兵种总数（不含工人），用于 步:弓:骑 ≈ 3:2:2 的比例控制
        static int ArmyCount(Game g) => g.units.FindAll(u => IsAi(u) && !u.def.worker).Count;
    }
}
