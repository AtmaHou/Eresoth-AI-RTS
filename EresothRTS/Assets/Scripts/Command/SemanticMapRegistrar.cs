using UnityEngine;

namespace Eresoth
{
    /// <summary>语义点注册器：在 Game.BuildWorld() 末尾根据本局实际布局生成语义点。
    /// 命名以玩家视角为准（own=玩家阵营，enemy=AI 阵营），保证"东矿"等别名稳定可解析。</summary>
    public static class SemanticMapRegistrar
    {
        public static void Register(Game g)
        {
            SemanticMap.Clear();
            Team own = g.playerTeam;
            Vector3 ownBase = g.baseCenter[(int)own];
            Vector3 enemyBase = g.baseCenter[(int)own == 0 ? 1 : 0];

            SemanticMap.Register("own_main_base", "己方主基地", ownBase);
            SemanticMap.Register("enemy_main_base", "敌方主基地", enemyBase);
            SemanticMap.Register("center_field", "地图中心", Vector3.zero);
            SemanticMap.Register("own_retreat_point", "己方撤退点", RetreatPoint(ownBase));
            // 动态点：初始为敌方主基地，之后由 ForceManager 更新为可见敌军质心
            SemanticMap.Register("enemy_frontline", "敌方前线", enemyBase, isDynamic: true);

            // 资源点：按半场归属（离谁基地近）+ 东西方位（x 坐标）命名
            RegisterResources(g, ownBase, enemyBase, "mana", "魔法矿");
            RegisterResources(g, ownBase, enemyBase, "wood", "树林");

            Debug.Log($"[SemanticMap] 注册 {SemanticMap.All.Count} 个语义点");
        }

        static Vector3 RetreatPoint(Vector3 basePos)
        {
            // 撤退点：主基地向地图边缘方向（远离中心）再退 6 格
            Vector3 dir = basePos.sqrMagnitude > 0.1f ? basePos.normalized : Vector3.one.normalized;
            var p = basePos + dir * 6f;
            p.y = Game.TerrainHeight(p.x, p.z);
            return p;
        }

        /// <summary>每半场每资源类型注册东/西两个代表点（取该方向最远的资源点，同名多点取簇中心）。</summary>
        static void RegisterResources(Game g, Vector3 ownBase, Vector3 enemyBase, string kind, string cn)
        {
            ResourceNode ownE = null, ownW = null, eneE = null, eneW = null;
            foreach (var n in g.nodes)
            {
                if (n == null || n.kind != kind) continue;
                var p = n.transform.position;
                // 中央富集区（距中心 12 内）单独命名
                if (kind == "mana" && p.magnitude < 12f)
                {
                    SemanticMap.Register("center_mana", "中央富集矿", p);
                    continue;
                }
                bool ownHalf = Vector3.Distance(p, ownBase) < Vector3.Distance(p, enemyBase);
                if (ownHalf)
                {
                    if (ownE == null || p.x > ownE.transform.position.x) ownE = n;
                    if (ownW == null || p.x < ownW.transform.position.x) ownW = n;
                }
                else
                {
                    if (eneE == null || p.x > eneE.transform.position.x) eneE = n;
                    if (eneW == null || p.x < eneW.transform.position.x) eneW = n;
                }
            }
            if (kind == "mana" && !SemanticMap.Exists("center_mana"))
                SemanticMap.Register("center_mana", "中央富集矿", Vector3.zero); // 兜底：指向地图中心
            if (ownE != null) SemanticMap.Register($"own_east_{kind}", $"己方东侧{cn}", ownE.transform.position);
            if (ownW != null) SemanticMap.Register($"own_west_{kind}", $"己方西侧{cn}", ownW.transform.position);
            if (eneE != null) SemanticMap.Register($"enemy_east_{kind}", $"敌方东侧{cn}", eneE.transform.position);
            if (eneW != null) SemanticMap.Register($"enemy_west_{kind}", $"敌方西侧{cn}", eneW.transform.position);
        }
    }
}
