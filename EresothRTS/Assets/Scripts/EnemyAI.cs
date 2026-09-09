using System.Collections.Generic;
using UnityEngine;

namespace Eresoth
{
    /// <summary>第一阶段脚本 AI：采集 → 建军 → 攀科技 → 按克制配比持续暴兵（含英雄）→ 集结成波 → 多轮进攻。
    /// 波次管理：兵力到阈值即集结一波压上；进攻中新兵持续增援；残部回撤重整后再起下一波，规模逐波递增；
    /// 目标被摧毁后自动续打最近玩家建筑，保证持续多轮进攻压力。
    /// 兵种搭配：目标编制 = 基础配比（步:弓:骑 ≈ .38:.32:.30）按玩家兵种构成向克制兵种偏移
    /// （步克骑、弓克步、骑克弓），每类限幅 15%~55% 保持混合编制；缺口最大的兵种优先且豁免经济门槛。
    /// 战术行为：基地遇袭全军回防、集结期派 2 骑兵骚扰暴露的采集工人。
    /// 后续将被"执行体 AI + LLM 参谋"双层结构替换（设计文档 5.1）。</summary>
    public class EnemyAI : MonoBehaviour
    {
        float t;
        int wave;                        // 已发起的进攻波次（决定下一波规模）
        float assaultCooldown;           // 波次间隔冷却
        bool assaulting;                 // true=进攻阶段（已压上），false=集结阶段
        float assaultTimer;              // 本波进攻已进行时长
        ITargetable assaultTarget;       // 本波目标
        Vector3 rally;                   // 集结点：主基地通往玩家方向的缓冲带

        // AI 阵营 = 玩家的对手方；其兵种/建筑按该阵营种族取对应定义
        static Team Ai => Game.I.playerTeam == Team.Player ? Team.Enemy : Team.Player;

        static bool IsAi(Unit u) => u.team == Ai;

