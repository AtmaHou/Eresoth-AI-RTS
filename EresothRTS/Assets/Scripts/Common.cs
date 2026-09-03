using System.Collections.Generic;
using UnityEngine;

namespace Eresoth
{
    public enum Team { Player = 0, Enemy = 1 }

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

    /// <summary>兵种静态数据。数值与设计文档 3.2 节对应（第一阶段为简化子集）。</summary>
    public class UnitDef
    {
        public string id, name;
        public float hp, dmg, range, speed, cooldown, aggro, size;
        public int wood, mana, pop;
        public bool worker;
        public Color color;

        public UnitDef(string id, string name, float hp, float dmg, float range, float speed,
            float cooldown, float aggro, float size, int wood, int mana, int pop, bool worker, Color color)
        {
            this.id = id; this.name = name; this.hp = hp; this.dmg = dmg; this.range = range;
            this.speed = speed; this.cooldown = cooldown; this.aggro = aggro; this.size = size;
            this.wood = wood; this.mana = mana; this.pop = pop; this.worker = worker; this.color = color;
        }
    }

    public static class GameConfig
    {
        public const int PopCap = 30;
        public const int BarracksWood = 150;
        public const int GatherAmount = 8;      // 单次采集量
        public const float GatherTime = 2f;     // 单次采集耗时(秒)
        public const float TrainTime = 2.5f;    // 单位训练耗时(秒)

        // ---- 人类（玩家阵营）----
        public static readonly UnitDef Farmer  = new("farmer",  "农夫",     45,  3, 1.2f, 5.5f, 1.0f, 0,  0.80f,  50,  0, 1, true,  new Color(.85f, .75f, .55f));
        public static readonly UnitDef Militia = new("militia", "民兵",     90,  9, 1.6f, 5.5f, 1.1f, 9,  0.90f,  40,  0, 1, false, new Color(.55f, .65f, .95f));
        public static readonly UnitDef Archer  = new("archer",  "长弓手",   60, 11, 9.0f, 5.0f, 1.2f, 10, 0.85f,  50, 10, 1, false, new Color(.45f, .85f, .55f));
        public static readonly UnitDef Knight  = new("knight",  "重甲骑士", 190, 18, 1.8f, 6.5f, 1.3f, 9,  1.15f,  90, 30, 2, false, new Color(.90f, .85f, .40f));

        // ---- 不死（AI 阵营）----
        public static readonly UnitDef Acolyte = new("acolyte", "侍僧",     40,  3, 1.2f, 5.5f, 1.0f, 0,  0.80f,  50,  0, 1, true,  new Color(.50f, .40f, .55f));
        public static readonly UnitDef Skeleton= new("skeleton","骷髅兵",   55,  7, 1.5f, 5.5f, 1.0f, 9,  0.85f,  25,  0, 1, false, new Color(.90f, .90f, .85f));
        public static readonly UnitDef Ghoul   = new("ghoul",   "食尸鬼",   80,  8, 1.4f, 6.0f, 1.0f, 9,  0.90f,  45,  5, 1, false, new Color(.60f, .85f, .45f));
        public static readonly UnitDef Abom    = new("abom",    "憎恶",     240, 22, 2.0f, 4.2f, 1.5f, 9,  1.30f, 110, 70, 3, false, new Color(.55f, .35f, .60f));

        public static readonly UnitDef[] HumanTrain  = { Militia, Archer, Knight };
        public static readonly UnitDef[] UndeadTrain = { Skeleton, Ghoul, Abom };
    }
}
