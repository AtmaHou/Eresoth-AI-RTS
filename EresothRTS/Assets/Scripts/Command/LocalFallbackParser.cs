using System.Collections.Generic;

namespace Eresoth
{
    /// <summary>本地兜底解析器：API 断线/超时/非法输出时的关键词规则解析。
    /// 词表动态来自注册表 + ReferenceResolver 别名表（军团/兵种/建筑/方位矿区），不是写死的命令清单。
    /// 只覆盖高频意图；解析不了就明确说没听懂，绝不乱猜。</summary>
    public static class LocalFallbackParser
    {
        static int batchCounter;

        /// <summary>尝试把玩家文本解析为 CommandRequest；失败返回 null。</summary>
        public static DebugCommandRunner.CommandRequest TryParse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var req = new DebugCommandRunner.CommandRequest { player_text = text };

            // 编制调整优先（"编入/划给"类，避免被军事/经济动词截胡）
            if (TryReorganize(text, req)) { req.player_reply = "收到，编制已调整（本地解析）。"; return req; }

            // 经济意图（动词明确）
            if (TryEconomy(text, req)) { req.player_reply = "收到（本地解析）。"; return req; }

            // 侦查
            if (IsScoutText(text))
            {
                var o = new DebugCommandRunner.OrderDto
                {
                    force_id = ParseForce(text),
                    action = "scout",
                };
                if (ReferenceResolver.TryMatchKindFilter(text, out var sk)) o.unit_filter = sk.ToString().ToLower();
                req.orders.Add(o);
                req.player_reply = "收到（本地解析，侦察兵已出发）。";
                return req;
            }

            // 军事意图
            string forceId = ParseForce(text);
            string targetId = ParseTarget(text);
            string action = ParseMilitaryAction(text);
            if (action != null && forceId != null)
            {
                var o = new DebugCommandRunner.OrderDto { force_id = forceId, action = action };
                if (targetId != null) o.target_id = targetId;
                string targetRef = ParseTargetRef(text);
                if (targetRef != null) o.target_ref = targetRef;
                // "遇到主力就撤"类简易条件
                if (text.Contains("主力") && (text.Contains("就撤") || text.Contains("撤退")))
                    o.conditions.Add(new DebugCommandRunner.ConditionDto { legacy_when = "enemy_main_force_seen", then = new DebugCommandRunner.ThenDto { action = "retreat", target_id = "own_retreat_point" } });
                if (text.Contains("伤亡") && text.Contains("撤"))
                    o.conditions.Add(new DebugCommandRunner.ConditionDto { legacy_when = "self_health_below", threshold = 0.5f, then = new DebugCommandRunner.ThenDto { action = "retreat", target_id = "own_retreat_point" } });
                if (ReferenceResolver.TryMatchKindFilter(text, out var k)) o.unit_filter = k.ToString().ToLower();
                req.orders.Add(o);
                req.player_reply = "收到（本地解析，复杂条件可能丢失）。";
                return req;
            }
            return null;
        }

        // ---------------- 军团 ----------------

        /// <summary>模糊解析军团指代；"全军"返回展开标记；"军团一/1队/一队"均可。未提及军团时返回 null。</summary>
        static string ParseForce(string text)
        {
            if (ForceManager.I == null || Game.I == null) return null;
            if (ReferenceResolver.IsAllForcesText(text)) return ReferenceResolver.AllForcesId;
            var forces = ForceManager.I.OfTeam(Game.I.playerTeam);
            // 先精确名称/ID
            foreach (var f in forces)
                if (text.Contains(f.name) || text.Contains(f.id)) return f.id;
            // 再规范化序号："军团一""1队""第一军团"
            int idx = ReferenceResolver.ForceIndex(text);
            if (idx > 0 && idx <= forces.Count) return forces[idx - 1].id;
            // 没提军团：兜底第一军团（保持旧行为）
            return forces.Count > 0 ? forces[0].id : null;
        }

