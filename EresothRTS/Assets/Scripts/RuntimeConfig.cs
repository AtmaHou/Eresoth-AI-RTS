using System.Collections.Generic;
using UnityEngine;

namespace Eresoth
{
    /// <summary>运行时配置访问层：从 RTSConfig ScriptableObject 加载一次，
    /// 生成所有 UnitDef / BuildingDef / TechDef 并提供静态快速访问。
    /// 保证 GameConfig 的静态字段仍可被旧代码引用。</summary>
    public static class RuntimeConfig
    {
        public static RTSConfig Data { get; private set; }

        public static readonly Dictionary<string, UnitDef> Units = new();
        public static readonly Dictionary<string, BuildingDef> Buildings = new();
        public static readonly Dictionary<string, TechDef> Techs = new();

        public static UnitDef Farmer => Units["farmer"];
        public static UnitDef Footman => Units["footman"];
        public static UnitDef Archer => Units["archer"];
        public static UnitDef Knight => Units["knight"];
        public static UnitDef Acolyte => Units["acolyte"];
        public static UnitDef Skeleton => Units["skeleton"];
        public static UnitDef DarkArcher => Units["darkarcher"];
        public static UnitDef DeathKnight => Units["deathknight"];
        public static UnitDef LordKnight => Units["lordknight"];
        public static UnitDef DeathRanger => Units["deathranger"];

        public static BuildingDef Hall => Buildings["hall"];
        public static BuildingDef Barracks => Buildings["barracks"];
        public static BuildingDef Archery => Buildings["archery"];
        public static BuildingDef Stable => Buildings["stable"];
        public static BuildingDef Lumber => Buildings["resource_hub"];
        public static BuildingDef Tower => Buildings["tower"];
        public static BuildingDef House => Buildings["house"];
        public static BuildingDef Crypt => Buildings["crypt"];
        public static BuildingDef DarkTemple => Buildings["dark_temple"];
        public static BuildingDef DeathStable => Buildings["death_stable"];

        public static void Initialize()
        {
            if (Data != null) return;

            var cfg = Resources.Load<RTSConfig>("EresothRTSConfig");
            if (cfg == null)
            {
                Debug.LogWarning("[RuntimeConfig] 未找到 Resources/EresothRTSConfig.asset，使用默认配置。");
                cfg = ScriptableObject.CreateInstance<RTSConfig>();
                FillDefaults(cfg);
            }
            Data = cfg;

            Units.Clear();
            foreach (var u in cfg.units)
            {
                var d = u.ToDef();
                Units[d.id] = d;
            }

            Techs.Clear();
            foreach (var t in cfg.techs)
            {
                var d = t.ToDef();
                Techs[d.id] = d;
            }

            Buildings.Clear();
            foreach (var b in cfg.buildings)
            {
                var d = b.ToDef(Units, Techs);
                Buildings[d.kind] = d;
            }
        }

