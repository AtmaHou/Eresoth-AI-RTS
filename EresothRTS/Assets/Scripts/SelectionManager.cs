using System.Collections.Generic;
using UnityEngine;

namespace Eresoth
{
    /// <summary>选择与指挥：左键点选/框选，右键移动/攻击/采集，选中建筑时右键设集结点。
    /// 所有命令最终落到 Unit.CommandMove / CommandAttack —— 与未来 LLM 军令共用同一入口。</summary>
    public class SelectionManager : MonoBehaviour
    {
        public readonly List<Unit> selected = new();
        public Building selBuilding;
        public bool dragging;

        public BuildingDef placing;     // 建造放置模式（非 null 时左键选点）
        GameObject ghost;               // 放置预览
        readonly Dictionary<int, List<Unit>> groups = new();  // Ctrl+数字 编队

        Vector3 downPos;
        Camera cam;
        Unit lastClick;        // 双击检测：上次点选的单位
        float lastClickTime;

        void Start() { cam = Camera.main; }

        void Update()
        {
            if (!Game.I.started || Game.I.over) return;
            if (cam == null) cam = Camera.main;   // 世界在开局确认后才生成，相机随之出现
            selected.RemoveAll(u => u == null);

            // 建造放置模式：拦截一切选择/指挥输入
            if (placing != null) { PlacementStep(); return; }

            // 编队：Ctrl+数字 设置（编辑器中 Ctrl+1~5 被 Unity 窗口快捷键占用，可用 Alt+数字代替），数字 召回
            for (int i = 0; i <= 9; i++)
            {
                if (!Input.GetKeyDown(KeyCode.Alpha0 + i)) continue;
                bool assign = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                           || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                if (assign)
                {
                    groups[i] = new List<Unit>(selected);
                    if (selected.Count > 0) Game.I.Toast($"编队 {i}：{selected.Count} 单位");
                }
                else if (groups.TryGetValue(i, out var list))
                {
                    list.RemoveAll(u => u == null);
                    if (list.Count > 0) SetSelected(list);
                }
            }

            if (Input.GetMouseButtonDown(0)) { downPos = Input.mousePosition; dragging = false; }
            if (Input.GetMouseButton(0) && (Input.mousePosition - downPos).magnitude > 10f) dragging = true;
            if (Input.GetMouseButtonUp(0))
            {
                bool onHud = Input.mousePosition.y < 120; // 底部 HUD 区域不响应框选
                if (!onHud)
                {
                    if (dragging) BoxSelect(); else ClickSelect();
                }
                dragging = false;
            }
            if (Input.GetMouseButtonDown(1) && Input.mousePosition.y > 120) Command();
        }

        void ClickSelect()
        {
            if (!Raycast(out var hit)) { ClearAll(); lastClick = null; return; }
            var u = hit.collider.GetComponentInParent<Unit>();
            if (u != null && u.team == Game.I.playerTeam)
            {
                // 双击（0.3s 内连点同一单位）：选中屏幕内所有同类型单位
                if (u == lastClick && Time.time - lastClickTime < 0.3f)
                {
                    lastClick = null;
                    var same = new List<Unit>();
                    foreach (var x in Game.I.units)
                    {
                        if (x.team != Game.I.playerTeam || x.def != u.def) continue;
                        var sp = cam.WorldToScreenPoint(x.transform.position);
                        if (sp.z > 0 && sp.x >= 0 && sp.x <= Screen.width && sp.y >= 0 && sp.y <= Screen.height)
                            same.Add(x);
                    }
                    SetSelected(same);
                    return;
                }
                lastClick = u; lastClickTime = Time.time;
                SetSelected(new List<Unit> { u });
                return;
            }
            lastClick = null;
            var b = hit.collider.GetComponentInParent<Building>();
            if (b != null && b.team == Game.I.playerTeam) { SelectBuilding(b); return; }
            ClearAll();
        }

        void BoxSelect()
        {
            lastClick = null;
            var rect = ScreenRect(downPos, Input.mousePosition);
            var list = new List<Unit>();
            foreach (var u in Game.I.units)
            {
                if (u.team != Game.I.playerTeam) continue;
                var sp = cam.WorldToScreenPoint(u.transform.position);
                if (sp.z > 0 && rect.Contains(new Vector2(sp.x, sp.y))) list.Add(u);
            }
            if (list.Count > 0) SetSelected(list); else ClearAll();
        }