        // ---------------- 军事目标 ----------------

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
            // 组合指代："东边的矿""我方西侧树林"
            return ReferenceResolver.ResolveResourcePointId(text);
        }

        /// <summary>模糊目标指代：英雄/兵种类别/敌我建筑（"全军集火英雄""拆敌方的箭塔"）。</summary>
        static string ParseTargetRef(string text)
        {
            if (text.Contains("英雄")) return "hero";
            if (ReferenceResolver.TryMatchKindFilter(text, out var k))
                return $"kind:{k.ToString().ToLower()}";
            return ReferenceResolver.ParseBuildingTargetRef(text);
        }

        static bool IsScoutText(string text)
            => text.Contains("侦查") || text.Contains("侦察") || text.Contains("探图") || text.Contains("探路")
                || text.Contains("探一圈") || (text.Contains("探") && text.Contains("一圈"));

        static string ParseMilitaryAction(string text)
        {
            if (text.Contains("集火") || text.Contains("优先打") || text.Contains("先杀")) return "focus_fire";
            if (text.Contains("撤")) return "retreat";
            if (text.Contains("防守") || text.Contains("守住") || text.Contains("守家") || text.Contains("回防")) return "defend";
            if (text.Contains("集结")) return "regroup";
            if (text.Contains("驻守") || text.Contains("待命") || text.Contains("原地")) return "hold";
            if (text.Contains("进攻") || text.Contains("攻击") || text.Contains("打") || text.Contains("压上去")
                || text.Contains("去") || text.Contains("骚扰") || text.Contains("偷") || text.Contains("拆")) return "attack_move";
            if (text.Contains("移动")) return "move";
            return null;
        }

        // ---------------- 编制调整 ----------------

        static bool TryReorganize(string text, DebugCommandRunner.CommandRequest req)
        {
            string keyword = text.Contains("编入") ? "编入" : text.Contains("划给") ? "划给"
                : text.Contains("调到") ? "调到" : text.Contains("转隶") ? "转隶" : null;
            if (keyword == null) return false;
            if (ForceManager.I == null || Game.I == null) return false;
            var forces = ForceManager.I.OfTeam(Game.I.playerTeam);
            if (forces.Count == 0) return false;

            // 找出文本中所有军团指代及其位置（名称/ID + "1队/一队/军团一"数字指代）
            var mentions = new List<(Force f, int pos)>();
            var seen = new HashSet<Force>();
            foreach (var f in forces)
            {
                int p = text.IndexOf(f.name);
                if (p < 0) p = text.IndexOf(f.id);
                if (p >= 0 && seen.Add(f)) mentions.Add((f, p));
            }
            var mm = System.Text.RegularExpressions.Regex.Matches(text, @"(?:军团|第)?([0-9一二两三四五六七八九]{1,2})(?:军团|队|军)");
            foreach (System.Text.RegularExpressions.Match m in mm)
            {
                int idx = ReferenceResolver.ForceIndex(m.Groups[1].Value);
                if (idx <= 0 || idx > forces.Count) continue;
                if (seen.Add(forces[idx - 1])) mentions.Add((forces[idx - 1], m.Index));
            }
            int kwPos = text.IndexOf(keyword);
            Force dest = null; int destPos = -1;
            foreach (var (f, p) in mentions)
                if (p > kwPos && (dest == null || p < destPos)) { dest = f; destPos = p; }
            if (dest == null)
                foreach (var (f, p) in mentions)
                    if (f != null && (dest == null || p > destPos)) { dest = f; destPos = p; }
            if (dest == null) return false;

            string source = ReferenceResolver.AllForcesId;
            foreach (var (f, p) in mentions)
                if (f != dest) { source = f.id; break; }

            float ratio = 0f;
            if (text.Contains("一半") || text.Contains("半数") || text.Contains("二分之一")) ratio = 0.5f;
            else if (text.Contains("三分之一")) ratio = 0.34f;
            else if (text.Contains("四分之一")) ratio = 0.25f;

            var dto = new DebugCommandRunner.OrderDto
            {
                action = "reorganize",
                force_id = dest.id,
                source_id = source,
                ratio = ratio,
            };
            if (text.Contains("英雄")) dto.target_ref = "hero";
            else if (ReferenceResolver.TryMatchKindFilter(text, out var k)) dto.unit_filter = k.ToString().ToLower();
            req.orders.Add(dto);
            return true;
        }

        // ---------------- 经济 ----------------

        static bool TryEconomy(string text, DebugCommandRunner.CommandRequest req)
        {
            bool any = false;
            bool sentWorkersToBuild = false;
            Team team = Game.I.playerTeam;

            // 修理："农民修建筑/修一下主基地/修理箭塔"（不含"建/造"避免与"修建"混淆）
            if (text.Contains("修") && !text.Contains("建") && !text.Contains("造"))
            {
                string kind = ReferenceResolver.ResolveBuildableKind(team, text);
                // "修建筑"泛指：不指定 kind 则修受损最重的一座
                if (kind == null && (text.Contains("建筑") || text.Contains("基地") || text.Contains("塔")))
                    kind = text.Contains("基地") ? "hall" : text.Contains("塔") ? "tower" : null;
                req.economy.Add(new DebugCommandRunner.OrderDto
                { action = "repair", target_id = kind, worker_count = ExtractCount(text, 2) });
                any = true;
            }

            // 拉 N 个工人开分矿："拉两个农民工去开个分矿"
            if ((text.Contains("分矿") || text.Contains("开矿") || text.Contains("扩张"))
                && (text.Contains("拉") || text.Contains("派") || text.Contains("叫") || text.Contains("让") || text.Contains("抽")))
            {
                int wc = ExtractCount(text, 2);
                req.economy.Add(new DebugCommandRunner.OrderDto
                { action = "build", target_id = "resource_hub", count = 1, worker_count = wc });
                any = true;
                sentWorkersToBuild = true;
            }
            // 依次建造队列："依次造2个兵营和三个箭塔"
            else if (text.Contains("依次") || text.Contains("顺序"))
            {
                any = ParseBuildQueue(text, req) || any;
            }

            // 训练：匹配兵种中文名/别名（动态词表，"弓箭手"也能认出长弓手）
            if (text.Contains("训练") || text.Contains("造") || text.Contains("补") || text.Contains("出"))
            {
                foreach (var seg in SplitSegments(text))
                {
                    string uid = ReferenceResolver.ResolveTrainableUnitId(team, seg);
                    if (uid == null) continue;
                    // 队列分段里"造2个兵营"不应命中兵种——ResolveTrainableUnitId 只匹兵种名，安全
                    req.economy.Add(new DebugCommandRunner.OrderDto
                    { action = "train", target_id = uid, count = ExtractCount(seg, 1) });
                    any = true;
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
            // 建造（未走队列时）：匹配建筑中文名/别名；"开分矿/扩张"直指资源收集站
            if (!any && (text.Contains("建") || text.Contains("开分矿") || text.Contains("扩张")))
            {
                string kind = (text.Contains("分矿") || text.Contains("扩张"))
                    ? "resource_hub" : ReferenceResolver.ResolveBuildableKind(team, text);
                if (kind != null)
                {
                    req.economy.Add(new DebugCommandRunner.OrderDto
                    { action = "build", target_id = kind, count = ExtractCount(text, 1) });
                    any = true;
                }
            }
            // 工人采集分配："所有农民采矿按比分配"/"工人去采木"/"3个农民去采魔法矿"
            // 未指定数量时只动空闲工人（不强行打扰在采的）；指定 N 个则抽 N 个；说"所有/全体"全量重排
            if (!sentWorkersToBuild && (text.Contains("工人") || text.Contains("农民") || text.Contains("侍僧") || text.Contains("农奴")))
            {
                bool allWorkers = text.Contains("所有") || text.Contains("全部") || text.Contains("全体");
                if (text.Contains("采") || text.Contains("矿") || text.Contains("木") || text.Contains("资源"))
                {
                    var dto = new DebugCommandRunner.OrderDto { action = "assign_workers" };
                    if (text.Contains("木")) { dto.resource = "wood"; dto.ratio = 0.6f; }
                    else if (text.Contains("矿") || text.Contains("魔")) { dto.resource = "mana"; dto.ratio = 0.6f; }
                    else { dto.resource = "both"; dto.ratio = 0.6f; }   // "所有农民采资源"：按比例分
                    dto.worker_count = allWorkers ? -1 : ExtractCount(text, 0);
                    req.economy.Add(dto);
                    any = true;
                }
            }
            return any;
        }

        /// <summary>"依次造2个兵营和三个箭塔" → 同批次 build 计划，按 sequence 排队执行。</summary>
        static bool ParseBuildQueue(string text, DebugCommandRunner.CommandRequest req)
        {
            string batch = "bq_" + (++batchCounter);
            int seq = 0;
            bool any = false;
            foreach (var seg in SplitSegments(text))
            {
                string kind = ReferenceResolver.ResolveBuildableKind(Game.I.playerTeam, seg);
                if (kind == null) continue;
                req.economy.Add(new DebugCommandRunner.OrderDto
                { action = "build", target_id = kind, count = ExtractCount(seg, 1), batch = batch, sequence = seq++ });
                any = true;
            }
            return any;
        }

        /// <summary>按并列/顺序词切分段落（"依次A和B然后C" → A/B/C）。</summary>
        static IEnumerable<string> SplitSegments(string text)
        {
            var parts = text.Split(new[] { "依次", "顺序", "然后", "接着", "随后", "再", "和", "与", "，", "、", ",", "；" },
                System.StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
                if (!string.IsNullOrWhiteSpace(p)) yield return p;
        }

        static int ExtractCount(string text, int def)
        {
            // 提取阿拉伯数字
            var m = System.Text.RegularExpressions.Regex.Match(text, @"(\d+)\s*[个名座]");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int n) && n > 0 && n < 50) return n;
            // 中文数字（两/一二三四五六七八九十）
            string[] digits = { "一", "二", "两", "三", "四", "五", "六", "七", "八", "九" };
            for (int i = 0; i < digits.Length; i++)
                if (text.Contains(digits[i] + "个") || text.Contains(digits[i] + "名") || text.Contains(digits[i] + "座")
                    || text.Contains(digits[i] + "只") || text.Contains(digits[i] + "人")) return i + 1;
            if (text.Contains("十个")) return 10;
            return def;
        }
    }
}