        static void FillDefaults(RTSConfig c)
        {
            c.initialWood = 70;
            c.initialMana = 180;
            c.aiHardBonusWood = 90;
            c.aiHardBonusMana = 180;
            c.aiHardGatherMultiplier = 1.35f;
            c.gatherAmount = 8;
            c.gatherTime = 2f;
            c.trainTime = 2.5f;
            c.maxProductionQueue = 5;
            c.constructionTime = 8f;
            c.maxBuildersPerBuilding = 3;
            c.constructionDamageMultiplier = 2f;
            c.counterBonus = 1.5f;
            c.splashFrac = 0.5f;
            c.heroCap = 1;
            c.basePop = 40;
            c.housePop = 15;
            c.maxPopCap = 100;
            c.aiAssaultInterval = 18f;
            c.aiMinAssaultForce = 6;
            c.aiMaxAssaultForce = 12;
            c.aiDecisionInterval = 2f;
            c.aiMaxWorkers = 12;
            c.aiWorkerReassignIdleFrames = 3;
            c.woodPrice = 1f;
            c.manaPrice = 1f;
            c.resourceShortageRatio = 0.35f;
            c.aiSpendHistorySeconds = 12f;

            c.units = new()
            {
                new UnitConfig { id="farmer", name="农夫", kind=UnitKind.Worker, hp=45, dmg=3, range=1.2f, speed=5.5f, cooldown=1f, aggro=0, size=0.80f, wood=10, mana=40, pop=1, worker=true, color=new Color(.85f,.75f,.55f) },
                new UnitConfig { id="footman", name="步兵", kind=UnitKind.Infantry, hp=110, dmg=10, range=1.6f, speed=5f, cooldown=1.1f, aggro=9, size=0.95f, wood=10, mana=50, pop=1, color=new Color(.55f,.65f,.95f) },
                new UnitConfig { id="archer", name="长弓手", kind=UnitKind.Ranged, hp=65, dmg=12, range=9f, speed=5f, cooldown=1.2f, aggro=10, size=0.85f, wood=10, mana=50, pop=1, color=new Color(.45f,.85f,.55f) },
                new UnitConfig { id="knight", name="骑士", kind=UnitKind.Cavalry, hp=170, dmg=17, range=1.8f, speed=7.5f, cooldown=1.3f, aggro=9, size=1.15f, wood=30, mana=90, pop=2, color=new Color(.90f,.85f,.40f) },
                new UnitConfig { id="acolyte", name="侍僧", kind=UnitKind.Worker, hp=40, dmg=3, range=1.2f, speed=5.5f, cooldown=1f, aggro=0, size=0.80f, wood=10, mana=40, pop=1, worker=true, color=new Color(.50f,.40f,.55f) },
                new UnitConfig { id="skeleton", name="骷髅兵", kind=UnitKind.Infantry, hp=100, dmg=9, range=1.5f, speed=5f, cooldown=1f, aggro=9, size=0.85f, wood=5, mana=45, pop=1, color=new Color(.90f,.90f,.85f) },
                new UnitConfig { id="darkarcher", name="亡灵射手", kind=UnitKind.Ranged, hp=60, dmg=11, range=9f, speed=5f, cooldown=1.2f, aggro=10, size=0.85f, wood=10, mana=50, pop=1, color=new Color(.65f,.45f,.85f) },
                new UnitConfig { id="deathknight", name="死亡骑士", kind=UnitKind.Cavalry, hp=160, dmg=16, range=1.8f, speed=7.5f, cooldown=1.3f, aggro=9, size=1.15f, wood=30, mana=90, pop=2, color=new Color(.55f,.60f,.70f) },
                new UnitConfig { id="lordknight", name="骑士团长", kind=UnitKind.Cavalry, hp=650, dmg=34, range=2.2f, speed=7.5f, cooldown=1.2f, aggro=10, size=1.50f, wood=100, mana=300, pop=3, hero=true, auraRadius=10f, auraBonus=0.25f, aoeRadius=3f, color=new Color(1f,.92f,.45f) },
                new UnitConfig { id="deathranger", name="霜骨巫妖", kind=UnitKind.Ranged, hp=520, dmg=30, range=10f, speed=5.5f, cooldown=1.1f, aggro=11, size=1.10f, wood=100, mana=300, pop=3, hero=true, auraRadius=10f, auraBonus=0.25f, aoeRadius=2.5f, color=new Color(.85f,.55f,1f) },
            };

            c.buildings = new()
            {
                new BuildingConfig { kind="hall", name="主基地", wood=0, mana=0, hp=1500, size=5.0f },
                new BuildingConfig { kind="barracks", name="兵营", wood=50, mana=100, hp=800, size=3.6f, trainUnitIds=new(){"footman","lordknight"}, techIds=new(){"human_atk","human_def"} },
                new BuildingConfig { kind="archery", name="弓箭场", wood=40, mana=110, hp=700, size=3.2f, trainUnitIds=new(){"archer"} },
                new BuildingConfig { kind="stable", name="马厩", wood=50, mana=140, hp=800, size=3.6f, trainUnitIds=new(){"knight"} },
                new BuildingConfig { kind="resource_hub", name="资源收集站", wood=30, mana=80, hp=600, size=3.0f },
                new BuildingConfig { kind="tower", name="箭塔", wood=20, mana=80, hp=350, size=2.6f, atk=12, atkRange=11, atkCd=1f },
                new BuildingConfig { kind="house", name="民居", wood=20, mana=70, hp=400, size=3.0f },
                new BuildingConfig { kind="crypt", name="地穴", wood=50, mana=100, hp=800, size=3.6f, trainUnitIds=new(){"skeleton","deathranger"}, techIds=new(){"undead_atk","undead_def"} },
                new BuildingConfig { kind="dark_temple", name="诅咒神殿", wood=40, mana=110, hp=700, size=3.2f, trainUnitIds=new(){"darkarcher"} },
                new BuildingConfig { kind="death_stable", name="死亡马厩", wood=50, mana=140, hp=800, size=3.6f, trainUnitIds=new(){"deathknight"} },
            };

            c.techs = new()
            {
                new TechConfig { id="human_atk", name="利刃淬炼", desc="全军攻击 +15%", wood=50, mana=100, time=20f, maxLevel=2, effect=TechEffect.Atk },
                new TechConfig { id="human_def", name="坚壁工事", desc="全军受伤减免约 13%", wood=50, mana=100, time=20f, maxLevel=2, effect=TechEffect.Def },
                new TechConfig { id="undead_atk", name="白骨磨锋", desc="全军攻击 +15%", wood=50, mana=100, time=20f, maxLevel=2, effect=TechEffect.Atk },
                new TechConfig { id="undead_def", name="腐躯硬化", desc="全军受伤减免约 13%", wood=50, mana=100, time=20f, maxLevel=2, effect=TechEffect.Def },
            };

            c.factionBuildings = new FactionBuildings
            {
                humanBuildingKinds = new() { "barracks", "archery", "stable", "resource_hub", "tower", "house" },
                undeadBuildingKinds = new() { "crypt", "dark_temple", "death_stable", "resource_hub", "tower", "house" }
            };
        }
    }
}
