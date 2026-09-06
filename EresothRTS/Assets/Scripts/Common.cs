using System.Collections.Generic;
using UnityEngine;

namespace Eresoth
{
    public enum Team { Player = 0, Enemy = 1 }
    public enum TechEffect { Atk, Def }

    /// <summary>兵种大类：循环克制 步兵→骑兵→远程→步兵（伤害 ×CounterBonus）。</summary>
    public enum UnitKind { Worker, Infantry, Ranged, Cavalry }

    /// <summary>所有可被攻击/选中的目标（单位、建筑）统一接口。
    /// 后续 LLM 军令层的 target 也用此接口寻址。</summary>
    public interface ITargetable
    {
        Vector3 Pos { get; }
        float Radius { get; }
        Team Team { get; }
        bool Alive { get; }
        float Hp01 { get; }
        string DisplayName { get; }
        void Damage(float dmg);
    }

    /// <summary>兵种静态数据：循环克制体系（步兵/远程/骑兵）+ 工人 + 英雄（光环/AOE）。
    /// 可指定 prefab：非空时实例化外部模型，空时回退到程序化低多边形模型。</summary>
    public class UnitDef
    {
        public string id, name;
        public UnitKind kind;
        public float hp, dmg, range, speed, cooldown, aggro, size;
        public int wood, mana, pop;
        public bool worker;
        public bool hero;                       // 英雄：全场唯一，数值/光环/AOE 强化
        public float auraRadius, auraBonus;     // 光环：附近友军伤害 +bonus
        public float aoeRadius;                 // AOE：攻击对目标周围溅射（SplashFrac 伤害）
        public Color color;
        public GameObject prefab;               // 可选外部模型/动画 Prefab

        public UnitDef(string id, string name, UnitKind kind, float hp, float dmg, float range, float speed,
            float cooldown, float aggro, float size, int wood, int mana, int pop, bool worker, Color color,
            bool hero = false, float auraRadius = 0f, float auraBonus = 0f, float aoeRadius = 0f,
            GameObject prefab = null)
        {
            this.id = id; this.name = name; this.kind = kind;
            this.hp = hp; this.dmg = dmg; this.range = range;
            this.speed = speed; this.cooldown = cooldown; this.aggro = aggro; this.size = size;
            this.wood = wood; this.mana = mana; this.pop = pop; this.worker = worker;
            this.color = color;
            this.hero = hero; this.auraRadius = auraRadius; this.auraBonus = auraBonus;
            this.aoeRadius = aoeRadius; this.prefab = prefab;
        }
    }

    /// <summary>建筑静态数据：造价/HP/可训练兵种/可研究科技，全部数据驱动。
    /// atkRange > 0 时为防御塔（自动攻击射程内敌人）。
    /// 可指定 prefab：非空时实例化外部模型，空时回退到程序化低多边形模型。</summary>
    public class BuildingDef
    {
        public string kind, name;
        public int wood, mana;
        public float hp, size;
        public UnitDef[] train;
        public string[] techs;
        public float atk, atkRange, atkCd;   // 防御塔攻击参数（atkRange=0 表示无攻击力）
        public GameObject prefab;            // 可选外部模型 Prefab

        public BuildingDef(string kind, string name, int wood, int mana, float hp, float size,
            UnitDef[] train = null, string[] techs = null,
            float atk = 0f, float atkRange = 0f, float atkCd = 0f,
            GameObject prefab = null)
        {
            this.kind = kind; this.name = name; this.wood = wood; this.mana = mana;
            this.hp = hp; this.size = size; this.train = train; this.techs = techs;
            this.atk = atk; this.atkRange = atkRange; this.atkCd = atkCd;
            this.prefab = prefab;
        }
    }

    /// <summary>科技静态数据。第一层：兵种攻防强化（本次实现）；
    /// 第二层 AI 指挥科技（设计文档 5.4）留待指挥所/通灵塔，接 LLM 层时再实现。</summary>
    public class TechDef
    {
        public string id, name, desc;
        public int wood, mana;      // 1 级造价，每级造价 = 基础 × 等级
        public float time;          // 研究秒数
        public int maxLevel;
        public TechEffect effect;

