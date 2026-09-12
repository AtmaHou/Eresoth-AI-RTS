using System.Collections.Generic;
using UnityEngine;

namespace Eresoth
{
    /// <summary>地图语义点：自然语言/军令引用的稳定地标（"东矿""家门口"）。
    /// 模型与 JSON 军令只引用 ID，不输出坐标。</summary>
    public class SemanticPoint
    {
        public string id;           // "own_main_base" 等稳定 ID
        public string displayName;  // "己方主基地"
        public Vector3 pos;         // 动态点由注册方/ForceManager 更新
        public bool isDynamic;
    }

    /// <summary>语义点注册表：ID 直查 + 中文别名消歧。
    /// 消歧规则：战场方位词（"东矿"）默认指敌方半场对应点；"家门口/家里"指己方。</summary>
    public static class SemanticMap
    {
        static readonly Dictionary<string, SemanticPoint> byId = new();

        // 中文别名 -> 语义点 ID（相对玩家视角命名，注册时生成对应 ID）
        static readonly Dictionary<string, string> alias = new()
        {
            { "家门口", "own_main_base" }, { "家里", "own_main_base" }, { "己方主基地", "own_main_base" },
            { "敌方主基地", "enemy_main_base" }, { "对面基地", "enemy_main_base" }, { "他家", "enemy_main_base" },
            { "东矿", "enemy_east_mana" }, { "东边矿", "enemy_east_mana" },
            { "西矿", "enemy_west_mana" }, { "西边矿", "enemy_west_mana" },
            { "中矿", "center_mana" }, { "中央矿", "center_mana" }, { "富集矿", "center_mana" },
            { "中场", "center_field" }, { "地图中心", "center_field" },
            { "撤退点", "own_retreat_point" }, { "后方", "own_retreat_point" },
            { "前线", "enemy_frontline" }, { "敌方前线", "enemy_frontline" },
        };

        public static List<SemanticPoint> All { get; } = new();

        /// <summary>新一局开始前清空（BuildWorld 时由注册器调用）。</summary>
        public static void Clear() { byId.Clear(); All.Clear(); }

        public static void Register(string id, string displayName, Vector3 pos, bool isDynamic = false)
        {
            var p = new SemanticPoint { id = id, displayName = displayName, pos = pos, isDynamic = isDynamic };
            byId[id] = p;
            All.RemoveAll(x => x.id == id);
            All.Add(p);
        }

        /// <summary>更新动态点位置（如 enemy_frontline 跟随可见敌军质心）。</summary>
        public static void Update(string id, Vector3 pos)
        {
            if (byId.TryGetValue(id, out var p)) p.pos = pos;
        }

        /// <summary>按 ID 或中文别名查找；都找不到返回 false。</summary>
        public static bool TryGet(string idOrAlias, out SemanticPoint p)
        {
            if (idOrAlias != null && byId.TryGetValue(idOrAlias, out p)) return true;
            if (idOrAlias != null && alias.TryGetValue(idOrAlias, out var id))
                return byId.TryGetValue(id, out p);
            p = null;
            return false;
        }

        /// <summary>是否存在（校验用，不取点）。</summary>
        public static bool Exists(string idOrAlias) => TryGet(idOrAlias, out _);
    }
}
