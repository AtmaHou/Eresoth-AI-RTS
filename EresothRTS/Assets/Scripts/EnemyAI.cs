using System.Collections.Generic;
using UnityEngine;

namespace Eresoth
{
    /// <summary>第一阶段脚本 AI：采集 → 造兵营 → 暴兵 → 分波进攻。
    /// 后续将被"执行体 AI + LLM 参谋"双层结构替换（设计文档 5.1）。</summary>
    public class EnemyAI : MonoBehaviour
    {
        float t;
        int wave;
        bool barracksBuilt;

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

            // 3. 造兵营
            if (!barracksBuilt && g.Barracks(Team.Enemy) == null && g.wood[1] >= GameConfig.BarracksWood)
            {
                if (g.TrySpend(1, GameConfig.BarracksWood, 0))
                {
                    Building.Spawn(Team.Enemy, "barracks", g.baseCenter[1] + new Vector3(-9, 0, -5));
                    barracksBuilt = true;
                }
            }

            // 4. 暴兵：有钱憎恶，缺矿食尸鬼，兜底骷髅海
            var bar = g.Barracks(Team.Enemy);
            if (bar != null)
            {
                if (g.mana[1] > 70 && g.wood[1] > 150) bar.TryTrain(GameConfig.Abom);
                else if (g.mana[1] > 5) bar.TryTrain(GameConfig.Ghoul);
                else bar.TryTrain(GameConfig.Skeleton);
            }

            // 5. 兵力到阈值就压一波，每波规模递增
            var army = g.units.FindAll(u => u.team == Team.Enemy && !u.def.worker);
            int need = 6 + wave * 2;
            var targetHall = g.Hall(Team.Player);
            if (targetHall != null && army.Count >= need)
            {
                wave++;
                foreach (var u in army) u.CommandAttack(targetHall);
            }
        }
    }
}