        void Command()
        {
            lastClick = null;
            if (!Raycast(out var hit)) return;

            // 选中建筑时：右键资源点=派空闲工人去采集（集结点同步设到资源处，新工人出厂即上工）；右键地面=设集结点
            if (selBuilding != null)
            {
                var resNode = hit.collider.GetComponentInParent<ResourceNode>();
                if (resNode != null)
                {
                    selBuilding.rally = resNode.transform.position;
                    int sent = 0;
                    foreach (var u in Game.I.units)
                    {
                        if (u.team != Game.I.playerTeam || !u.def.worker) continue;
                        var w = u.GetComponent<Worker>();
                        if (w != null && w.state == Worker.State.Idle) { w.GatherAt(resNode); sent++; }
                    }
                    Game.I.Toast(sent > 0 ? $"已派 {sent} 个空闲工人去采集" : "没有空闲工人（集结点已设到资源处）");
                    return;
                }
                if (hit.collider.GetComponentInParent<Building>() != selBuilding)
                    selBuilding.rally = hit.point;
                return;
            }
            if (selected.Count == 0) return;

            var eu = hit.collider.GetComponentInParent<Unit>();
            if (eu != null && eu.team != Game.I.playerTeam)
            { foreach (var u in selected) u.CommandAttack(eu); return; }

            var eb = hit.collider.GetComponentInParent<Building>();
            if (eb != null && eb.team != Game.I.playerTeam)
            { foreach (var u in selected) u.CommandAttack(eb); return; }

            // 右键己方未完工建筑：让选中的工人参与建造
            if (eb != null && eb.team == Game.I.playerTeam && eb.constructing)
            {
                int sent = 0;
                foreach (var u in selected)
                {
                    var w = u.GetComponent<Worker>();
                    if (w != null) { w.BuildAt(eb); sent++; }
                    else u.CommandMove(hit.point);
                }
                if (sent > 0) Game.I.Toast($"已派 {sent} 个工人前去建造");
                return;
            }

            var node = hit.collider.GetComponentInParent<ResourceNode>();
            if (node != null)
            {
                foreach (var u in selected)
                {
                    var w = u.GetComponent<Worker>();
                    if (w != null) w.GatherAt(node); else u.CommandMove(node.transform.position);
                }
                return;
            }

            // 地面：方阵偏移移动
            int cols = Mathf.CeilToInt(Mathf.Sqrt(selected.Count));
            for (int i = 0; i < selected.Count; i++)
            {
                var off = new Vector3((i % cols - cols / 2) * 1.8f, 0, (i / cols - cols / 2) * 1.8f);
                selected[i].CommandMove(hit.point + off);
            }
        }

        bool Raycast(out RaycastHit hit)
        {
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            return Physics.Raycast(ray, out hit, 500f);
        }

        // ---------- 建造放置模式 ----------

        /// <summary>进入放置模式：显示预览体，左键确认、右键/Esc 取消。</summary>
        public void BeginPlacement(BuildingDef def)
        {
            CancelPlacement();
            placing = def;
            ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.Destroy(ghost.GetComponent<Collider>());
            ghost.name = "放置预览-" + def.name;
        }

        public void CancelPlacement()
        {
            if (ghost != null) Destroy(ghost);
            ghost = null; placing = null;
        }

        void PlacementStep()
        {
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit, 500f)) return;
            Vector3 p = hit.point;
            p.y = Game.TerrainHeight(p.x, p.z);

            bool valid = Game.I.CanPlaceAt(Game.I.playerTeam, placing, p, out _);
            ghost.transform.position = p + Vector3.up * placing.size * 0.4f;
            ghost.transform.localScale = new Vector3(placing.size, placing.size * 0.8f, placing.size);
            ghost.GetComponent<Renderer>().sharedMaterial =
                Gfx.Mat(valid ? new Color(.2f, 1f, .4f) : new Color(1f, .3f, .3f));

            if (Input.GetKeyDown(KeyCode.Escape)
                || Input.GetMouseButtonDown(1)) CancelPlacement();
            else if (valid && Input.GetMouseButtonDown(0) && Input.mousePosition.y > 120)
            {
                if (Game.I.BuildAt(Game.I.playerTeam, placing, p)) CancelPlacement();
            }
        }

        // ---------- 选择状态 ----------

        void SetSelected(List<Unit> list)
        {
            ClearRings();
            selected.Clear(); selected.AddRange(list);
            selBuilding = null;
            foreach (var u in selected) u.ring.gameObject.SetActive(true);
        }

        void SelectBuilding(Building b)
        {
            ClearRings(); selected.Clear();
            selBuilding = b;
        }

        public void ClearAll()
        {
            ClearRings(); selected.Clear(); selBuilding = null;
        }

        void ClearRings()
        {
            foreach (var u in selected) if (u != null) u.ring.gameObject.SetActive(false);
        }

        // ---------- 框选框绘制 ----------

        static Rect ScreenRect(Vector3 a, Vector3 b)
        {
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                                   Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        void OnGUI()
        {
            if (!dragging) return;
            var r = ScreenRect(downPos, Input.mousePosition);
            var gui = new Rect(r.x, Screen.height - r.yMax, r.width, r.height);
            GUI.color = new Color(0.2f, 1f, 0.4f, 0.25f);
            GUI.DrawTexture(gui, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
