using System;
using System.Collections.Generic;
using UnityEngine;

namespace Eresoth
{
    /// <summary>可外部编辑的 RTS 总配置（ScriptableObject）。
    /// 包含兵种、建筑、科技、经济/AI 参数，运行时由 RuntimeConfig 加载为快速访问的静态定义。
    /// 策划/非程序人员可在 Unity Inspector 中直接调整数值，无需改代码。</summary>
    [CreateAssetMenu(fileName = "EresothRTSConfig", menuName = "Eresoth/RTS Config")]
    public class RTSConfig : ScriptableObject
    {
        [Header("经济参数")]
        public int initialWood = 70;
        public int initialMana = 180;
        public int aiHardBonusWood = 90;
        public int aiHardBonusMana = 180;
        public float aiHardGatherMultiplier = 1.35f;
        public int gatherAmount = 8;
        public float gatherTime = 2f;
        public float trainTime = 2.5f;
        public int maxProductionQueue = 5;
        public float constructionTime = 8f;
        public int maxBuildersPerBuilding = 3;
        public float constructionDamageMultiplier = 2f;
        public float counterBonus = 1.5f;
        public float splashFrac = 0.5f;
        public int heroCap = 1;

        [Header("人口")]
        public int basePop = 40;
        public int housePop = 15;
        public int maxPopCap = 100;

        [Header("AI 行为")]
        public float aiAssaultInterval = 18f;
        public int aiMinAssaultForce = 6;
        public int aiMaxAssaultForce = 12;
        public float aiDecisionInterval = 2f;
        public int aiMaxWorkers = 12;
        public int aiWorkerReassignIdleFrames = 3;   // 工人空闲超过 N 轮决策后强制重分配

        [Header("资源价格（用于 AI 动态分工）")]
        public float woodPrice = 1.0f;
        public float manaPrice = 1.0f;
        [Tooltip("库存占比低于此值时视为紧缺，提高采集优先级")]
        public float resourceShortageRatio = 0.35f;
        [Tooltip("最近 N 秒消耗用于预测未来缺口")]
        public float aiSpendHistorySeconds = 12f;

        [Header("单位定义")]
        public List<UnitConfig> units = new();

        [Header("建筑定义")]
        public List<BuildingConfig> buildings = new();

        [Header("科技定义")]
        public List<TechConfig> techs = new();

        [Header("阵营建筑表")]
        public FactionBuildings factionBuildings = new();
    }

    [Serializable]
    public class UnitConfig
    {
        public string id, name;
        public UnitKind kind;
        public float hp, dmg, range, speed, cooldown, aggro, size;
        public int wood, mana, pop;
        public bool worker;
        public bool hero;
        public float auraRadius, auraBonus;
        public float aoeRadius;
        public Color color = Color.white;
        public GameObject prefab;

        public UnitDef ToDef()
        {
            return new UnitDef(id, name, kind, hp, dmg, range, speed, cooldown, aggro, size,
                wood, mana, pop, worker, color, hero, auraRadius, auraBonus, aoeRadius, prefab);
        }
    }

    [Serializable]
    public class BuildingConfig
    {
        public string kind, name;
        public int wood, mana;
        public float hp, size;
        public List<string> trainUnitIds = new();
        public List<string> techIds = new();
        public float atk, atkRange, atkCd;
        public GameObject prefab;

        public BuildingDef ToDef(Dictionary<string, UnitDef> unitMap, Dictionary<string, TechDef> techMap)
        {
            var train = new List<UnitDef>();
            foreach (var id in trainUnitIds)
                if (unitMap.TryGetValue(id, out var ud)) train.Add(ud);

            var techs = new List<string>();
            foreach (var id in techIds)
                if (techMap.ContainsKey(id)) techs.Add(id);

            return new BuildingDef(kind, name, wood, mana, hp, size,
                train.ToArray(), techs.ToArray(), atk, atkRange, atkCd, prefab);
        }
    }

    [Serializable]
    public class TechConfig
    {
        public string id, name, desc;
        public int wood, mana;
        public float time;
        public int maxLevel;
        public TechEffect effect;

        public TechDef ToDef() => new(id, name, desc, wood, mana, time, maxLevel, effect);
    }

    [Serializable]
    public class FactionBuildings
    {
        public List<string> humanBuildingKinds = new() { "barracks", "archery", "stable", "resource_hub", "tower", "house" };
        public List<string> undeadBuildingKinds = new() { "crypt", "dark_temple", "death_stable", "resource_hub", "tower", "house" };
    }
}
