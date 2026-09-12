using System.Text;
using UnityEngine;

namespace Eresoth
{
    /// <summary>态势摘要构造器：把战场状态压缩成 1~2K token 的 JSON 供 LLM 参考。
    /// 要点：位置用最近语义点表示（不发明坐标）；词表每次现拼，模型只看到本局真实存在的东西。</summary>
    public static class StateDigestBuilder
    {
        /// <summary>生成完整态势摘要 JSON（手写拼串，避免 DTO 序列化的样板与冗余字段）。</summary>
        public static string Build(Team team)
        {
            var g = Game.I;
            var sb = new StringBuilder(2048);
            int pi = (int)team;

            sb.Append("{\"t\":").Append((int)Time.time);
            // 资源
            sb.Append(",\"resources\":{\"wood\":").Append(g.wood[pi])
              .Append(",\"mana\":").Append(g.mana[pi])
              .Append(",\"pop\":\"").Append(g.PopCount(pi)).Append('/').Append(g.PopCap(team)).Append("\"}");

            // 军团
            sb.Append(",\"forces\":[");
            bool first = true;
            if (ForceManager.I != null)
                foreach (var f in ForceManager.I.OfTeam(team))
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"id\":\"").Append(f.id).Append("\",\"name\":\"").Append(f.name)
                      .Append("\",\"at\":\"").Append(NearestPointId(f.Centroid)).Append("\"");
                    sb.Append(",\"comp\":{");
                    var comp = f.Composition();
                    bool cf = true;
                    foreach (var kv in comp)
                    {
                        if (!cf) sb.Append(',');
                        cf = false;
                        sb.Append('\"').Append(kv.Key.ToString().ToLower()).Append("\":").Append(kv.Value);
                    }
                    sb.Append("},\"hp\":").Append(f.HealthRatio.ToString("0.00"));
                    sb.Append(",\"order\":\"").Append(f.currentOrder != null && !f.currentOrder.IsTerminal
                        ? f.currentOrder.Describe() : "待命").Append("\"}");
                }
            sb.Append(']');

            // 建筑统计
            sb.Append(",\"buildings\":{");
            var bcnt = new System.Collections.Generic.Dictionary<string, int>();
            foreach (var b in g.buildings)
                if (b != null && b.Alive && b.team == team)
                    bcnt[b.kind] = bcnt.TryGetValue(b.kind, out var c) ? c + 1 : 1;
            bool bf = true;
            foreach (var kv in bcnt)
            {
                if (!bf) sb.Append(',');
                bf = false;
                sb.Append('\"').Append(kv.Key).Append("\":").Append(kv.Value);
            }
            sb.Append('}');

            // 敌方情报：战争迷雾开启时只统计当前看得见的敌人（参谋不知道=不存在），关闭时为全图
            Team enemy = team == Team.Player ? Team.Enemy : Team.Player;
            int enemyCount = 0;
            foreach (var u in g.units)
                if (u != null && u.Alive && u.team == enemy && !u.def.worker
                    && FogOfWarManager.VisibleToPlayer(u.transform.position)) enemyCount++;
            sb.Append(",\"enemy_intel\":{\"fog\":").Append(FogOfWarManager.Active ? "true" : "false")
              .Append(",\"visible_count\":").Append(enemyCount)
              .Append(",\"frontline\":\"").Append(
                  SemanticMap.TryGet("enemy_frontline", out var fl) ? NearestPointId(fl.pos) : "unknown")
              .Append("\"}");

            // 最近事件（60 秒内，最多 8 条）
            sb.Append(",\"recent_events\":[");
            var evs = GameEventBus.Recent(team, Time.time - 60f);
            int shown = 0;
            for (int i = evs.Count - 1; i >= 0 && shown < 8; i--, shown++)
            {
                if (shown > 0) sb.Append(',');
                sb.Append('\"').Append(Escape(evs[i].detail ?? "")).Append('\"');
            }
            sb.Append(']');

