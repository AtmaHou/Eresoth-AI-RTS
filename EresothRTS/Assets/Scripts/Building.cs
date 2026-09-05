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
            // 随机朝向（AI 槽位建筑不再千篇一律），主基地面向地图中心
            if (def.kind != "hall")
                root.transform.rotation = Quaternion.Euler(0, Mathf.Floor(Random.value * 4) * 90f, 0);
            else if (pos.sqrMagnitude > 0.1f)
                root.transform.rotation = Quaternion.LookRotation(-pos.normalized);

            float s = def.size;
            // 外观：完整风格化建筑（基座/屋顶/门窗/旗帜/阵营装饰），全部程序化拼装
            if (def.prefab != null)
            {
                var inst = Object.Instantiate(def.prefab, root.transform, false);
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
            }
            else
            {
                Models.BuildBuilding(root.transform, team, def.kind, s);
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
                    // 出厂：在建筑门口生成，自己走到集结点（不再瞬移到集结点）
                    var dir = rally - transform.position; dir.y = 0;
                    if (dir.sqrMagnitude < .01f) dir = Vector3.forward;
                    dir.Normalize();
                    Vector2 c = Random.insideUnitCircle * 1.2f;
                    var spawnP = transform.position + dir * (radius + 1.5f) + new Vector3(-dir.z, 0, dir.x) * c.x + dir * c.y;
                    spawnP.y = Game.TerrainHeight(spawnP.x, spawnP.z);
                    var u = Unit.Spawn(team, def, spawnP);
                    // 集结点压在资源上时工人出厂即上工，其他单位/情况走到集结点
                    var w = u.GetComponent<Worker>();
                    var n = Game.I.NearestNodeAny(rally);
                    if (w != null && n != null && Vector3.Distance(n.transform.position, rally) < 5f)
                        w.GatherAt(n);
                    else
                        u.CommandMove(rally);
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
            if (g.PopCount((int)team) + unitDef.pop > g.PopCap(team))
            { if (team == g.playerTeam) g.Toast("人口已达上限（建民居可提升）"); return false; }
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
