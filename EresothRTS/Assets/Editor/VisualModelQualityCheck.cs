using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Eresoth.EditorTools
{
    /// <summary>自动化建模质量检查（任务书第十章）：只生成临时对象做结构/几何/材质/挂点/预算检查，
    /// 不运行游戏、不修改场景或游戏状态。报告写入 ProjectBook/VisualModelQualityReport.md。</summary>
    public static class VisualModelQualityCheck
    {
        const string MenuRoot = "工具/Eresoth RTS/";
        const int ErrBudgetNormal = 12, ErrBudgetCavalry = 18, ErrBudgetHero = 28;

        static readonly List<string> errors = new(), warnings = new(), infos = new();

        [MenuItem(MenuRoot + "Visual Model Quality Check")]
        public static void Run()
        {
            errors.Clear(); warnings.Clear(); infos.Clear();
            var defs = new[]
            {
                (def: GameConfig.Farmer, team: Team.Player),
                (def: GameConfig.Footman, team: Team.Player),
                (def: GameConfig.Archer, team: Team.Player),
                (def: GameConfig.Knight, team: Team.Player),
                (def: GameConfig.LordKnight, team: Team.Player),
                (def: GameConfig.Acolyte, team: Team.Enemy),
                (def: GameConfig.Skeleton, team: Team.Enemy),
                (def: GameConfig.DarkArcher, team: Team.Enemy),
                (def: GameConfig.DeathKnight, team: Team.Enemy),
                (def: GameConfig.DeathRanger, team: Team.Enemy),
            };
            var sharedMats = new HashSet<Material>();
            var matrix = new StringBuilder("单位,阵营,兵种,英雄,Renderer,预算,材质数,发光节点,挂点,尺寸,结论\n");

            foreach (var (def, team) in defs)
            {
                var root = new GameObject("QC_" + def.id);
                try
                {
                    BodyRig rig = Models.BuildUnit(root.transform, team, def);
                    CheckUnit(root, rig, def, team, sharedMats, matrix);
                }
                catch (System.Exception e)
                {
                    errors.Add($"[ERROR] {def.id} 构建异常: {e.Message}");
                }
                Object.DestroyImmediate(root);
            }

            infos.Add($"[INFO] 全部单位共享 {sharedMats.Count} 个材质实例");

            var sb = new StringBuilder("# Eresoth RTS 视觉模型质量检查报告\n\n");
            sb.AppendLine($"生成时间: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Summary: ERROR={errors.Count} WARNING={warnings.Count} INFO={infos.Count} PASS={errors.Count == 0}\n");
            foreach (var l in errors) sb.AppendLine(l);
            foreach (var l in warnings) sb.AppendLine(l);
            foreach (var l in infos) sb.AppendLine(l);
            sb.AppendLine("\n## 识别矩阵\n");
            sb.Append(matrix);

            string path = Path.Combine(Application.dataPath, "../../ProjectBook/VisualModelQualityReport.md");
            File.WriteAllText(path, sb.ToString());
            Debug.Log($"[VisualModelQualityCheck] ERROR={errors.Count} WARNING={warnings.Count} INFO={infos.Count}\n{sb}");
        }

        static void CheckUnit(GameObject root, BodyRig rig, UnitDef def, Team team, HashSet<Material> sharedMats, StringBuilder matrix)
        {
            string tag = def.id;
            int errsBefore = errors.Count;

            // ---- 10.3 几何与 Transform ----
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var p = t.localPosition; var sc = t.localScale;
                if (!IsFinite(p) || !IsFinite(t.localRotation) || !IsFinite(sc))
                    errors.Add($"[ERROR] {tag} 节点 {t.name} 含 NaN/Infinity");
                if (Mathf.Abs(sc.x) < 1e-6f || Mathf.Abs(sc.y) < 1e-6f || Mathf.Abs(sc.z) < 1e-6f)
                    errors.Add($"[ERROR] {tag} 节点 {t.name} scale 为零");
                if (sc.x < 0 || sc.y < 0 || sc.z < 0)
                    errors.Add($"[ERROR] {tag} 节点 {t.name} 负缩放");
                // ---- 10.4 碰撞与逻辑隔离 ----
                if (t != root.transform && t.GetComponent<Collider>() != null)
                    errors.Add($"[ERROR] {tag} 视觉节点 {t.name} 含 Collider");
                if (t.GetComponent<Rigidbody>() != null || t.GetComponent<UnityEngine.AI.NavMeshAgent>() != null)
                    errors.Add($"[ERROR] {tag} 视觉节点 {t.name} 含刚体/寻路组件");
                var mb = t.GetComponent<MonoBehaviour>();
                if (mb != null)
                    errors.Add($"[ERROR] {tag} 视觉节点 {t.name} 含脚本 {mb.GetType().Name}");
            }

            // Mesh 有效性 + 渲染统计
            int renderers = 0, emissiveNodes = 0;
            var bounds = new Bounds(root.transform.position, Vector3.zero);
            bool boundsInit = false;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) { errors.Add($"[ERROR] {tag} {mf.name} 缺 Mesh"); continue; }
                if (mesh.vertexCount == 0 || mesh.triangles.Length == 0)
                    errors.Add($"[ERROR] {tag} {mf.name} Mesh 为空");
                else
                {
                    int maxIdx = 0;
                    foreach (var i in mesh.triangles) maxIdx = Mathf.Max(maxIdx, i);
                    if (maxIdx >= mesh.vertexCount)
                        errors.Add($"[ERROR] {tag} {mf.name} 三角形索引越界");
                }
            }
            foreach (var rd in root.GetComponentsInChildren<Renderer>(true))
            {
                renderers++;
                if (rd.sharedMaterial == null || rd.sharedMaterial.shader == null)
                    errors.Add($"[ERROR] {tag} {rd.name} 材质/Shader 缺失");
                else
                {
                    sharedMats.Add(rd.sharedMaterial);
                    if (rd.sharedMaterial.IsKeywordEnabled("_EMISSION")) emissiveNodes++;
                }
                var b = rd.bounds;
                if (!boundsInit) { bounds = b; boundsInit = true; } else bounds.Encapsulate(b);
            }
            if (!boundsInit) errors.Add($"[ERROR] {tag} 无任何 Renderer");

            // 尺寸基线（10.3）
            float s = def.size;
            if (boundsInit)
            {
                float h = bounds.size.y, minY = bounds.min.y - root.transform.position.y;
                float horiz = new Vector2(bounds.center.x - root.transform.position.x, bounds.center.z - root.transform.position.z).magnitude;
                float hMin = def.hero ? .9f : .6f, hMax = def.hero ? 5f : 3.5f;
                if (h < hMin * s || h > hMax * s)
                    errors.Add($"[ERROR] {tag} 高度 {h:F2} 超出基线 [{hMin * s:F2},{hMax * s:F2}] (size={s})");
                if (horiz > .8f * s * 3f)
                    warnings.Add($"[WARNING] {tag} 模型中心水平偏移 {horiz:F2} 较大");
                if (minY < -.05f)
                    warnings.Add($"[WARNING] {tag} 模型陷入地面 {minY:F2}");
            }

            // ---- 10.7 挂点检查 ----
            var need = new List<string> { "torso", "head" };
            if (def.worker) need.Add("weaponMain(tool)");
            switch (def.kind)
            {
                case UnitKind.Infantry: need.AddRange(new[] { "armL", "armR", "weaponMain", "weaponOff" }); break;
                case UnitKind.Ranged:   need.AddRange(new[] { "armL", "armR", "weaponMain", "projectileOrigin" }); break;
                case UnitKind.Cavalry:  need.AddRange(new[] { "armR", "horseLegs", "weaponMain" }); break;
            }
            if (def.hero)
                need.AddRange(new[] { "projectileOrigin", "auraOrigin", "cape/banner" });
            if (def.id == "deathranger") need.Add("soulCore");

            bool Has(string n)
            {
                switch (n)
                {
                    case "torso": return rig.torso != null;
                    case "head": return rig.head != null;
                    case "armL": return rig.armL != null;
                    case "armR": return rig.armR != null;
                    case "horseLegs": return rig.horseLegs != null && rig.horseLegs.Length == 4;
                    case "weaponMain(tool)":
                    case "weaponMain": return rig.weaponMain != null;
                    case "weaponOff": return rig.weaponOff != null;
                    case "projectileOrigin": return rig.projectileOrigin != null;
                    case "auraOrigin": return rig.auraOrigin != null;
                    case "cape/banner": return rig.capePieces != null || rig.banner != null;
                    case "soulCore": return rig.soulCore != null;
                }
                return false;
            }
            foreach (var n in need)
                if (!Has(n)) errors.Add($"[ERROR] {tag} 缺少挂点 {n}");

            // 英雄识别部件（10.5）
            if (def.hero)
            {
                int heroParts = 0;
                if (def.id == "deathranger")
                {
                    if (rig.head != null) heroParts++;                       // 细长头骨
                    if (rig.soulCore != null) heroParts++;
                    if (rig.staff != null || rig.weaponMain != null) heroParts++;
                    if (rig.floatingParts != null && rig.floatingParts.Length > 0) heroParts++;
                    if (heroParts < 3) errors.Add($"[ERROR] 巫妖 {tag} 识别部件不足 3 类（头骨/魂核/法器/悬浮碎片 实际 {heroParts}）");
                }
                else
                {
                    if (rig.weaponMain != null) heroParts++;
                    if (rig.banner != null || rig.capePieces != null) heroParts++;
                    if (rig.auraOrigin != null) heroParts++;
                    if (rig.head != null) heroParts++;
                    if (heroParts < 3) errors.Add($"[ERROR] 人族英雄 {tag} 识别部件不足 3 类");
                }
            }

            // ---- 10.8 性能预算 ----
            int budget = def.hero ? ErrBudgetHero : def.kind == UnitKind.Cavalry ? ErrBudgetCavalry : ErrBudgetNormal;
            if (renderers > budget)
                warnings.Add($"[WARNING] {tag} Renderer {renderers} 超过预算 {budget}");
            int emBudget = def.hero ? 10 : 3;
            if (emissiveNodes > emBudget)
                warnings.Add($"[WARNING] {tag} 自发光节点 {emissiveNodes} 超过预算 {emBudget}");

            bool pass = errors.Count == errsBefore;
            matrix.AppendLine($"{def.name},{(team == Team.Player ? "human" : "undead")},{def.kind}," +
                              $"{(def.hero ? "YES" : "NO")},{renderers},{budget},{renderers},{emissiveNodes}," +
                              $"{need.Count},{(boundsInit ? "OK" : "FAIL")},{(pass ? "PASS" : "FAIL")}");
            infos.Add($"[INFO] {def.name}: Renderer={renderers} 发光={emissiveNodes} 材质={sharedMats.Count}(共享累计)");
        }

        static bool IsFinite(Vector3 v) => !float.IsNaN(v.x + v.y + v.z) && !float.IsInfinity(v.x + v.y + v.z);
        static bool IsFinite(Quaternion q) => !float.IsNaN(q.x + q.y + q.z + q.w) && !float.IsInfinity(q.x + q.y + q.z + q.w);
    }

    /// <summary>视觉评审展示工具（任务书命令 H）：生成全部单位的临时预览阵列，供编辑器内
    /// 近景/远景/正背侧面人工复核。不混入战斗代码，预览对象一键清除。</summary>
    public class VisualModelPreviewWindow : EditorWindow
    {
        static readonly (UnitDef def, Team team)[] Units =
        {
            (GameConfig.Farmer, Team.Player), (GameConfig.Footman, Team.Player),
            (GameConfig.Archer, Team.Player), (GameConfig.Knight, Team.Player),
            (GameConfig.LordKnight, Team.Player),
            (GameConfig.Acolyte, Team.Enemy), (GameConfig.Skeleton, Team.Enemy),
            (GameConfig.DarkArcher, Team.Enemy), (GameConfig.DeathKnight, Team.Enemy),
            (GameConfig.DeathRanger, Team.Enemy),
        };

        [MenuItem("工具/Eresoth RTS/Visual Model Preview")]
        static void Open() => GetWindow<VisualModelPreviewWindow>("单位视觉预览").Show();

        void OnGUI()
        {
            GUILayout.Label("生成临时预览（出现在场景原点前方，选中项目可复查层级）", EditorStyles.wordWrappedLabel);
            if (GUILayout.Button("生成全部单位预览阵列"))
            {
                Clear();
                for (int i = 0; i < Units.Length; i++)
                {
                    var (def, team) = Units[i];
                    var root = new GameObject("Preview_" + def.id);
                    root.transform.position = new Vector3((i % 5) * 3f, 0, (i / 5) * 4f);
                    Models.BuildUnit(root.transform, team, def);
                }
                if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.LookAt(new Vector3(6, 0, 2), Quaternion.Euler(25, 0, 0));
            }
            if (GUILayout.Button("仅英雄（近景复核）"))
            {
                Clear();
                for (int i = 0; i < 2; i++)
                {
                    var (def, team) = Units[i == 0 ? 4 : 9];
                    var root = new GameObject("Preview_" + def.id);
                    root.transform.position = new Vector3(i * 4f, 0, 0);
                    Models.BuildUnit(root.transform, team, def);
                }
            }
            if (GUILayout.Button("清除全部预览")) Clear();
        }

        static void Clear()
        {
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name.StartsWith("Preview_")) DestroyImmediate(t.gameObject);
        }
    }
}
