using System.Collections.Generic;

namespace Eresoth
{
    /// <summary>本地兜底解析器：API 断线/超时/非法输出时的关键词规则解析。
    /// 词表同样动态来自注册表（军团名/语义别名/兵种名/科技名/建筑名），不是写死的命令清单。
    /// 只覆盖高频意图；解析不了就明确说没听懂，绝不乱猜。</summary>
    public static class LocalFallbackParser
    {
        /// <summary>尝试把玩家文本解析为 CommandRequest；失败返回 null。</summary>
        public static DebugCommandRunner.CommandRequest TryParse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var req = new DebugCommandRunner.CommandRequest { player_text = text };

            string forceId = ParseForce(text);
            string targetId = ParseTarget(text);

            // 经济意图优先（动词明确）
            if (TryEconomy(text, req)) { req.player_reply = "收到（本地解析）。"; return req; }

            // 军事意图
            string action = ParseMilitaryAction(text);
            if (action != null && forceId != null)
            {
                var o = new DebugCommandRunner.OrderDto { force_id = forceId, action = action };
                if (targetId != null) o.target_id = targetId;
                // "遇到主力就撤"类简易条件
                if (text.Contains("主力") && (text.Contains("就撤") || text.Contains("撤退")))
                    o.conditions.Add(new DebugCommandRunner.ConditionDto { legacy_when = "enemy_main_force_seen", then = new DebugCommandRunner.ThenDto { action = "retreat", target_id = "own_retreat_point" } });
                if (text.Contains("伤亡") && text.Contains("撤"))
                    o.conditions.Add(new DebugCommandRunner.ConditionDto { legacy_when = "self_health_below", threshold = 0.5f, then = new DebugCommandRunner.ThenDto { action = "retreat", target_id = "own_retreat_point" } });
                if (text.Contains("骑兵")) o.unit_filter = "cavalry";
                else if (text.Contains("弓") || text.Contains("远程")) o.unit_filter = text.Contains("骑兵") ? null : o.unit_filter;
                req.orders.Add(o);
                req.player_reply = "收到（本地解析，复杂条件可能丢失）。";
                return req;
            }
            return null;
        }

        static string ParseForce(string text)
        {
            if (ForceManager.I == null) return "army_1";
            var forces = ForceManager.I.OfTeam(Game.I.playerTeam);
            foreach (var f in forces)
                if (text.Contains(f.name) || text.Contains(f.id)) return f.id;
            if (text.Contains("全军") || text.Contains("所有部队") || text.Contains("所有人"))
                return forces.Count > 0 ? forces[0].id : "army_1";   // 兜底：全军只发第一军团（简化）
            return forces.Count > 0 ? forces[0].id : "army_1";
        }

        static string ParseTarget(string text)
        {
            // 动态别名表 + 语义点 ID 直匹配，最长优先避免"东矿"被"矿"截胡
            var cands = new List<(string key, string id)>();
            foreach (var p in SemanticMap.All)
            {
                cands.Add((p.id, p.id));
                cands.Add((p.displayName, p.id));
            }
            foreach (var alias in new[] { "东矿", "西矿", "中矿", "中央矿", "家门口", "家里", "敌方主基地", "中场", "前线", "撤退点", "后方" })
                if (SemanticMap.TryGet(alias, out var sp)) cands.Add((alias, sp.id));
            cands.Sort((a, b) => b.key.Length.CompareTo(a.key.Length));
            foreach (var (key, id) in cands)
                if (text.Contains(key)) return id;
            return null;
        }

        static string ParseMilitaryAction(string text)
        {
            if (text.Contains("集火") || text.Contains("优先打") || text.Contains("先杀")) return "focus_fire";
            if (text.Contains("撤")) return "retreat";
            if (text.Contains("防守") || text.Contains("守住") || text.Contains("守家") || text.Contains("回防")) return "defend";
            if (text.Contains("集结")) return "regroup";
            if (text.Contains("驻守") || text.Contains("待命") || text.Contains("原地")) return "hold";
            if (text.Contains("进攻") || text.Contains("攻击") || text.Contains("打") || text.Contains("压上去")
                || text.Contains("去") || text.Contains("骚扰") || text.Contains("偷")) return "attack_move";
            if (text.Contains("移动")) return "move";
            return null;
        }

        static bool TryEconomy(string text, DebugCommandRunner.CommandRequest req)
        {
            bool any = false;
            // 训练：匹配兵种中文名（动态词表）
            if (text.Contains("训练") || text.Contains("造") || text.Contains("补") || text.Contains("出"))
            {
                foreach (var kv in RuntimeConfig.Units)
                {
                    if (!text.Contains(kv.Value.name)) continue;
                    int count = ExtractCount(text, 4);
                    req.economy.Add(new DebugCommandRunner.OrderDto { action = "train", target_id = kv.Key, count = count });
                    any = true;
                    break;
                }
            }
            if (text.Contains("研究") || text.Contains("升"))
            {
                foreach (var kv in GameConfig.Techs)
                {
                    if (!text.Contains(kv.Value.name) && !(text.Contains("攻击") && kv.Key.EndsWith("_atk"))
                        && !(text.Contains("防御") && kv.Key.EndsWith("_def"))) continue;
                    req.economy.Add(new DebugCommandRunner.OrderDto { action = "research", target_id = kv.Key });
                    any = true;
                    break;
                }
            }
            if (text.Contains("建") || text.Contains("开分矿") || text.Contains("扩张"))
            {
                foreach (var kv in RuntimeConfig.Buildings)
                {
                    if (!text.Contains(kv.Value.name) && !(text.Contains("分矿") && kv.Key == "resource_hub")) continue;
                    req.economy.Add(new DebugCommandRunner.OrderDto { action = "build", target_id = kv.Key });
                    any = true;
                    break;
                }
            }
            if (text.Contains("工人") || text.Contains("农民") || text.Contains("采集"))
            {
                if (text.Contains("木头") || text.Contains("木"))
                    req.economy.Add(new DebugCommandRunner.OrderDto { action = "assign_workers", resource = "wood", ratio = 0.6f });
                else if (text.Contains("矿"))
                    req.economy.Add(new DebugCommandRunner.OrderDto { action = "assign_workers", resource = "mana", ratio = 0.6f });
                any = req.economy.Count > 0;
            }
            return any;
        }

        static int ExtractCount(string text, int def)
        {
            // 提取阿拉伯数字；"两"特判
            var m = System.Text.RegularExpressions.Regex.Match(text, @"(\d+)\s*[个名位]");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int n) && n > 0 && n < 50) return n;
            if (text.Contains("两个")) return 2;
            return def;
        }
    }
}