        void Update()
        {
            var g = Game.I;
            if (g == null || g.over) return;
            t -= Time.deltaTime;
            assaultCooldown -= Time.deltaTime;
            if (t > 0) return;
            t = GameConfig.AiDecisionInterval; // 每 2 秒一轮决策

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

            // 集结点：主基地朝向玩家一侧 10 格
            var pHall0 = g.Hall(g.playerTeam);
            Vector3 toPlayer = pHall0 != null ? pHall0.transform.position - hall.transform.position : Vector3.forward;
            toPlayer.y = 0;
            rally = hall.transform.position + (toPlayer.sqrMagnitude > .01f ? toPlayer.normalized : Vector3.forward) * 10f;

            // 1. 采集分配：根据当前库存与后续建造/训练需求动态调整 wood/mana 比例
            AssignGatherers(g, ai, aiIdx, humanSide, infB, rngB, cavB);

            // 2. 补工人（上限取自配置）
            int workerCount = g.units.FindAll(u => IsAi(u) && u.def.worker).Count;
            if (workerCount < GameConfig.AiMaxWorkers) hall.TryTrain(workerDef);

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

            // 本波规模：逐波递增直至上限（wave 在每次发起进攻时 +1）
            int need = Mathf.Min(GameConfig.AiMaxAssaultForce,
                GameConfig.AiMinAssaultForce + wave * 2);

            // 6. 持续暴兵：目标编制 = 克制配比 × 本波规模，按各类缺口训练（混合搭配 + 战损补充 + 针对玩家）
            var rng = g.BuildingOfKind(ai, rngKind);
            var cav = g.BuildingOfKind(ai, cavKind);
            {
                int aInf = 0, aRng = 0, aCav = 0;
                foreach (var u in g.units)
                {
                    if (!IsAi(u) || u.def.worker) continue;
                    if (u.def.kind == UnitKind.Infantry) aInf++;
                    else if (u.def.kind == UnitKind.Ranged) aRng++;
                    else if (u.def.kind == UnitKind.Cavalry) aCav++;
                }

                // 期望配比（x=步 y=弓 z=骑），只对已建军营的兵种归一
                Vector3 w = DesiredComp(pInf, pRng, pCav);
                float wInf = inf != null ? w.x : 0f, wRng = rng != null ? w.y : 0f, wCav = cav != null ? w.z : 0f;
                float wSum = wInf + wRng + wCav;
                if (wSum <= 0f)   // 兵营全无（理论兜底）：平均分给将有的
                { wInf = inf != null ? 1f : 0f; wRng = rng != null ? 1f : 0f; wCav = cav != null ? 1f : 0f; wSum = wInf + wRng + wCav; }

                // 各类缺口（目标数 - 现有数）；缺口 ≥1 才训练，天然控制混合比例
                float dInf = wInf / wSum * need - aInf;
                float dRng = wRng / wSum * need - aRng;
                float dCav = wCav / wSum * need - aCav;
                // 缺口最大者 = 当前最急需的克制兵种，豁免经济门槛；其余保留门槛防早期断矿
                if (dInf >= 1f) inf.TryTrain(infUnit);
                if (rng != null && dRng >= 1f && (dRng >= dInf && dRng >= dCav || g.mana[aiIdx] > 80))
                    rng.TryTrain(rngUnit);
                if (cav != null && dCav >= 1f && (dCav >= dInf && dCav >= dRng || g.mana[aiIdx] > 140))
                    cav.TryTrain(cavUnit);
            }

            // 英雄：场上或队列中都没有时才训练（全场唯一，死亡后可再训）
            if (inf != null)
            {
                bool heroBusy = g.units.Exists(u => IsAi(u) && u.def.hero)
                    || g.buildings.Exists(b => b.team == ai && b.queue.Exists(d => d.hero));
                if (!heroBusy && g.wood[aiIdx] > heroUnit.wood + 100 && g.mana[aiIdx] > heroUnit.mana)
                    inf.TryTrain(heroUnit);
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

            if (!assaulting)
            {
                // 8a. 集结：把基地附近的散兵拢到集结点成波（到达后 hasMoveOrder 自动清除，等待出兵）
                foreach (var u in force)
                {
                    if (u.target != null || u.busy) continue;
                    if (Vector3.Distance(u.transform.position, rally) > 5f)
                        u.CommandMove(rally + Jitter(3f));
                }

                // 8b. 骚扰（仅集结期）：派最近的 2 个骑兵杀暴露在外的采集工人
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

                // 8c. 出兵：兵力到阈值且冷却结束即压上一波
                if (force.Count >= need && assaultCooldown <= 0f)
                {
                    var tgt = PickTarget(g, ai, force, pHall);
                    if (tgt != null)
                    {
                        assaulting = true;
                        assaultTimer = 0f;
                        assaultTarget = tgt;
                        wave++;
                        foreach (var u in force) u.CommandAttack(tgt);
                    }
                }
            }
            else
            {
                // 9a. 目标失效 → 续打离集结点最近的玩家成品建筑（保证多轮连续施压）
                if (assaultTarget == null || !assaultTarget.Alive)
                    assaultTarget = NextAssaultTarget(g, ai);
                // 9b. 增援与续攻：交战中的不打断；基地附近新兵/残部统一指向本波目标
                if (assaultTarget != null)
                    foreach (var u in force)
                    {
                        if (u.target != null && u.target.Alive) continue;
                        u.CommandAttack(assaultTarget);
                    }
                // 9c. 撤退：进攻超过 8 秒后，"仍在战场"（正在交战或已远离基地推进）的兵力
                //     不足本波 1/3，或一波拖超过 60 秒，则残部回撤集结点，冷却后再起下一波
                assaultTimer += GameConfig.AiDecisionInterval;
                if (assaultTimer > 8f)
                {
                    int inField = 0;
                    foreach (var u in force)
                        if ((u.target != null && u.target.Alive)
                            || Vector3.Distance(u.transform.position, hall.transform.position) > 30f) inField++;
                    if (inField <= Mathf.Max(2, need / 3) || assaultTimer > 60f)
                    {
                        assaulting = false;
                        assaultCooldown = GameConfig.AiAssaultInterval;
                        assaultTarget = null;
                        foreach (var u in force) u.CommandMove(rally + Jitter(3f));
                    }
                }
            }
        }

        // 集结/撤退时的分散落点，避免叠成一个点
        static Vector3 Jitter(float r)
        {
            var c = Random.insideUnitCircle * r;
            return new Vector3(c.x, 0, c.y);
        }

        /// <summary>期望兵种配比（x=步 y=弓 z=骑）：基础 .38/.32/.30，按玩家兵种构成向克制兵种偏移
        /// （步克骑、弓克步、骑克弓，玩家某类 ≥3 才启用），每类限幅 15%~55% 保持混合编制。</summary>
        static Vector3 DesiredComp(int pInf, int pRng, int pCav)
        {
            Vector3 w = new(.38f, .32f, .30f);
            float total = pInf + pRng + pCav;
            if (total >= 3f)
            {
                const float pull = .8f;
                w.x += pull * pCav / total;   // 玩家骑兵多 → 补步兵（步克骑）
                w.y += pull * pInf / total;   // 玩家步兵多 → 补远程（弓克步）
                w.z += pull * pRng / total;   // 玩家远程多 → 补骑兵（骑克弓）
            }
            w.x = Mathf.Clamp(w.x, .15f, .55f);
            w.y = Mathf.Clamp(w.y, .15f, .55f);
            w.z = Mathf.Clamp(w.z, .15f, .55f);
            return w / (w.x + w.y + w.z);
        }

        /// <summary>本波首发目标：优先打玩家资源收集站（兵力 <14 时），其次清玩家主基地附近的守军
        /// （≥4 人时），否则直冲主基地；主基地已灭则改打最近玩家建筑。</summary>
        ITargetable PickTarget(Game g, Team ai, List<Unit> force, Building pHall)
        {
            var enemyHub = g.buildings.Find(b => b.team == g.playerTeam && b.kind == "resource_hub" && !b.constructing);
            if (enemyHub != null && force.Count < 14) return enemyHub;
            if (pHall != null)
            {
                Unit nearDef = null; float nd = 22f;
                foreach (var u in g.units)
                {
                    if (u.team == ai || u.def.worker) continue;
                    float d = Vector3.Distance(u.transform.position, pHall.transform.position);
                    if (d < nd) { nd = d; nearDef = u; }
                }
                if (nearDef != null && CountNear(pHall.transform.position, 22f) >= 4) return nearDef;
                return pHall;
            }
            return NextAssaultTarget(g, ai);
        }

        /// <summary>当前目标被摧毁后的续攻目标：离集结点最近的玩家成品建筑；无则 null（由 CheckEnd 收场）。</summary>
        ITargetable NextAssaultTarget(Game g, Team ai)
        {
            Building best = null; float bd = float.MaxValue;
            foreach (var b in g.buildings)
            {
                if (b.team == ai || b.constructing) continue;
                float d = Vector3.Distance(b.transform.position, rally);
                if (d < bd) { bd = d; best = b; }
            }
            return best;
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

        /// <summary>根据库存与建筑/训练需求，动态分配空闲工人采集 wood 或 mana。</summary>
        void AssignGatherers(Game g, Team ai, int aiIdx, bool humanSide,
            BuildingDef infB, BuildingDef rngB, BuildingDef cavB)
        {
            var workers = g.units.FindAll(u => IsAi(u) && u.def.worker);
            if (workers.Count == 0) return;

            // 计算后续想建造的建筑资源需求（只考虑待建建筑，训练需求动态变化，不提前占用分工）
            float woodNeed = 0, manaNeed = 0;
            void Need(BuildingDef d) { if (d != null) { woodNeed += d.wood; manaNeed += d.mana; } }

            var inf = g.BuildingOfKind(ai, humanSide ? "barracks" : "crypt");
            var rng = g.BuildingOfKind(ai, humanSide ? "archery" : "dark_temple");
            var cav = g.BuildingOfKind(ai, humanSide ? "stable" : "death_stable");

            if (inf == null) Need(infB);
            if (rng == null) Need(rngB);
            if (cav == null) Need(cavB);
            if (g.buildings.FindAll(b => b.team == ai && b.kind == "resource_hub").Count < 2) Need(GameConfig.Lumber);
            if (g.PopCount(aiIdx) > g.PopCap(ai) - 8) Need(GameConfig.House);

            // 当前库存缺口
            float woodGap = Mathf.Max(0, woodNeed - g.wood[aiIdx]);
            float manaGap = Mathf.Max(0, manaNeed - g.mana[aiIdx]);
            float totalGap = woodGap + manaGap;

            // 基础目标比例：按建筑缺口分配
            float manaRatio = totalGap > 0 ? manaGap / totalGap : 0.5f;

            // 库存紧缺修正：若木头库存占比明显偏低，则强制提高采木优先级，避免木头卡住建筑进度
            float totalStock = g.wood[aiIdx] + g.mana[aiIdx];
            float stockWoodRatio = totalStock > 0 ? g.wood[aiIdx] / totalStock : 0.5f;
            if (stockWoodRatio < 0.30f) manaRatio = Mathf.Min(manaRatio, 0.50f);
            if (stockWoodRatio < 0.20f) manaRatio = Mathf.Min(manaRatio, 0.40f);

            // 保证至少 35% 工人采木、最多 65% 采魔，避免早期过度偏向魔法矿而断木
            manaRatio = Mathf.Clamp(manaRatio, 0.35f, 0.65f);
            int targetMana = Mathf.RoundToInt(workers.Count * manaRatio);
            targetMana = Mathf.Clamp(targetMana, 1, workers.Count - 1);

            int toMana = 0, toWood = 0;
            foreach (var w in workers)
            {
                var wk = w.GetComponent<Worker>();
                if (wk == null) continue;
                // 只动真正空闲的工人；已在路上的保持原目标（避免反复切换）
                if (wk.node != null || wk.state != Worker.State.Idle) continue;

                string kind;
                if (toMana < targetMana) { kind = "mana"; toMana++; }
                else { kind = "wood"; toWood++; }

                var n = g.NearestNode(kind, w.transform.position);
                if (n != null) wk.GatherAt(n);
            }
        }

        /// <summary>建筑建成后由 Building 回调，立即把释放出来的空闲工人重新投入采集。</summary>
        public void OnBuildingCompleted(Team team)
        {
            if (team != Ai) return;
            var g = Game.I;
            if (g == null || g.over) return;
            var ai = Ai;
            int aiIdx = (int)ai;
            bool humanSide = ai == Team.Player;
            var infB = humanSide ? GameConfig.Barracks : GameConfig.Crypt;
            var rngB = humanSide ? GameConfig.Archery : GameConfig.DarkTemple;
            var cavB = humanSide ? GameConfig.Stable : GameConfig.DeathStable;
            AssignGatherers(g, ai, aiIdx, humanSide, infB, rngB, cavB);
        }
    }
}