            // 动态词表
            sb.Append(",\"vocab\":{");
            sb.Append("\"actions\":[\"move\",\"attack\",\"attack_move\",\"defend\",\"retreat\",\"focus_fire\",\"regroup\",\"hold\"],");
            sb.Append("\"economy_actions\":[\"train\",\"research\",\"build\",\"assign_workers\"],");
            sb.Append("\"metrics\":[\"enemy_count_near\",\"ally_health_ratio\",\"resource\",\"building_hp_ratio\",\"enemy_visible\",\"time_elapsed\"],");
            sb.Append("\"targets\":[");
            for (int i = 0; i < SemanticMap.All.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('\"').Append(SemanticMap.All[i].id).Append('\"');
            }
            sb.Append("],\"forces_list\":[");
            if (ForceManager.I != null)
            {
                var fs = ForceManager.I.OfTeam(team);
                for (int i = 0; i < fs.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append("\"").Append(fs[i].id).Append(':').Append(fs[i].name).Append("\"");
                }
            }
            sb.Append("],");
            AppendUnits(sb, team);
            AppendTechs(sb, team);
            AppendBuildings(sb, team);
            sb.Append("}}");
            return sb.ToString();
        }

        static void AppendUnits(StringBuilder sb, Team team)
        {
            // 当前阵营可训练兵种（从建筑表反查，保证与当局可用能力一致）
            sb.Append("\"units\":[");
            var seen = new System.Collections.Generic.HashSet<string>();
            bool first = true;
            foreach (var kv in RuntimeConfig.Buildings)
            {
                if (kv.Value.train == null) continue;
                if (!BelongsToFaction(kv.Key, team)) continue;
                foreach (var ud in kv.Value.train)
                {
                    if (!seen.Add(ud.id)) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"id\":\"").Append(ud.id).Append("\",\"name\":\"").Append(ud.name)
                      .Append("\",\"kind\":\"").Append(ud.kind.ToString().ToLower())
                      .Append("\",\"cost\":\"").Append(ud.wood).Append("木+").Append(ud.mana).Append("矿\"}");
                }
            }
            sb.Append(']');
        }

        static void AppendTechs(StringBuilder sb, Team team)
        {
            sb.Append("\"techs\":[");
            bool first = true;
            bool humanSide = IsHumanSide(team);
            foreach (var kv in GameConfig.Techs)
            {
                // 阵营匹配：人类阵营用 human_*，不死用 undead_*
                if (kv.Key.StartsWith("human") != humanSide) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"id\":\"").Append(kv.Key).Append("\",\"name\":\"").Append(kv.Value.name).Append("\"}");
            }
            sb.Append(']');
        }

        static void AppendBuildings(StringBuilder sb, Team team)
        {
            sb.Append("\"buildable\":[");
            var kinds = IsHumanSide(team)
                ? RuntimeConfig.Data.factionBuildings.humanBuildingKinds
                : RuntimeConfig.Data.factionBuildings.undeadBuildingKinds;
            bool first = true;
            foreach (var k in kinds)
            {
                if (!RuntimeConfig.Buildings.TryGetValue(k, out var bd)) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"kind\":\"").Append(k).Append("\",\"name\":\"").Append(bd.name).Append("\"}");
            }
            sb.Append(']');
        }

        static bool IsHumanSide(Team team)
        {
            // 阵营种族判定：看该阵营工人是农夫还是侍僧
            foreach (var u in Game.I.units)
                if (u != null && u.Alive && u.team == team && u.def.worker)
                    return u.def.id == "farmer";
            return true;
        }

        static bool BelongsToFaction(string buildingKind, Team team)
        {
            var list = IsHumanSide(team)
                ? RuntimeConfig.Data.factionBuildings.humanBuildingKinds
                : RuntimeConfig.Data.factionBuildings.undeadBuildingKinds;
            return list.Contains(buildingKind) || buildingKind == "hall";
        }

        /// <summary>离 pos 最近的语义点 ID（部队位置的文字化）。</summary>
        public static string NearestPointId(Vector3 pos)
        {
            string best = "unknown"; float bd = 20f;   // 超过 20 格就不归属任何语义点
            foreach (var p in SemanticMap.All)
            {
                float d = Vector3.Distance(pos, p.pos);
                if (d < bd) { bd = d; best = p.id; }
            }
            return best;
        }

        static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