        public TechDef(string id, string name, string desc, int wood, int mana, float time, int maxLevel, TechEffect effect)
        {
            this.id = id; this.name = name; this.desc = desc;
            this.wood = wood; this.mana = mana; this.time = time;
            this.maxLevel = maxLevel; this.effect = effect;
        }
    }

    /// <summary>开局地图设置。static 字段跨场景保留，"再来一局"沿用同一设置。</summary>
    public static class MapSettings
    {
        public static bool randomMap = true;   // true = 每局随机种子；false = 固定种子（布局可复现）
        public static int richnessIndex = 1;   // 资源丰富度档位：0=贫瘠 1=标准 2=富饶
        public static Team playerTeam = Team.Player;  // 玩家操控的阵营（另一阵营由 AI 操控）
        public static readonly string[] RichnessNames = { "贫瘠", "标准", "富饶" };
        public static readonly float[] RichnessValues = { 0.6f, 1f, 1.8f };  // 资源点数量乘区
        public static float Richness => RichnessValues[richnessIndex];
    }

    public static class GameConfig
    {
        public const int BasePop = 40;      // 主基地自带人口
        public const int HousePop = 15;     // 每座民居 +15 人口
        public const int MaxPopCap = 100;   // 人口硬上限
        public const int GatherAmount = 8;      // 单次采集量
        public const float GatherTime = 2f;     // 单次采集耗时(秒)
        public const float TrainTime = 2.5f;    // 单位训练耗时(秒)
        public const float CounterBonus = 1.5f; // 循环克制伤害加成：步兵克骑兵、远程克步兵、骑兵(高速)克远程
        public const float SplashFrac = 0.5f;   // 英雄 AOE 对主目标周围单位的溅射伤害比例
        public const int HeroCap = 1;           // 每方英雄同时存在上限

        // ---- 人类（玩家阵营）：工人 + 步兵/远程/骑兵 ----
        public static readonly UnitDef Farmer  = new("farmer",     "农夫",     UnitKind.Worker,    45,  3, 1.2f, 5.5f, 1.0f, 0,  0.80f,  50,  0,  1, true,  new Color(.85f, .75f, .55f));
        public static readonly UnitDef Footman = new("footman",    "步兵",     UnitKind.Infantry,  110, 10, 1.6f, 5.0f, 1.1f, 9,  0.95f,  50,  0,  1, false, new Color(.55f, .65f, .95f));
        public static readonly UnitDef Archer  = new("archer",     "长弓手",   UnitKind.Ranged,    65,  12, 9.0f, 5.0f, 1.2f, 10, 0.85f,  50, 10, 1, false, new Color(.45f, .85f, .55f));
        public static readonly UnitDef Knight  = new("knight",     "骑士",     UnitKind.Cavalry,   170, 17, 1.8f, 7.5f, 1.3f, 9,  1.15f,  90, 30, 2, false, new Color(.90f, .85f, .40f));

        // ---- 不死（AI 阵营）：工人 + 步兵/远程/骑兵 ----
        public static readonly UnitDef Acolyte     = new("acolyte",     "侍僧",     UnitKind.Worker,   40,  3, 1.2f, 5.5f, 1.0f, 0,  0.80f,  50,  0,  1, true,  new Color(.50f, .40f, .55f));
        public static readonly UnitDef Skeleton    = new("skeleton",    "骷髅兵",   UnitKind.Infantry, 100, 9, 1.5f, 5.0f, 1.0f, 9,  0.85f,  45,  0,  1, false, new Color(.90f, .90f, .85f));
        public static readonly UnitDef DarkArcher  = new("darkarcher",  "亡灵射手", UnitKind.Ranged,   60,  11, 9.0f, 5.0f, 1.2f, 10, 0.85f,  50, 10, 1, false, new Color(.65f, .45f, .85f));
        public static readonly UnitDef DeathKnight = new("deathknight", "死亡骑士", UnitKind.Cavalry,  160, 16, 1.8f, 7.5f, 1.3f, 9,  1.15f,  90, 30, 2, false, new Color(.55f, .60f, .70f));

