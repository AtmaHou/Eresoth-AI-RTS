using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Eresoth
{
    /// <summary>指挥台：右侧可收放文字指挥栏（OnGUI，零资源）。
    /// 回车发送 → LLM 解析（失败走本地兜底）→ 结构化军令执行 → 参谋回复。
    /// 内置高频命令 tips（点击填入输入框）与滚动建议展播；收拢后只留右侧把手，不挡世界操作。
    /// 全部调用落盘 command_log.jsonl（将来评测/蒸馏的数据源）。LLM 配置在开局菜单填写。</summary>
    public class CommandConsole : MonoBehaviour
    {
        public static CommandConsole I;

        /// <summary>输入框聚焦中：镜头与选择快捷键静默。</summary>
        public static bool TypingActive { get; private set; }
        /// <summary>鼠标悬停在展开的指挥栏上：点击不穿透到世界。</summary>
        public static bool PointerOver { get; private set; }

        struct ChatMsg { public string who; public string text; public string raw; public bool rawOpen; public float time; }

        readonly List<ChatMsg> msgs = new();
        string input = "";
        bool waiting;
        bool focusInput;
        bool collapsed;             // 收拢为右侧把手（世界操作不被遮挡）
        GUIStyle style;
        GUIStyle rawStyle;
        Vector2 scroll;
        string logPath;
        readonly Queue<string> recentTurns = new();   // 多轮上下文（最近 4 轮）

        /// <summary>滚动建议池：环形展播可尝试的命令（覆盖军事/经济/编制/侦察/队列）。</summary>
        static readonly string[] Suggestions =
        {
            "一军团进攻敌方主基地，遇到主力就撤",
            "二军团去东矿，伤亡过半就撤退",
            "全军集火敌方英雄",
            "派个骑兵去侦查一圈",
            "所有的骑兵编入二队",
            "一军团一半的兵编入二队",
            "造4个弓箭手再研究攻击科技",
            "依次造2个兵营和3个箭塔",
            "拉两个农夫去开个分矿",
            "所有农民按比分配采资源",
            "工人去采魔法矿",
            "全军撤退",
        };

        void OnEnable()
        {
            I = this;
            logPath = Path.Combine(Application.dataPath, "../command_log.jsonl");
        }
        void OnDestroy() { if (I == this) I = null; }

        /// <summary>发送入口（UI 与将来语音共用）。</summary>
        public void Send(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || waiting) return;
            if (!Game.I.started || Game.I.over) return;
            Add("你", text);
            waiting = true;

            string digest = StateDigestBuilder.Build(Game.I.playerTeam);
            string history = string.Join("\n", recentTurns);

            if (LlmClient.I != null && LlmClient.I.Available)
            {
                LlmClient.I.Parse(text, digest, history,
                    onJson: json => HandleLlmJson(text, digest, json),
                    onError: (err, raw) => HandleFallback(text, digest, err, raw));
            }
            else HandleFallback(text, digest, LlmClient.I != null ? LlmClient.I.UnavailableReason : "LLM 不可用", null);
        }

        void HandleLlmJson(string text, string digest, string json)
        {
            waiting = false;
            json = LlmClient.StripCodeFence(json);   // Kimi 等无 response_format 的模型可能包 ```json 围栏
            DebugCommandRunner.CommandRequest req = null;
            try { req = JsonUtility.FromJson<DebugCommandRunner.CommandRequest>(json); } catch { }

            if (req == null) { HandleFallback(text, digest, "模型输出非法 JSON", json); return; }

            // 追问分支：不执行任何军令
            if (req.clarification_needed && !string.IsNullOrEmpty(req.question))
            {
                Add("参谋", req.question, json);
                Log(text, digest, json, "clarification");
                PushTurn(text, req.question);
                return;
            }

            req.player_text = text;
            int executed = Execute(req, out string execSummary);
            string reply = !string.IsNullOrEmpty(req.player_reply) ? req.player_reply
                : executed > 0 ? "收到，已下达。" : "没有可执行的指令。";
            Add("参谋", reply, json);
            Log(text, digest, json, executed > 0 ? "executed" : "no-op", execSummary);
            PushTurn(text, reply);
        }

        void HandleFallback(string text, string digest, string reason, string raw)
        {
            waiting = false;
            var req = LocalFallbackParser.TryParse(text);
            if (req == null)
            {
                Add("参谋", $"没听懂（{reason}）。试试：\"一军团防守家门口\"、\"二军团去打东矿\"、\"造4个弓箭手\"。", raw);
                Log(text, digest, null, "fallback_failed: " + reason);
                return;
            }
            int executed = Execute(req, out string fbSummary);
            Add("参谋", (req.player_reply ?? "收到。") + "［离线兜底］", raw);
            Log(text, digest, null, $"fallback_executed({executed}): {reason}", fbSummary);
            PushTurn(text, req.player_reply ?? "");
        }

        /// <summary>执行解析结果：军事军令 + 经济计划逐条提交，返回成功条数。"全军"在此展开为每军团一条。
        /// execSummary 记录每条军令的执行/拒绝详情，落盘 command_log.jsonl 便于复盘"AI 到底收到了什么"。</summary>
        int Execute(DebugCommandRunner.CommandRequest req, out string execSummary)
        {
            int ok = 0;
            var sb = new System.Text.StringBuilder();
            if (OrderDispatcher.I == null) { execSummary = "[dispatcher unavailable]"; return 0; }
            foreach (var dto in req.orders)
                foreach (var expanded in DebugCommandRunner.ExpandForces(dto))
                {
                    var o = DebugCommandRunner.Map(expanded, req.player_text, out string err);
                    if (o == null) { Add("系统", $"军令无效：{err}"); sb.Append($"[invalid:{err}]"); continue; }
                    if (OrderDispatcher.I.SubmitOrder(o)) { ok++; sb.Append($"[{o.forceId}:{o.action}]"); }
                    else { Add("系统", $"军令被拒：{o.failReason}"); sb.Append($"[rejected:{o.failReason}]"); }
                }
            if (req.economy != null)
                foreach (var dto in req.economy)
                {
                    var o = DebugCommandRunner.Map(dto, req.player_text, out string err);
                    if (o == null) { Add("系统", $"经济计划无效：{err}"); sb.Append($"[eco invalid:{err}]"); continue; }
                    if (OrderDispatcher.I.SubmitOrder(o)) { ok++; sb.Append($"[eco:{o.action}:{o.targetId}]"); }
                    else { Add("系统", $"计划被拒：{o.failReason}"); sb.Append($"[eco rejected:{o.failReason}]"); }
                }
            execSummary = sb.ToString();
            return ok;
        }

        void PushTurn(string player, string reply)
        {
            recentTurns.Enqueue($"玩家：{player}\n参谋：{reply}");
            while (recentTurns.Count > 4) recentTurns.Dequeue();
        }

        void Add(string who, string text, string raw = null)
        {
            msgs.Add(new ChatMsg { who = who, text = text, raw = raw, time = Time.time });
            if (msgs.Count > 50) msgs.RemoveAt(0);
            scroll.y = float.MaxValue;   // 滚到底
        }

        void Log(string text, string digest, string modelJson, string result, string exec = null)
        {
            try
            {
                // 完整现场：墙上时间 + 发给 LLM 的请求体（含 prompt）+ API 原始响应；均为最近一次调用
                string reqBody = LlmClient.I != null ? LlmClient.I.LastRequestBody : null;
                string respBody = LlmClient.I != null ? LlmClient.I.LastResponseBody : null;
                string line = "{\"wall\":" + J(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                    + ",\"t\":" + Time.time.ToString("0")
                    + ",\"text\":" + J(text) + ",\"digest\":" + (digest ?? "null")
                    + ",\"request\":" + (reqBody != null ? J(Truncate(reqBody, 4000)) : "null")
                    + ",\"response\":" + (respBody != null ? J(Truncate(respBody, 4000)) : "null")
                    + ",\"model\":" + (modelJson != null ? J(Truncate(modelJson, 4000)) : "null")
                    + ",\"result\":" + J(result)
                    + ",\"exec\":" + (exec != null ? J(exec) : "null") + "}";
                File.AppendAllText(logPath, line + "\n");
            }
            catch { /* 日志失败不影响游戏 */ }
        }

        static string Truncate(string s, int max)
            => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max) + "…");

        static string J(string s)
            => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

        /// <summary>单条消息高度（正文 42 + 折叠的原始输出行）。</summary>
        static float MsgHeight(ChatMsg m, float width)
        {
            float h = 42;
            if (!string.IsNullOrEmpty(m.raw))
            {
                h += 17;
                if (m.rawOpen) h += RawHeight(m.raw, width - 10) + 8;
            }
            return h;
        }

        /// <summary>原始输出展示高度：按字符数折行估算，封顶 6 行。</summary>
        static float RawHeight(string raw, float width)
        {
            int charsPerLine = Mathf.Max(20, (int)(width / 6.5f));
            int lines = 0;
            foreach (var seg in raw.Split('\n'))
                lines += Mathf.Max(1, Mathf.CeilToInt(seg.Length / (float)charsPerLine));
            return Mathf.Min(lines, 6) * 14 + 4;
        }

        // ---------------- UI ----------------

        /// <summary>高频命令 tips：结合本局军团名生成，点击填入输入框。</summary>
        List<string> BuildTips()
        {
            string f1 = "一军团", f2 = "二军团";
            if (ForceManager.I != null && Game.I != null)
            {
                var fs = ForceManager.I.OfTeam(Game.I.playerTeam);
                if (fs.Count > 0) f1 = fs[0].name;
                if (fs.Count > 1) f2 = fs[1].name;
            }
            return new List<string>
            {
                $"{f1}防守家门口",
                $"{f1}攻击敌方主基地",
                $"{f2}去东矿",
                "全军集火敌方英雄",
                "全军撤退",
                "造4个弓箭手",
                "研究攻击",
                "工人采魔法矿",
                "派个骑兵去侦查一圈",
                "所有的骑兵编入二队",
                $"{f1}一半的兵编入{f2}",
                "拉两个农夫去开个分矿",
                "所有农民按比分配采资源",
                "依次造2个兵营和3个箭塔",
                "农民修理受损建筑",
            };
        }

        void OnGUI()
        {
            var g = Game.I;
            bool show = g != null && g.started && !g.over;
            TypingActive = false;
            PointerOver = false;
            if (!show) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
                style.normal.textColor = Color.white;
            }

            // 收拢态：右侧小把手，不遮挡世界
            if (collapsed)
            {
                if (GUI.Button(new Rect(Screen.width - 34, 160, 30, 96), "指\n挥\n台")) collapsed = false;
                return;
            }

            var tips = BuildTips();
            float tipsH = 20 + Mathf.CeilToInt(tips.Count / 2f) * 24 + 4;
            float w = 340, x = Screen.width - w - 8;
            float top = 32, bottom = Screen.height - 130;   // 底部让出 HUD 栏
            float msgH = Mathf.Max(60, bottom - top - 28 - 36 - tipsH - 34);
            var r = new Rect(x, top, w, bottom - top);
            PointerOver = r.Contains(Event.current.mousePosition);
            GUI.Box(r, GUIContent.none);

            if (rawStyle == null)
            {
                rawStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
                rawStyle.normal.textColor = new Color(.75f, .75f, .78f);
            }

            // 头部：标题 + LLM 状态 + 收拢
            bool ready = LlmClient.I != null && LlmClient.I.Available;
            GUI.color = ready ? new Color(.6f, 1f, .7f) : new Color(1f, .7f, .5f);
            GUI.Label(new Rect(x + 8, top + 4, w - 96, 22), ready ? "指挥台 · 参谋在线" : "指挥台 · 离线兜底", style);
            GUI.color = Color.white;
            if (GUI.Button(new Rect(x + w - 88, top + 4, 80, 22), "收拢 —")) collapsed = true;

            // 消息区（带"原始输出"折叠：LLM 原始 JSON / API 错误默认收起，点开可查）
            float viewH = 8;
            for (int i = 0; i < msgs.Count; i++) viewH += MsgHeight(msgs[i], w - 46);
            scroll = GUI.BeginScrollView(new Rect(x + 8, top + 28, w - 16, msgH),
                scroll, new Rect(0, 0, w - 40, Mathf.Max(viewH, msgH - 4)));
            float y = 4;
            for (int i = 0; i < msgs.Count; i++)
            {
                var m = msgs[i];
                GUI.color = m.who == "你" ? new Color(.6f, .9f, 1f)
                    : m.who == "参谋" ? new Color(.6f, 1f, .7f) : new Color(1f, .8f, .5f);
                GUI.Label(new Rect(0, y, w - 46, 40), $"{m.who}：{m.text}", style);
                GUI.color = Color.white;
                y += 42;
                if (!string.IsNullOrEmpty(m.raw))
                {
                    if (GUI.Button(new Rect(0, y, 110, 15), m.rawOpen ? "▾ 收起原始输出" : "▸ 查看原始输出"))
                    { m.rawOpen = !m.rawOpen; msgs[i] = m; }
                    y += 17;
                    if (m.rawOpen)
                    {
                        float rh = RawHeight(m.raw, w - 56);
                        GUI.Box(new Rect(0, y, w - 44, rh + 6), GUIContent.none);
                        GUI.Label(new Rect(4, y + 3, w - 52, rh), m.raw, rawStyle);
                        y += rh + 8;
                    }
                }
            }
            GUI.EndScrollView();

            // 输入行
            float iy = top + 28 + msgH + 4;
            GUI.SetNextControlName("cmdInput");
            input = GUI.TextField(new Rect(x + 8, iy, w - 92, 28), input, 200);
            if (focusInput) { GUI.FocusControl("cmdInput"); focusInput = false; }
            bool enter = Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                && GUI.GetNameOfFocusedControl() == "cmdInput";
            GUI.enabled = !waiting;
            if (GUI.Button(new Rect(x + w - 80, iy, 72, 28), waiting ? "思考中…" : "发送")) enter = true;
            GUI.enabled = true;
            if (enter)
            {
                var t = input; input = "";
                Send(t);
                focusInput = true;
                Event.current.Use();
            }

            // 高频命令 tips（点击填入输入框，改几个字就能发）
            float ty = iy + 32;
            GUI.Label(new Rect(x + 8, ty, w - 16, 20), "高频命令（点击填入）:", style);
            for (int i = 0; i < tips.Count; i++)
            {
                float bx = x + 8 + (i % 2) * ((w - 20) / 2f);
                float by = ty + 20 + (i / 2) * 24;
                if (GUI.Button(new Rect(bx, by, (w - 24) / 2f, 22), tips[i])) { input = tips[i]; focusInput = true; }
            }

            // 滚动建议：环形滚动展播可尝试的命令，点击填入
            int si = Suggestions.Length == 0 ? 0 : (int)(Time.time / 6f) % Suggestions.Length;
            string s = Suggestions[si];
            int off = s.Length == 0 ? 0 : (int)(Time.time * 2.2f) % s.Length;
            string shown = s.Substring(off) + (off > 0 ? s.Substring(0, off) : "");
            var sr = new Rect(x + 8, bottom - 30, w - 16, 24);
            GUI.BeginGroup(sr);
            if (GUI.Button(new Rect(0, 0, sr.width, sr.height), "试试：" + shown)) { input = s; focusInput = true; }
            GUI.EndGroup();

            TypingActive = GUI.GetNameOfFocusedControl() == "cmdInput";
        }
    }
}
