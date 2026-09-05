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

        Vector3 downPos;
        Camera cam;

        void Start() { cam = Camera.main; }

        void Update()
        {
            if (!Game.I.started || Game.I.over) return;
            if (cam == null) cam = Camera.main;   // 世界在开局确认后才生成，相机随之出现
            selected.RemoveAll(u => u == null);

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
            if (!Raycast(out var hit)) { ClearAll(); return; }
            var u = hit.collider.GetComponentInParent<Unit>();
            if (u != null && u.team == Team.Player) { SetSelected(new List<Unit> { u }); return; }
            var b = hit.collider.GetComponentInParent<Building>();
            if (b != null && b.team == Team.Player) { SelectBuilding(b); return; }
            ClearAll();
        }

        void BoxSelect()
        {
            var rect = ScreenRect(downPos, Input.mousePosition);
            var list = new List<Unit>();
            foreach (var u in Game.I.units)
            {
                if (u.team != Team.Player) continue;
                var sp = cam.WorldToScreenPoint(u.transform.position);
                if (sp.z > 0 && rect.Contains(new Vector2(sp.x, sp.y))) list.Add(u);
            }
            if (list.Count > 0) SetSelected(list); else ClearAll();
        }

        void Command()
        {
            if (!Raycast(out var hit)) return;

            // 选中建筑时：右键设置集结点
            if (selBuilding != null)
            {
                if (hit.collider.GetComponentInParent<Building>() != selBuilding)
                    selBuilding.rally = hit.point;
                return;
            }
            if (selected.Count == 0) return;

            var eu = hit.collider.GetComponentInParent<Unit>();
            if (eu != null && eu.team == Team.Enemy)
            { foreach (var u in selected) u.CommandAttack(eu); return; }

            var eb = hit.collider.GetComponentInParent<Building>();
            if (eb != null && eb.team == Team.Enemy)
            { foreach (var u in selected) u.CommandAttack(eb); return; }

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

        // ---------- 选择状态 ----------

        void SetSelected(List<Unit> list)
        {
            ClearRings();
            selected.Clear(); selected.AddRange(list);
            selBuilding = null;
            foreach (var u in selected) u.ring.SetActive(true);
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
            foreach (var u in selected) if (u != null) u.ring.SetActive(false);
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
