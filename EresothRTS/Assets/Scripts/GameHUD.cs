using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Eresoth
{
    /// <summary>HUD：资源栏 / 生产面板 / 血条 / 胜负结算。全部 OnGUI 实现，零 UI 资源。</summary>
    public class GameHUD : MonoBehaviour
    {
        SelectionManager sel;
        GUIStyle mid, big;

        void Start() { sel = GetComponent<SelectionManager>(); }

        void EnsureStyles()
        {
            if (mid != null) return;
            mid = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            mid.normal.textColor = Color.white;
            big = new GUIStyle(GUI.skin.label) { fontSize = 28, alignment = TextAnchor.MiddleCenter };
            big.normal.textColor = Color.white;
        }

        void OnGUI()
        {
            var g = Game.I;
            EnsureStyles();

            // 顶部资源栏
            GUI.Box(new Rect(0, 0, Screen.width, 26), GUIContent.none);
            GUI.Label(new Rect(12, 3, 400, 22),
                $"木头 {g.wood[0]}    魔法矿 {g.mana[0]}    人口 {g.PopCount(0)}/{GameConfig.PopCap}", mid);
            GUI.Label(new Rect(Screen.width - 430, 3, 420, 22),
                "左键框选 | 右键移动/攻击/采集 | WASD滚屏 | 滚轮缩放", mid);

            // 提示消息
            if (g.ToastVisible)
            {
                GUI.color = new Color(1f, .6f, .4f);
                GUI.Label(new Rect(Screen.width / 2f - 150, 40, 300, 26), g.toast, mid);
                GUI.color = Color.white;
            }

            // 底部操作面板
            GUI.Box(new Rect(0, Screen.height - 120, Screen.width, 120), GUIContent.none);
            DrawBottom();

            DrawHealthBars();
            if (g.over) DrawEnd();
        }

        void DrawBottom()
        {
            float y = Screen.height - 106;
            var g = Game.I;

            if (sel.selBuilding != null)
            {
                var b = sel.selBuilding;
                GUI.Label(new Rect(16, y, 500, 24), $"{b.DisplayName}  HP {b.hp:0}/{b.maxHp:0}", mid);

                if (b.kind == "hall")
                {
                    var workerDef = b.team == Team.Player ? GameConfig.Farmer : GameConfig.Acolyte;
                    Btn(new Rect(16, y + 30, 160, 30), $"训练{workerDef.name} (50木)",
                        () => b.TryTrain(workerDef), g.wood[0] >= 50);
                    bool hasBar = g.Barracks(Team.Player) != null;
                    Btn(new Rect(16, y + 66, 160, 30), hasBar ? "兵营已建成" : $"建造兵营 ({GameConfig.BarracksWood}木)",
                        () => g.BuildBarracksPlayer(), !hasBar && g.wood[0] >= GameConfig.BarracksWood);
                }
                else
                {
                    var defs = b.team == Team.Player ? GameConfig.HumanTrain : GameConfig.UndeadTrain;
                    for (int i = 0; i < defs.Length; i++)
                    {
                        var d = defs[i];
                        string cost = d.mana > 0 ? $"{d.wood}木+{d.mana}矿" : $"{d.wood}木";
                        Btn(new Rect(16 + i * 180, y + 30, 170, 30), $"{d.name} ({cost})",
                            () => b.TryTrain(d), g.wood[0] >= d.wood && g.mana[0] >= d.mana);
                    }
                }
                if (b.queue.Count > 0)
                    GUI.Label(new Rect(200, y + 66, 600, 24),
                        "队列：" + string.Join("、", b.queue.ConvertAll(q => q.name)), mid);
            }
            else if (sel.selected.Count > 0)
            {
                var cnt = new Dictionary<string, int>();
                foreach (var u in sel.selected)
                    cnt[u.def.name] = cnt.TryGetValue(u.def.name, out var c) ? c + 1 : 1;
                var sb = new StringBuilder();
                foreach (var kv in cnt) sb.Append($"{kv.Key}×{kv.Value}  ");
                GUI.Label(new Rect(16, y, 900, 24), $"已选 {sel.selected.Count} 单位：{sb}", mid);
                GUI.Label(new Rect(16, y + 30, 900, 24),
                    "右键点地=移动 | 点敌人=攻击 | 农夫点树/水晶=采集", mid);
            }
            else
            {
                GUI.Label(new Rect(16, y, 900, 24),
                    "目标：摧毁右上角不死族主基地！点主基地训练农夫、造兵营，框选部队右键进攻。", mid);
            }
        }

        void Btn(Rect r, string label, System.Action act, bool enabled)
        {
            GUI.enabled = enabled;
            if (GUI.Button(r, label)) act();
            GUI.enabled = true;
        }

        void DrawHealthBars()
        {
            var g = Game.I;
            foreach (var u in g.units)
                Bar(u.transform.position, 34 * u.def.size, u.Hp01, u.team == Team.Player, u.def.size * 2.6f);
            foreach (var b in g.buildings)
                Bar(b.transform.position, 60, b.Hp01, b.team == Team.Player, b.kind == "hall" ? 6f : 4f);
        }

        void Bar(Vector3 world, float w, float h01, bool friendly, float yOff)
        {
            var cam = Camera.main; if (cam == null) return;
            var sp = cam.WorldToScreenPoint(world + Vector3.up * yOff);
            if (sp.z < 0) return;
            float x = sp.x - w / 2, y = Screen.height - sp.y;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(x - 1, y - 1, w + 2, 6), Texture2D.whiteTexture);
            GUI.color = friendly ? new Color(.3f, 1f, .4f) : new Color(1f, .3f, .3f);
            GUI.DrawTexture(new Rect(x, y, w * Mathf.Clamp01(h01), 4), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        void DrawEnd()
        {
            var g = Game.I;
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            var r = new Rect(Screen.width / 2f - 250, Screen.height / 2f - 90, 500, 180);
            GUI.Box(r, GUIContent.none);
            GUI.color = g.winner == 0 ? new Color(.5f, 1f, .6f) : new Color(1f, .5f, .5f);
            GUI.Label(new Rect(r.x, r.y + 30, r.width, 40),
                g.winner == 0 ? "胜利！敌方主基地已被摧毁" : "战败……你的主基地陷落了", big);
            GUI.color = Color.white;

            if (GUI.Button(new Rect(r.x + 175, r.y + 105, 150, 40), "再来一局"))
            {
                string scene = SceneManager.GetActiveScene().name;
                if (!string.IsNullOrEmpty(scene)) SceneManager.LoadScene(scene);
            }
        }
    }
}
