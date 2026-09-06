using System.Collections.Generic;
using UnityEngine;

namespace Eresoth
{
    /// <summary>第一阶段脚本 AI：采集 → 建军 → 攀科技 → 针对性暴兵（含英雄）→ 分波进攻。
    /// 战术行为：基地遇袭全军回防、骑兵骚扰采集工人、按玩家兵种构成出克制兵种、
    /// 玩家基地有重兵时先打部队。后续将被"执行体 AI + LLM 参谋"双层结构替换（设计文档 5.1）。</summary>
    public class EnemyAI : MonoBehaviour
    {
        float t;
        int wave;
        float assaultCooldown;

        // AI 阵营 = 玩家的对手方；其兵种/建筑按该阵营种族取对应定义
        static Team Ai => Game.I.playerTeam == Team.Player ? Team.Enemy : Team.Player;

        static bool IsAi(Unit u) => u.team == Ai;

        void Update()
        {
            var g = Game.I;
            if (g.over) return;
            t -= Time.deltaTime;
            assaultCooldown -= Time.deltaTime;
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

            // 1. 采集分配：魔法矿是主经济，木材只保留少量工人采集
            var workers = g.units.FindAll(u => IsAi(u) && u.def.worker);
            bool needMana = g.mana[aiIdx] < 260;
            int toMana = 0;
            foreach (var w in workers)
            {
                var wk = w.GetComponent<Worker>();
                if (wk.node == null && wk.state == Worker.State.Idle)
                {
                    string kind = (needMana || toMana < Mathf.Max(2, workers.Count - 2)) ? "mana" : "wood";
                    if (kind == "mana") toMana++;
                    var n = g.NearestNode(kind, w.transform.position);
                    if (n != null) wk.GatherAt(n);
                }
            }

            // 2. 补工人
            if (workers.Count < 8) hall.TryTrain(workerDef);

            // 3. 建筑顺序：兵种建筑 → 资源收集站分矿 → 民居（人口快满时）
            if (g.BuildingOfKind(ai, infKind) == null
                && g.wood[aiIdx] >= infB.wood && g.mana[aiIdx] >= infB.mana)
                g.BuildStructure(ai, infB);
            else if (g.BuildingOfKind(ai, rngKind) == null
                && g.wood[aiIdx] >= rngB.wood && g.mana[aiIdx] >= rngB.mana)
                g.BuildStructure(ai, rngB);
            else if (g.BuildingOfKind(ai, cavKind) == null
                && g.wood[aiIdx] >= cavB.wood && g.mana[aiIdx] >= cavB.mana)
                g.BuildStructure(ai, cavB);
            else if (g.buildings.FindAll(b => b.team == ai && b.kind == "resource_hub").Count < 2
                && g.wood[aiIdx] >= GameConfig.Lumber.wood && g.mana[aiIdx] >= GameConfig.Lumber.mana)
                g.BuildForwardResourceHub(ai);             // 远端收集站：主矿/分矿交付与采集加成
            else if (g.PopCount(aiIdx) > g.PopCap(ai) - 8
                && g.wood[aiIdx] >= GameConfig.House.wood && g.mana[aiIdx] >= GameConfig.House.mana)
                g.BuildStructure(ai, GameConfig.House);    // 民居：人口快满时扩容

            // 4. 攀科技：木富余时轮流研究攻防（步兵兵营，单研究槽）
            var inf = g.BuildingOfKind(ai, infKind);
            if (inf != null && inf.research == null && g.mana[aiIdx] > 220)
            {
                int atk = g.TechLevel(ai, TechEffect.Atk);
                int def = g.TechLevel(ai, TechEffect.Def);
                inf.TryResearch(atk <= def ? atkTech : defTech);
            }

            // 5. 情报：统计玩家军队构成（不含工人），用于针对性出兵与骚扰
            int pInf = 0, pRng = 0, pCav = 0;
            Unit loneWorker = null; float loneD = 0f;   // 离玩家主基地最远的采集工人
            var pHall = g.Hall(g.playerTeam);
            foreach (var u in g.units)
            {
                if (u.team != g.playerTeam) continue;
                if (!u.def.worker)
                {
                    if (u.def.kind == UnitKind.Infantry) pInf++;
                    else if (u.def.kind == UnitKind.Ranged) pRng++;
                    else if (u.def.kind == UnitKind.Cavalry) pCav++;
                    continue;
                }
                if (pHall == null) continue;
                var wk = u.GetComponent<Worker>();
                if (wk == null || wk.node == null) continue;    // 只在采集中的工人才算暴露
                float d = Vector3.Distance(u.transform.position, pHall.transform.position);
                if (d > 26f && d > loneD) { loneD = d; loneWorker = u; }
            }

            // 威胁最大的玩家兵种 ≥4 时，优先训练克制兵种（步克骑、弓克步、骑克弓）
            UnitKind focus = UnitKind.Infantry; int top = pInf;
            if (pRng > top) { focus = UnitKind.Ranged; top = pRng; }
            if (pCav > top) { focus = UnitKind.Cavalry; top = pCav; }
            // focus 是"威胁最大的兵种"，克制方 = 被威胁方反过来：玩家远程多→我爆骑兵 等
            UnitKind counter = focus == UnitKind.Ranged ? UnitKind.Cavalry
                : focus == UnitKind.Cavalry ? UnitKind.Infantry : UnitKind.Ranged;
            bool counterMode = top >= 4;

            // 6. 暴兵：克制模式下只出克制兵种；平时步:弓:骑 ≈ 3:2:2（步兵兵营富余时训练英雄，全场唯一）
            if (inf != null)
            {
                if (!counterMode || counter == UnitKind.Infantry) inf.TryTrain(infUnit);
                bool heroAlive = g.units.Exists(u => IsAi(u) && u.def.hero);
                if (!heroAlive && g.wood[aiIdx] > heroUnit.wood + 100 && g.mana[aiIdx] > heroUnit.mana)
                    inf.TryTrain(heroUnit);
            }
            var rng = g.BuildingOfKind(ai, rngKind);
            if (rng != null && g.mana[aiIdx] > 80 && (!counterMode || counter == UnitKind.Ranged))
            {
                int ranged = g.units.FindAll(u => IsAi(u) && u.def.kind == UnitKind.Ranged).Count;
                if (ranged * 3 < ArmyCount(g)) rng.TryTrain(rngUnit);
            }
            var cav = g.BuildingOfKind(ai, cavKind);
            if (cav != null && g.mana[aiIdx] > 140 && (!counterMode || counter == UnitKind.Cavalry))
            {
                int count = g.units.FindAll(u => IsAi(u) && u.def.kind == UnitKind.Cavalry).Count;
                if (count * 3 < ArmyCount(g)) cav.TryTrain(cavUnit);
            }

            var force = g.units.FindAll(u => IsAi(u) && !u.def.worker);

            // 7. 基地遇袭：玩家军队打进 30 格内时全军优先回防剿灭入侵者
            Unit invader = null; float ivd = 30f;
            foreach (var u in g.units)
            {
                if (u.team == ai || u.def.worker) continue;
                float d = Vector3.Distance(u.transform.position, hall.transform.position);
                if (d < ivd) { ivd = d; invader = u; }
            }
            if (invader != null)
            {
                foreach (var u in force) u.CommandAttack(invader);
                return;   // 每 2 秒重评估，入侵者清完自然回落到原逻辑
            }

            // 8. 骚扰：有 ≥2 骑兵时，派最近的 2 个去杀暴露在外的采集工人
            if (loneWorker != null)
            {
                var raiders = force.FindAll(u => u.def.kind == UnitKind.Cavalry);
                if (raiders.Count >= 2)
                {
                    raiders.Sort((a, b) =>
                        Vector3.Distance(a.transform.position, loneWorker.transform.position)
                        .CompareTo(Vector3.Distance(b.transform.position, loneWorker.transform.position)));
                    raiders[0].CommandAttack(loneWorker);
                    raiders[1].CommandAttack(loneWorker);
                }
            }

            // 9. 兵力到阈值就压一波，每波规模递增；若玩家基地附近有重兵，先打部队而非直冲主基地
            int need = Mathf.Min(GameConfig.AiMaxAssaultForce,
                GameConfig.AiMinAssaultForce + (wave % 4) * 2);
            if (pHall != null && force.Count >= need && assaultCooldown <= 0f)
            {
                Unit nearDef = null; float nd = 22f;
                foreach (var u in g.units)
                {
                    if (u.team == ai || u.def.worker) continue;
                    float d = Vector3.Distance(u.transform.position, pHall.transform.position);
                    if (d < nd) { nd = d; nearDef = u; }
                }
                wave++;
                var enemyHub = g.buildings.Find(b => b.team == g.playerTeam && b.kind == "resource_hub" && !b.constructing);
                ITargetable tgt = enemyHub != null && force.Count < 14 ? enemyHub
                    : nearDef != null && CountNear(pHall.transform.position, 22f) >= 4 ? nearDef : pHall;
                foreach (var u in force) u.CommandAttack(tgt);
                assaultCooldown = GameConfig.AiAssaultInterval;
            }
        }

        // 玩家战斗单位在 pos 半径内的数量
        int CountNear(Vector3 pos, float r)
        {
            int c = 0;
            foreach (var u in Game.I.units)
                if (u.team == Game.I.playerTeam && !u.def.worker
                    && Vector3.Distance(u.transform.position, pos) < r) c++;
            return c;
        }

        // 战斗兵种总数（不含工人），用于 步:弓:骑 ≈ 3:2:2 的比例控制
        static int ArmyCount(Game g) => g.units.FindAll(u => IsAi(u) && !u.def.worker).Count;
    }
}