        // ---- 英雄（全场唯一，兵营训练）：数值强化 + 攻击光环 + AOE 溅射 ----
        public static readonly UnitDef LordKnight = new("lordknight", "骑士团长", UnitKind.Cavalry, 650, 34, 2.2f, 7.5f, 1.2f, 10, 1.50f,
            300, 150, 3, false, new Color(1f, .92f, .45f), hero: true, auraRadius: 10f, auraBonus: 0.25f, aoeRadius: 3f);
        public static readonly UnitDef DeathRanger = new("deathranger", "霜骨巫妖", UnitKind.Ranged, 520, 30, 10f, 5.5f, 1.1f, 11, 1.10f,
            300, 150, 3, false, new Color(.85f, .55f, 1f), hero: true, auraRadius: 10f, auraBonus: 0.25f, aoeRadius: 2.5f);

        // ---- 第一层科技：兵种攻防强化（每级 +15%，造价 = 基础 × 等级）----
        public static readonly TechDef HumanAtk  = new("human_atk",  "利刃淬炼", "全军攻击 +15%",       100, 50, 20f, 2, TechEffect.Atk);
        public static readonly TechDef HumanDef  = new("human_def",  "坚壁工事", "全军受伤减免约 13%",  100, 50, 20f, 2, TechEffect.Def);
        public static readonly TechDef UndeadAtk = new("undead_atk", "白骨磨锋", "全军攻击 +15%",       100, 50, 20f, 2, TechEffect.Atk);
        public static readonly TechDef UndeadDef = new("undead_def", "腐躯硬化", "全军受伤减免约 13%",  100, 50, 20f, 2, TechEffect.Def);

        public static readonly Dictionary<string, TechDef> Techs = new()
        {
            { HumanAtk.id, HumanAtk }, { HumanDef.id, HumanDef },
            { UndeadAtk.id, UndeadAtk }, { UndeadDef.id, UndeadDef },
        };

        // ---- 建筑表：主基地 + 每类兵种一座专属兵营（科技在步兵兵营研究）----
        // 伐木场：工人采集量 +50%（全场唯一）；箭塔：自动攻击的防御塔（可建多座）
        // 民居：人口 +15（可建多座，主基地 40 + 4 民居正好到 100 上限）
        public static readonly BuildingDef Hall        = new("hall",        "主基地",   0,   0,   1500, 5.0f);
        public static readonly BuildingDef Barracks    = new("barracks",    "兵营",     150, 0,   800,  3.6f, new[] { Footman, LordKnight }, new[] { "human_atk", "human_def" });
        public static readonly BuildingDef Archery     = new("archery",     "弓箭场",   140, 20,  700,  3.2f, new[] { Archer });
        public static readonly BuildingDef Stable      = new("stable",      "马厩",     180, 40,  800,  3.6f, new[] { Knight });
        public static readonly BuildingDef Lumber      = new("lumber",      "伐木场",   120, 0,   600,  3.0f);
        public static readonly BuildingDef Tower       = new("tower",       "箭塔",     80,  0,   350,  2.6f, atk: 12, atkRange: 11, atkCd: 1f);
        public static readonly BuildingDef House       = new("house",       "民居",     80,  0,   400,  3.0f);
        public static readonly BuildingDef Crypt       = new("crypt",       "地穴",     150, 0,   800,  3.6f, new[] { Skeleton, DeathRanger }, new[] { "undead_atk", "undead_def" });
        public static readonly BuildingDef DarkTemple  = new("dark_temple", "诅咒神殿", 140, 20,  700,  3.2f, new[] { DarkArcher });
        public static readonly BuildingDef DeathStable = new("death_stable","死亡马厩", 180, 40,  800,  3.6f, new[] { DeathKnight });

        public static readonly BuildingDef[] HumanBuildings  = { Barracks, Archery, Stable, Lumber, Tower, House };
        public static readonly BuildingDef[] UndeadBuildings = { Crypt, DarkTemple, DeathStable, Lumber, Tower, House };
    }
}
