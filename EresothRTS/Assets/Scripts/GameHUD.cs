using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Eresoth
{
    /// <summary>HUD：资源栏 / 生产与科技面板 / 血条 / 胜负结算。全部 OnGUI 实现，零 UI 资源。
    /// 面板按 BuildingDef.train / BuildingDef.techs 数据驱动生成。</summary>
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

            // 开局设置面板：确认后才生成世界
            if (!g.started) { DrawIntro(); return; }

            // 顶部资源栏（含科技等级、本局种子与操控阵营）
            int pi = (int)g.playerTeam;
            string faction = g.playerTeam == Team.Player ? "人类" : "不死族";
            GUI.Box(new Rect(0, 0, Screen.width, 26), GUIContent.none);
            GUI.Label(new Rect(12, 3, 760, 22),
                $"【{faction}】魔法矿 {g.mana[pi]}    木头 {g.wood[pi]}    人口 {g.PopCount(pi)}/{g.PopCap(g.playerTeam)}" +
                $"    攻+{g.atkLevel[pi] * 15}%  防+{g.defLevel[pi] * 15}%    种子 {g.seed}", mid);
            GUI.Label(new Rect(Screen.width - 430, 3, 420, 22),
                "左键框选 | 右键移动/攻击/采集 | WASD滚屏 | 滚轮缩放", mid);

            // 提示消息
            if (g.ToastVisible)
            {
                GUI.color = new Color(1f, .6f, .4f);
                GUI.Label(new Rect(Screen.width / 2f - 150, 40, 300, 26), g.toast, mid);
                GUI.color = Color.white;
            }

            // 建造放置模式提示
            if (sel.placing != null)
            {
                GUI.color = new Color(.4f, 1f, .6f);
                GUI.Label(new Rect(Screen.width / 2f - 250, 66, 500, 26),
                    $"放置 {sel.placing.name}：左键确认 | 右键 / Esc 取消（绿=可建 红=不可建）", mid);
                GUI.color = Color.white;
            }

            // 底部操作面板
            GUI.Box(new Rect(0, Screen.height - 120, Screen.width, 120), GUIContent.none);
            DrawBottom();

            DrawHealthBars();
            if (g.over) DrawEnd();
        }

        void DrawIntro()
        {
            var g = Game.I;

            GUI.color = new Color(0, 0, 0, 0.78f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float w = 520, h = 420;
            var r = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(r, GUIContent.none);
            GUI.Label(new Rect(r.x, r.y + 16, r.width, 40), "厄瑞索斯 RTS", big);

            // 阵营选择：人类 / 不死族（另一方由 AI 操控）
            Btn(new Rect(r.x + 50, r.y + 72, 110, 32),
                MapSettings.playerTeam == Team.Player ? "人类 ●" : "人类",
                () => MapSettings.playerTeam = Team.Player, true);
            Btn(new Rect(r.x + 170, r.y + 72, 110, 32),
                MapSettings.playerTeam == Team.Enemy ? "不死族 ●" : "不死族",
                () => MapSettings.playerTeam = Team.Enemy, true);
            GUI.Label(new Rect(r.x + 295, r.y + 76, 180, 24), "选择你的阵营", mid);

            // 地图随机：开 = 每局随机种子；关 = 固定种子（布局可复现，便于调试）
            Btn(new Rect(r.x + 50, r.y + 118, 170, 32),
                $"地图随机：{(MapSettings.randomMap ? "开" : "关")}",
                () => MapSettings.randomMap = !MapSettings.randomMap, true);
            GUI.Label(new Rect(r.x + 235, r.y + 122, 220, 24),
                MapSettings.randomMap ? "每局不同布局" : "固定种子可复现", mid);

            // 地图大小
            Btn(new Rect(r.x + 50, r.y + 164, 170, 32),
                $"地图：{MapSettings.MapSizeNames[MapSettings.mapSizeIndex]}",
                () => MapSettings.mapSizeIndex = (MapSettings.mapSizeIndex + 1) % MapSettings.MapSizeNames.Length, true);
            GUI.Label(new Rect(r.x + 235, r.y + 168, 220, 24),
                $"地图边长 {MapSettings.WorldSize:0}", mid);

            // 资源丰富度：影响树木与魔法矿数量
            Btn(new Rect(r.x + 50, r.y + 210, 170, 32),
                $"资源：{MapSettings.RichnessNames[MapSettings.richnessIndex]}",
                () => MapSettings.richnessIndex = (MapSettings.richnessIndex + 1) % 3, true);
            GUI.Label(new Rect(r.x + 235, r.y + 214, 220, 24),
                $"魔法矿/树木 ×{MapSettings.Richness}", mid);

            // 胜利条件
            Btn(new Rect(r.x + 50, r.y + 256, 170, 32),
                $"胜利：{MapSettings.VictoryNames[(int)MapSettings.victoryMode]}",
                () => MapSettings.victoryMode = MapSettings.victoryMode == VictoryMode.MainBase
                    ? VictoryMode.AllBuildings : VictoryMode.MainBase, true);
            GUI.Label(new Rect(r.x + 235, r.y + 260, 220, 24), "选择本局结束条件", mid);

            Btn(new Rect(r.x + 50, r.y + 302, 170, 32),
                $"难度：{MapSettings.DifficultyNames[(int)MapSettings.difficulty]}",
                () => MapSettings.difficulty = (Difficulty)(((int)MapSettings.difficulty + 1) % MapSettings.DifficultyNames.Length), true);
            GUI.Label(new Rect(r.x + 235, r.y + 306, 240, 24),
                MapSettings.difficulty == Difficulty.Hard ? "AI 经济加成 +35%" : "AI 标准经济与压力", mid);

            Btn(new Rect(r.x + 140, r.y + 360, 200, 42), "开 始 游 戏", () => g.StartGame(), true);
        }

        void DrawBottom()
        {
            float y = Screen.height - 106;
            var g = Game.I;

            if (sel.selBuilding != null)
            {
                var b = sel.selBuilding;
                GUI.Label(new Rect(16, y, 500, 24), $"{b.DisplayName}  HP {b.hp:0}/{b.def.hp:0}", mid);

                if (b.constructing)
                {
                    GUI.color = new Color(.6f, .9f, 1f);
                    GUI.Label(new Rect(16, y + 30, 520, 30),
                        $"施工中 {b.constructionProgress * 100f:0}%  | 工人会自动参与建造 | 施工中受到双倍伤害", mid);
                    GUI.color = Color.white;
                    return;
                }

                if (b.kind == "hall")
                {
                    // 训练工人 + 建造列表（点击后进入自由选址放置模式；由工人施工）
                    int bi = (int)b.team;
                    var workerDef = b.team == Team.Player ? GameConfig.Farmer : GameConfig.Acolyte;
                    string workerCost = workerDef.mana > 0 ? $"{workerDef.wood}木+{workerDef.mana}矿" : $"{workerDef.wood}木";
                    Btn(new Rect(16, y + 30, 160, 30), $"训练{workerDef.name} ({workerCost})",
                        () => b.TryTrain(workerDef), g.wood[bi] >= workerDef.wood && g.mana[bi] >= workerDef.mana);

                    var list = b.team == Team.Player ? GameConfig.HumanBuildings : GameConfig.UndeadBuildings;
                    for (int i = 0; i < list.Length; i++)
                    {
                        var bd = list[i];
                        int n = g.buildings.FindAll(x => x.team == b.team && x.kind == bd.kind).Count;
                        bool multi = bd.kind == "tower" || bd.kind == "house";   // 箭塔/民居可建多座
                        bool built = !multi && n > 0;
                        string cost = bd.mana > 0 ? $"{bd.wood}木+{bd.mana}矿" : $"{bd.wood}木";
                        bool afford = g.wood[bi] >= bd.wood && g.mana[bi] >= bd.mana;
                        string label = bd.kind == "tower" ? $"建造{bd.name}({n}/{Game.TowerCap})"
                            : multi ? $"建造{bd.name}({n})"
                            : built ? $"{bd.name}已建成" : $"建造{bd.name}({cost})";
                        bool canDo = bd.kind == "tower" ? n < Game.TowerCap && afford : multi ? afford : !built && afford;
                        var def = bd;
                        Btn(new Rect(190 + i * 145, y + 30, 140, 30), label,
                            () => sel.BeginPlacement(def), canDo);
                    }
                }
                else
                {
                    // 训练按钮（BuildingDef.train）；英雄全场唯一，已存在则置灰
                    int bi = (int)b.team;
                    var defs = b.def.train ?? System.Array.Empty<UnitDef>();
                    for (int i = 0; i < defs.Length; i++)
                    {
                        var d = defs[i];
                        string cost = d.mana > 0 ? $"{d.wood}木+{d.mana}矿" : $"{d.wood}木";
                        bool heroAlive = d.hero && g.units.Exists(u => u.team == b.team && u.def.hero);
                        string label = d.hero ? $"{d.name}★ ({cost})" : $"{d.name} ({cost})";
                        var unitDef = d;
                        Btn(new Rect(16 + i * 180, y + 30, 170, 30), label,
                            () => b.TryTrain(unitDef),
                            !heroAlive && g.wood[bi] >= d.wood && g.mana[bi] >= d.mana);
                    }

                    // 研究按钮（BuildingDef.techs）：研究中显示进度，满级置灰
                    var techs = b.def.techs;
                    if (techs != null)
                    {
                        for (int i = 0; i < techs.Length; i++)
                        {
                            var td = GameConfig.Techs[techs[i]];
                            int lvl = g.TechLevel(b.team, td.effect);
                            var r = new Rect(16 + i * 180, y + 66, 170, 30);
                            if (b.research == td.id)
                            {
                                GUI.Label(r, $"研究中… {td.name} {(int)(b.researchTimer / td.time * 100)}%", mid);
                            }
                            else if (lvl >= td.maxLevel)
                            {
                                GUI.Label(r, $"{td.name} 已满级", mid);
                            }
                            else
                            {
                                int mult = lvl + 1;
                                string cost = td.mana > 0 ? $"{td.wood * mult}木+{td.mana * mult}矿" : $"{td.wood * mult}木";
                                var techDef = td;
                                Btn(r, $"研究{td.name}Lv{lvl + 1}({cost})", () => b.TryResearch(techDef.id),
                                    g.wood[(int)b.team] >= td.wood * mult && g.mana[(int)b.team] >= td.mana * mult);
                            }
                        }
                    }
                }

                if (b.queue.Count > 0)
                    GUI.Label(new Rect(600, y + 66, 600, 24),
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
                    "右键点地=移动 | 点敌人=攻击 | 农夫点树/水晶=采集 | 选中建筑右键资源=派工人采集/右键地面=设集结点 | Ctrl+数字=编队", mid);
            }
            else
            {
                GUI.Label(new Rect(16, y, 1100, 24),
                    $"目标：{MapSettings.VictoryNames[(int)MapSettings.victoryMode]}！魔法矿是主要资源；资源收集站可作为远端交付点，建筑由工人施工。", mid);
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
                Bar(u.transform.position, 34 * u.def.size, u.Hp01, u.team == g.playerTeam, u.def.size * 2.6f);
            foreach (var b in g.buildings)
                Bar(b.transform.position, 60, b.Hp01, b.team == g.playerTeam, b.kind == "hall" ? 6f : 4f);
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
                g.winner == 0 ? $"胜利！{MapSettings.VictoryNames[(int)MapSettings.victoryMode]}" : "战败……你的建筑已全部失守", big);
            GUI.color = Color.white;

            if (GUI.Button(new Rect(r.x + 175, r.y + 105, 150, 40), "再来一局"))
            {
                string scene = SceneManager.GetActiveScene().name;
                if (!string.IsNullOrEmpty(scene)) SceneManager.LoadScene(scene);
            }
        }
    }
}
