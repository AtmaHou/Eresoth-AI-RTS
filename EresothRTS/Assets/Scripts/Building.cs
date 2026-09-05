using System.Collections.Generic;
using UnityEngine;

namespace Eresoth
{
    /// <summary>建筑：数据驱动（BuildingDef）。主基地训练工人/收资源，
    /// 其他建筑训练兵种或研究科技，各带生产/研究队列（单研究槽）。</summary>
    public class Building : MonoBehaviour, ITargetable
    {
        public Team team;
        public BuildingDef def;
        public float hp;
        public float radius;
        public Vector3 rally;        // 集结点
        public readonly List<UnitDef> queue = new();
        public string research;      // 当前研究中的科技 id，null = 空闲
        public float researchTimer;
        float timer;
        float towerCd;               // 防御塔攻击冷却

        public string kind => def.kind;

        public static Building Spawn(Team team, BuildingDef def, Vector3 pos)
        {
            var root = new GameObject(def.name);
            root.transform.position = pos;

            Color body = team == Team.Player
                ? (def.kind == "hall" ? new Color(.85f, .82f, .70f) : new Color(.70f, .65f, .50f))
                : (def.kind == "hall" ? new Color(.35f, .25f, .45f) : new Color(.30f, .28f, .35f));
            Color top = team == Team.Player ? new Color(.95f, .80f, .35f) : new Color(.50f, .90f, .80f);
            float s = def.size;

            // 造型按 kind 差异化组合（仍全程序化图元）
            switch (def.kind)
            {
                case "hall":      // 主基地：大本体 + 中层 + 圆顶
                    Gfx.Prim(PrimitiveType.Cube, root.transform, new Vector3(0, s * 0.4f, 0), new Vector3(s, s * 0.8f, s), body);
                    Gfx.Prim(PrimitiveType.Cube, root.transform, new Vector3(0, s * 0.9f, 0), new Vector3(s * 0.6f, s * 0.28f, s * 0.6f), body);
                    Gfx.Prim(PrimitiveType.Sphere, root.transform, new Vector3(0, s * 1.15f, 0), Vector3.one * s * 0.35f, top);
                    break;
                case "barracks":  // 兵营/地穴：本体 + 角楼
                case "crypt":
                    Gfx.Prim(PrimitiveType.Cube, root.transform, new Vector3(0, s * 0.4f, 0), new Vector3(s, s * 0.8f, s), body);
                    Gfx.Prim(PrimitiveType.Cylinder, root.transform, new Vector3(s * 0.42f, s * 0.65f, s * 0.42f),
                             new Vector3(s * 0.3f, s * 0.6f, s * 0.3f), top);
                    break;
                case "archery":   // 弓箭场/诅咒神殿：圆柱塔身 + 球顶
                case "dark_temple":
                    Gfx.Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0, s * 0.55f, 0),
                             new Vector3(s * 0.8f, s * 1.1f, s * 0.8f), body);
                    Gfx.Prim(PrimitiveType.Sphere, root.transform, new Vector3(0, s * 1.2f, 0), Vector3.one * s * 0.4f, top);
                    break;
                case "stable":    // 马厩/死亡马厩：宽扁棚屋 + 门柱
                case "death_stable":
                    Gfx.Prim(PrimitiveType.Cube, root.transform, new Vector3(0, s * 0.3f, 0),
                             new Vector3(s * 1.4f, s * 0.6f, s * 1.1f), body);
                    Gfx.Prim(PrimitiveType.Cylinder, root.transform, new Vector3(s * 0.55f, s * 0.5f, s * 0.45f),
                             new Vector3(s * 0.18f, s, s * 0.18f), top);
                    Gfx.Prim(PrimitiveType.Cylinder, root.transform, new Vector3(-s * 0.55f, s * 0.5f, s * 0.45f),
                             new Vector3(s * 0.18f, s, s * 0.18f), top);
                    break;
                case "lumber":    // 伐木场：本体 + 横放原木
                    Gfx.Prim(PrimitiveType.Cube, root.transform, new Vector3(0, s * 0.4f, 0), new Vector3(s, s * 0.8f, s), body);
                    var log = Gfx.Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0, s * 0.9f, 0),
                             new Vector3(s * 0.25f, s * 0.9f, s * 0.25f), top);
                    log.transform.localRotation = Quaternion.Euler(0, 0, 90f);
                    break;
                default:          // 箭塔：高瘦塔身 + 塔顶平台
                    Gfx.Prim(PrimitiveType.Cube, root.transform, new Vector3(0, s * 0.6f, 0),
                             new Vector3(s * 0.7f, s * 1.2f, s * 0.7f), body);
                    Gfx.Prim(PrimitiveType.Cube, root.transform, new Vector3(0, s * 1.2f + s * 0.14f, 0),
                             new Vector3(s * 0.55f, s * 0.28f, s * 0.55f), top);
                    break;
            }

            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0, s * 0.5f, 0);
            col.size = new Vector3(s * 1.4f, s, s * 1.2f);

            var b = root.AddComponent<Building>();
            b.team = team; b.def = def;
            b.hp = def.hp;
            b.radius = def.size * 0.7f;
            b.rally = pos + (pos.sqrMagnitude > 0.1f ? -pos.normalized * 7f : Vector3.right * 7f);

            Game.I.buildings.Add(b);
            return b;
        }

        void OnDestroy()
        {
            if (Game.I == null) return;
            Game.I.buildings.Remove(this);
            Game.I.CheckEnd();
        }

        void Update()
        {
            if (Game.I.over) return;
            float dt = Time.deltaTime;

            if (queue.Count > 0)
            {
                timer += dt;
                if (timer >= GameConfig.TrainTime)
                {
                    timer = 0;
                    var def = queue[0];
                    queue.RemoveAt(0);
                    Vector2 c = Random.insideUnitCircle * 1.5f;
                    Unit.Spawn(team, def, rally + new Vector3(c.x, 0, c.y));
                }
            }

            if (research != null)
            {
                researchTimer += dt;
                if (researchTimer >= GameConfig.Techs[research].time)
                {
                    string done = research;
                    research = null;
                    researchTimer = 0;
                    Game.I.FinishResearch(team, done);
                }
            }

            // 防御塔：自动攻击射程内最近的敌方单位（发射弹道）
            if (def.atkRange > 0)
            {
                towerCd -= dt;
                if (towerCd <= 0)
                {
                    Unit best = null; float bd = def.atkRange;
                    foreach (var u in Game.I.units)
                    {
                        if (u.team == team) continue;
                        float d = Vector3.Distance(transform.position, u.transform.position);
                        if (d < bd) { bd = d; best = u; }
                    }
                    if (best != null)
                    {
                        towerCd = def.atkCd;
                        Game.I.SpawnProjectile(team, transform.position + Vector3.up * def.size,
                                               best, def.atk * Game.I.AtkMult((int)team));
                    }
                }
            }
        }

        public bool TryTrain(UnitDef unitDef)
        {
            var g = Game.I;
            if (queue.Count >= 5) { if (team == g.playerTeam) g.Toast("生产队列已满"); return false; }
            if (g.PopCount((int)team) + unitDef.pop > GameConfig.PopCap)
            { if (team == g.playerTeam) g.Toast("人口已达上限"); return false; }
            if (unitDef.hero && g.units.Exists(u => u.team == team && u.def.hero))
            { if (team == g.playerTeam) g.Toast("英雄只能同时存在一位"); return false; }
            if (!g.TrySpend((int)team, unitDef.wood, unitDef.mana)) return false;
            queue.Add(unitDef);
            return true;
        }

        /// <summary>开始研究科技：校验归属建筑/等级上限/研究槽空闲，花费 = 基础 × (当前等级+1)。</summary>
        public bool TryResearch(string techId)
        {
            var g = Game.I;
            var t = GameConfig.Techs[techId];
            int lvl = g.TechLevel(team, t.effect);
            if (lvl >= t.maxLevel) { if (team == g.playerTeam) g.Toast("已达最高等级"); return false; }
            if (research != null) { if (team == g.playerTeam) g.Toast("正在研究中"); return false; }
            int mult = lvl + 1;
            if (!g.TrySpend((int)team, t.wood * mult, t.mana * mult)) return false;
            research = techId;
            researchTimer = 0;
            return true;
        }

        // ---------- ITargetable ----------

        public Vector3 Pos => transform.position;
        public float Radius => radius;
        public Team Team => team;
        public bool Alive => hp > 0;
        public float Hp01 => hp / def.hp;
        public string DisplayName => def.name;

        public void Damage(float dmg)
        {
            hp -= dmg;
            if (hp <= 0) { hp = 0; Destroy(gameObject); }
        }
    }
}
