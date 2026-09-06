using System.Collections.Generic;
using UnityEngine;

namespace Eresoth
{
    public enum Team { Player = 0, Enemy = 1 }
    public enum TechEffect { Atk, Def }
    public enum VictoryMode { MainBase, AllBuildings }
    public enum Difficulty { Easy, Normal, Hard }

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
        public static int mapSizeIndex = 1;           // 0=小 1=标准 2=大
        public static VictoryMode victoryMode = VictoryMode.MainBase;
        public static Difficulty difficulty = Difficulty.Normal;
        public static readonly string[] RichnessNames = { "贫瘠", "标准", "富饶" };
        public static readonly float[] RichnessValues = { 0.6f, 1f, 1.8f };  // 资源点数量乘区
        public static readonly string[] MapSizeNames = { "小型", "标准", "大型" };
        public static readonly float[] MapSizeValues = { 110f, 130f, 160f };
        public static readonly string[] VictoryNames = { "摧毁主基地", "摧毁所有建筑" };
        public static readonly string[] DifficultyNames = { "简单", "普通", "困难" };
        public static float Richness => RichnessValues[richnessIndex];
        public static float WorldSize => MapSizeValues[mapSizeIndex];
        public static float HalfSize => WorldSize * .5f;
    }

    public static class GameConfig
    {
        // 调试平衡总表现已迁移到可外部编辑的 RTSConfig ScriptableObject。
        // GameConfig 保留原有静态接口，内部代理到 RuntimeConfig，保证旧代码零改动。
        public static int InitialWood => RuntimeConfig.Data.initialWood;
        public static int InitialMana => RuntimeConfig.Data.initialMana;
        public static int AiHardBonusWood => RuntimeConfig.Data.aiHardBonusWood;
        public static int AiHardBonusMana => RuntimeConfig.Data.aiHardBonusMana;
        public static float AiHardGatherMultiplier => RuntimeConfig.Data.aiHardGatherMultiplier;
        public static float AiAssaultInterval => RuntimeConfig.Data.aiAssaultInterval;
        public static int AiMinAssaultForce => RuntimeConfig.Data.aiMinAssaultForce;
        public static int AiMaxAssaultForce => RuntimeConfig.Data.aiMaxAssaultForce;
        public static int BasePop => RuntimeConfig.Data.basePop;
        public static int HousePop => RuntimeConfig.Data.housePop;
        public static int MaxPopCap => RuntimeConfig.Data.maxPopCap;
        public static int GatherAmount => RuntimeConfig.Data.gatherAmount;
        public static float GatherTime => RuntimeConfig.Data.gatherTime;
        public static float TrainTime => RuntimeConfig.Data.trainTime;
        public static int MaxProductionQueue => RuntimeConfig.Data.maxProductionQueue;
        public static float ConstructionTime => RuntimeConfig.Data.constructionTime;
        public static int MaxBuildersPerBuilding => RuntimeConfig.Data.maxBuildersPerBuilding;
        public static float ConstructionDamageMultiplier => RuntimeConfig.Data.constructionDamageMultiplier;
        public static float CounterBonus => RuntimeConfig.Data.counterBonus;
        public static float SplashFrac => RuntimeConfig.Data.splashFrac;
        public static int HeroCap => RuntimeConfig.Data.heroCap;

        // ---- 人类（玩家阵营）：工人 + 步兵/远程/骑兵 ----
        public static UnitDef Farmer => RuntimeConfig.Farmer;
        public static UnitDef Footman => RuntimeConfig.Footman;
        public static UnitDef Archer => RuntimeConfig.Archer;
        public static UnitDef Knight => RuntimeConfig.Knight;

        // ---- 不死（AI 阵营）：工人 + 步兵/远程/骑兵 ----
        public static UnitDef Acolyte => RuntimeConfig.Acolyte;
        public static UnitDef Skeleton => RuntimeConfig.Skeleton;
        public static UnitDef DarkArcher => RuntimeConfig.DarkArcher;
        public static UnitDef DeathKnight => RuntimeConfig.DeathKnight;

        // ---- 英雄（全场唯一，兵营训练）：数值强化 + 攻击光环 + AOE 溅射 ----
        public static UnitDef LordKnight => RuntimeConfig.LordKnight;
        public static UnitDef DeathRanger => RuntimeConfig.DeathRanger;

        // ---- 第一层科技：兵种攻防强化（每级 +15%，造价 = 基础 × 等级）----
        public static TechDef HumanAtk => RuntimeConfig.Techs["human_atk"];
        public static TechDef HumanDef => RuntimeConfig.Techs["human_def"];
        public static TechDef UndeadAtk => RuntimeConfig.Techs["undead_atk"];
        public static TechDef UndeadDef => RuntimeConfig.Techs["undead_def"];

        public static Dictionary<string, TechDef> Techs => RuntimeConfig.Techs;

        // ---- 建筑表：主基地 + 每类兵种一座专属兵营（科技在步兵兵营研究）----
        public static BuildingDef Hall => RuntimeConfig.Hall;
        public static BuildingDef Barracks => RuntimeConfig.Barracks;
        public static BuildingDef Archery => RuntimeConfig.Archery;
        public static BuildingDef Stable => RuntimeConfig.Stable;
        public static BuildingDef Lumber => RuntimeConfig.Lumber;
        public static BuildingDef Tower => RuntimeConfig.Tower;
        public static BuildingDef House => RuntimeConfig.House;
        public static BuildingDef Crypt => RuntimeConfig.Crypt;
        public static BuildingDef DarkTemple => RuntimeConfig.DarkTemple;
        public static BuildingDef DeathStable => RuntimeConfig.DeathStable;

        public static BuildingDef[] HumanBuildings => RuntimeConfig.Data.factionBuildings.humanBuildingKinds
            .ConvertAll(k => RuntimeConfig.Buildings[k]).ToArray();
        public static BuildingDef[] UndeadBuildings => RuntimeConfig.Data.factionBuildings.undeadBuildingKinds
            .ConvertAll(k => RuntimeConfig.Buildings[k]).ToArray();
    }
}
