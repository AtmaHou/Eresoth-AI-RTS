using System.Collections.Generic;
using UnityEngine;

namespace Eresoth
{
    /// <summary>模糊指代解析：把"英雄/骑兵/敌方箭塔/东边的矿/军团一"这类自然语言指代
    /// 归一到注册表 ID（军团/兵种/建筑 kind/语义点）。词表集中在此，Prompt 词表与本地兜底共用。
    /// 规则：只归一到本局真实存在的东西，解析不了返回 null，由调用方决定兜底或拒绝。</summary>
    public static class ReferenceResolver
    {
        /// <summary>"全军"展开标记：执行层据此给每个军团各下一条。</summary>
        public const string AllForcesId = "__all__";

        // ---------------- 军团 ----------------

        public static bool IsAllForcesText(string text)
            => text != null && (text.Contains("全军") || text.Contains("所有部队") || text.Contains("全部部队")
                || text.Contains("所有兵团") || text.Contains("全部兵"));

        /// <summary>规范化军团指代："军团一/一军团/1队/一队/第一军团/army_1" → 序号 1。解析失败返回 0。</summary>
        public static int ForceIndex(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var m = System.Text.RegularExpressions.Regex.Match(text, @"(\d+)");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int n) && n > 0 && n < 100) return n;
            string[] digits = { "一", "二", "两", "三", "四", "五", "六", "七", "八", "九" };
            for (int i = 0; i < digits.Length; i++)
                if (text.Contains(digits[i])) return i + 1;
            if (text.Contains("十"))
            {
                if (text.Contains("十二")) return 12;
                return text.Replace("十", "").Length == 0 ? 10 : 0;
            }
            return 0;
        }

        // ---------------- 兵种 ----------------

        /// <summary>兵种别名 → 单位 id（含双方种族；训练时再按阵营过滤可训练性）。</summary>
        public static readonly Dictionary<string, string[]> UnitAliases = new()
        {
            { "farmer", new[] { "农夫", "农民", "农民工" } },
            { "acolyte", new[] { "侍僧" } },
            { "footman", new[] { "步兵" } },
            { "skeleton", new[] { "骷髅兵", "骷髅" } },
            { "archer", new[] { "长弓手", "弓箭手", "弓手", "弓兵" } },
            { "darkarcher", new[] { "亡灵射手", "黑暗射手" } },
            { "knight", new[] { "骑士" } },          // 注意"骑士团长"更长，匹配时按长度降序
            { "deathknight", new[] { "死亡骑士" } },
            { "lordknight", new[] { "骑士团长", "英雄" } },
            { "deathranger", new[] { "霜骨巫妖", "巫妖", "英雄" } },
        };

        /// <summary>玩家阵营是否为人类（看工人是农夫还是侍僧）。</summary>
        public static bool IsHumanSide(Team team)
        {
            foreach (var u in Game.I.units)
                if (u != null && u.Alive && u.team == team && u.def.worker)
                    return u.def.id == "farmer";
            return true;
        }

        static bool FactionCanTrain(string unitId, Team team)
        {
            var kinds = IsHumanSide(team)
                ? RuntimeConfig.Data.factionBuildings.humanBuildingKinds
                : RuntimeConfig.Data.factionBuildings.undeadBuildingKinds;
            foreach (var kv in RuntimeConfig.Buildings)
            {
                if (kv.Value.train == null) continue;
                if (kinds.Contains(kv.Key) || kv.Key == "hall")
                    foreach (var ud in kv.Value.train)
                        if (ud.id == unitId) return true;
            }
            return false;
        }

        /// <summary>按名称/别名找可训练兵种 id（"弓箭手"→ archer 或 darkarcher，按阵营取）。</summary>
        public static string ResolveTrainableUnitId(Team team, string nameOrAlias)
        {
            if (string.IsNullOrEmpty(nameOrAlias)) return null;
            if (RuntimeConfig.Units.TryGetValue(nameOrAlias, out var direct) && FactionCanTrain(direct.id, team))
                return direct.id;
            var cands = new List<(string key, string id)>();
            foreach (var kv in RuntimeConfig.Units)
            {
                cands.Add((kv.Value.name, kv.Key));
                if (UnitAliases.TryGetValue(kv.Key, out var als))
                    foreach (var a in als) cands.Add((a, kv.Key));
            }
            cands.Sort((a, b) => b.key.Length.CompareTo(a.key.Length));
            foreach (var (key, id) in cands)
                if (nameOrAlias.Contains(key) && FactionCanTrain(id, team)) return id;
            return null;
        }

        /// <summary>从文本提取兵种大类指代（骑兵/步兵/弓兵/远程/工人），用于 unit_filter。</summary>
        public static bool TryMatchKindFilter(string text, out UnitKind kind)
        {
            kind = default;
            if (string.IsNullOrEmpty(text)) return false;
            if (text.Contains("骑兵")) { kind = UnitKind.Cavalry; return true; }
            if (text.Contains("远程")) { kind = UnitKind.Ranged; return true; }
            // "弓"单独匹配会误伤"弓箭场"等建筑语境，要求弓+兵/手
            if ((text.Contains("弓兵") || text.Contains("弓手") || text.Contains("弓箭手") || text.Contains("长弓手"))) { kind = UnitKind.Ranged; return true; }
            if (text.Contains("步兵")) { kind = UnitKind.Infantry; return true; }
            return false;
        }

        // ---------------- 建筑 ----------------

        /// <summary>建筑通用别名 → 双方 kind 列表（兵营=barracks/crypt，按阵营取可建者）。</summary>
        public static readonly Dictionary<string, string[]> BuildingAliases = new()
        {
            { "兵营", new[] { "barracks", "crypt" } },
            { "地穴", new[] { "crypt" } },
            { "弓箭场", new[] { "archery", "dark_temple" } },
            { "神殿", new[] { "dark_temple" } },
            { "马厩", new[] { "stable", "death_stable" } },
            { "箭塔", new[] { "tower" } },
            { "塔", new[] { "tower" } },
            { "民居", new[] { "house" } },
            { "房子", new[] { "house" } },
            { "主基地", new[] { "hall" } },
            { "基地", new[] { "hall" } },
            { "资源收集站", new[] { "resource_hub" } },
            { "收集站", new[] { "resource_hub" } },
        };

        /// <summary>按名称/别名找该阵营可建建筑 kind（"兵营"→ barracks 或 crypt）。</summary>
        public static string ResolveBuildableKind(Team team, string nameOrAlias)
        {
            if (string.IsNullOrEmpty(nameOrAlias)) return null;
            var kinds = IsHumanSide(team)
                ? RuntimeConfig.Data.factionBuildings.humanBuildingKinds
                : RuntimeConfig.Data.factionBuildings.undeadBuildingKinds;
            if (RuntimeConfig.Buildings.TryGetValue(nameOrAlias, out var direct)
                && (kinds.Contains(direct.kind) || direct.kind == "hall")) return direct.kind;
            var cands = new List<(string key, string[] ids)>();
            foreach (var kv in RuntimeConfig.Buildings) cands.Add((kv.Value.name, new[] { kv.Key }));
            foreach (var kv in BuildingAliases) cands.Add((kv.Key, kv.Value));
            cands.Sort((a, b) => b.key.Length.CompareTo(a.key.Length));
            foreach (var (key, ids) in cands)
            {
                if (!nameOrAlias.Contains(key)) continue;
                foreach (var id in ids)
                    if (kinds.Contains(id) || id == "hall") return id;
            }
            return null;
        }

        /// <summary>从军事文本提取建筑类目标指代："敌方箭塔"→ "bld:箭塔:enemy"（别名保留在 ref 里，运行时按阵营解 kind）。</summary>
        public static string ParseBuildingTargetRef(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var keys = new List<string>(BuildingAliases.Keys);
            keys.Sort((a, b) => b.Length.CompareTo(a.Length));
            foreach (var key in keys)
            {
                if (!text.Contains(key)) continue;
                bool own = text.Contains("我方") || text.Contains("自家") || text.Contains("家里")
                    || text.Contains("我们") || text.Contains("自己的");
                string side = own ? "own" : "enemy";
                return $"bld:{key}:{side}";
            }
            return null;
        }

        // ---------------- 方位 + 资源（"东边的矿""我方西侧树林"） ----------------

        /// <summary>组合方位/归属/资源类型为语义点 ID；无法组合返回 null。</summary>
        public static string ResolveResourcePointId(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            bool hasResWord = text.Contains("矿") || text.Contains("魔") || text.Contains("木")
                || text.Contains("树") || text.Contains("资源");
            if (!hasResWord) return null;
            string dir = text.Contains("东") ? "east" : text.Contains("西") ? "west"
                : (text.Contains("中矿") || text.Contains("中央") || text.Contains("中场")) ? "center" : null;
            if (dir == null) return null;
            string kind = (text.Contains("木") || text.Contains("树")) ? "wood" : "mana";
            if (dir == "center")
            {
                string cid = kind == "wood" ? null : "center_mana";
                return cid != null && SemanticMap.Exists(cid) ? cid : null;
            }
            bool own = text.Contains("我方") || text.Contains("自家") || text.Contains("家里")
                || text.Contains("我们") || text.Contains("自己的");
            bool enemy = text.Contains("敌") || text.Contains("对面") || text.Contains("他家")
                || text.Contains("对方") || text.Contains("别人");
            // 无归属词时沿用语义点惯例：方位矿默认指敌方半场
            string side = own ? "own" : (enemy ? "enemy" : "enemy");
            string id = $"{side}_{dir}_{kind}";
            return SemanticMap.Exists(id) ? id : null;
        }

        // ---------------- 运行时目标解析（ForceController 用） ----------------

        /// <summary>把 targetRef 解析为当前可见的具体目标（迷雾外不可见=不存在，返回 false）。
        /// 目标死亡/消失后再次调用会自动换目标或返回 false。</summary>
        public static bool TryResolveRuntimeTarget(string targetRef, Team team, out ITargetable target)
        {
            target = null;
            if (string.IsNullOrEmpty(targetRef)) return false;
            var parts = targetRef.Split(':');
            Team foe = team == Team.Player ? Team.Enemy : Team.Player;

            if (parts[0] == "hero")
            {
                Unit best = null; float bs = -1f;
                foreach (var u in Game.I.units)
                {
                    if (u == null || !u.Alive || u.team != foe || u.def.worker) continue;
                    if (!FogOfWarManager.VisibleToPlayer(u.transform.position)) continue;
                    float score = (u.def.hero ? 10000f : 0f) + u.def.mana + u.Hp01 * 10f;
                    if (score > bs) { bs = score; best = u; }
                }
                target = best;
                return best != null;
            }
            if (parts[0] == "kind" && parts.Length >= 2
                && System.Enum.TryParse<UnitKind>(parts[1], true, out var k))
            {
                Unit best = null; float bd = float.MaxValue;
                Vector3 from = Game.I.baseCenter[(int)team];
                foreach (var u in Game.I.units)
                {
                    if (u == null || !u.Alive || u.team != foe || u.def.kind != k) continue;
                    if (!FogOfWarManager.VisibleToPlayer(u.transform.position)) continue;
                    float d = Vector3.Distance(from, u.transform.position);
                    if (d < bd) { bd = d; best = u; }
                }
                target = best;
                return best != null;
            }
            if (parts[0] == "bld" && parts.Length >= 3)
            {
                // bld:<中文别名>:<side>：按"指挥方阵营的种族"把别名落到具体 kind
                string[] ids = BuildingAliases.TryGetValue(parts[1], out var list) ? list : null;
                if (ids == null) return false;
                Team sideTeam = parts[2] == "own" ? team : foe;
                var kinds = IsHumanSide(team)
                    ? RuntimeConfig.Data.factionBuildings.humanBuildingKinds
                    : RuntimeConfig.Data.factionBuildings.undeadBuildingKinds;
                Building best = null; float bd = float.MaxValue;
                Vector3 from = Game.I.baseCenter[(int)team];
                foreach (var b in Game.I.buildings)
                {
                    if (b == null || !b.Alive || b.team != sideTeam) continue;
                    bool match = b.kind == "hall" && System.Array.Exists(ids, x => x == "hall");
                    if (!match)
                        foreach (var id in ids)
                            if (id == b.kind && (kinds.Contains(id) || id == "tower" || id == "house" || id == "resource_hub"))
                            { match = true; break; }
                    if (!match) continue;
                    if (!FogOfWarManager.VisibleToPlayer(b.transform.position)) continue;
                    float d = Vector3.Distance(from, b.transform.position);
                    if (d < bd) { bd = d; best = b; }
                }
                target = best;
                return best != null;
            }
            return false;
        }
    }
}
