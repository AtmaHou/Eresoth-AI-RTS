using System.Collections.Generic;
using UnityEngine;

namespace Eresoth
{
    /// <summary>建筑：主基地（训练工人/交付资源）与兵营（训练战斗单位），带生产队列。</summary>
    public class Building : MonoBehaviour, ITargetable
    {
        public Team team;
        public string kind;          // "hall" | "barracks"
        public float hp, maxHp;
        public float radius;
        public Vector3 rally;        // 集结点
        public readonly List<UnitDef> queue = new();
        float timer;

        public static Building Spawn(Team team, string kind, Vector3 pos)
        {
            bool hall = kind == "hall";
            var root = new GameObject((hall ? "主基地" : "兵营"));
            root.transform.position = pos;

            Color body = team == Team.Player
                ? (hall ? new Color(.85f, .82f, .70f) : new Color(.70f, .65f, .50f))
                : (hall ? new Color(.35f, .25f, .45f) : new Color(.30f, .28f, .35f));
            Color top = team == Team.Player ? new Color(.95f, .80f, .35f) : new Color(.50f, .90f, .80f);

            Gfx.Prim(PrimitiveType.Cube, root.transform, new Vector3(0, hall ? 2f : 1.4f, 0),
                     hall ? new Vector3(5, 4, 5) : new Vector3(3.6f, 2.8f, 3.6f), body);
            Gfx.Prim(PrimitiveType.Cube, root.transform, new Vector3(0, hall ? 4.6f : 3.2f, 0),
                     hall ? new Vector3(2, 1.4f, 2) : new Vector3(1.4f, .9f, 1.4f), top);

            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 2, 0);
            col.size = hall ? new Vector3(5, 4, 5) : new Vector3(3.6f, 4, 3.6f);

            var b = root.AddComponent<Building>();
            b.team = team; b.kind = kind;
            b.maxHp = b.hp = hall ? 1500 : 800;
            b.radius = hall ? 3.4f : 2.6f;
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
            if (Game.I.over || queue.Count == 0) return;
            timer += Time.deltaTime;
            if (timer >= GameConfig.TrainTime)
            {
                timer = 0;
                var def = queue[0];
                queue.RemoveAt(0);
                Vector2 c = Random.insideUnitCircle * 1.5f;
                Unit.Spawn(team, def, rally + new Vector3(c.x, 0, c.y));
            }
        }

        public bool TryTrain(UnitDef def)
        {
            var g = Game.I;
            if (queue.Count >= 5) { if (team == Team.Player) g.Toast("生产队列已满"); return false; }
            if (g.PopCount((int)team) + def.pop > GameConfig.PopCap)
            { if (team == Team.Player) g.Toast("人口已达上限"); return false; }
            if (!g.TrySpend((int)team, def.wood, def.mana)) return false;
            queue.Add(def);
            return true;
        }

        // ---------- ITargetable ----------

        public Vector3 Pos => transform.position;
        public float Radius => radius;
        public Team Team => team;
        public bool Alive => hp > 0;
        public float Hp01 => hp / maxHp;
        public string DisplayName => kind == "hall" ? "主基地" : "兵营";

        public void Damage(float dmg)
        {
            hp -= dmg;
            if (hp <= 0) { hp = 0; Destroy(gameObject); }
        }
    }
}
