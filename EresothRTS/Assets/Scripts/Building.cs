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

        public string kind => def.kind;

        public static Building Spawn(Team team, BuildingDef def, Vector3 pos)
        {
            bool hall = def.kind == "hall";
            var root = new GameObject(def.name);
            root.transform.position = pos;

            Color body = team == Team.Player
                ? (hall ? new Color(.85f, .82f, .70f) : new Color(.70f, .65f, .50f))
                : (hall ? new Color(.35f, .25f, .45f) : new Color(.30f, .28f, .35f));
            Color top = team == Team.Player ? new Color(.95f, .80f, .35f) : new Color(.50f, .90f, .80f);

            Gfx.Prim(PrimitiveType.Cube, root.transform, new Vector3(0, def.size * 0.4f, 0),
                     new Vector3(def.size, def.size * 0.8f, def.size), body);
            Gfx.Prim(PrimitiveType.Cube, root.transform, new Vector3(0, def.size * 0.8f + def.size * 0.14f, 0),
                     new Vector3(def.size * 0.4f, def.size * 0.28f, def.size * 0.4f), top);

            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0, def.size * 0.4f, 0);
            col.size = new Vector3(def.size, def.size * 0.8f, def.size);

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
        }

        public bool TryTrain(UnitDef unitDef)
        {
            var g = Game.I;
            if (queue.Count >= 5) { if (team == Team.Player) g.Toast("生产队列已满"); return false; }
            if (g.PopCount((int)team) + unitDef.pop > GameConfig.PopCap)
            { if (team == Team.Player) g.Toast("人口已达上限"); return false; }
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
            if (lvl >= t.maxLevel) { if (team == Team.Player) g.Toast("已达最高等级"); return false; }
            if (research != null) { if (team == Team.Player) g.Toast("正在研究中"); return false; }
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
