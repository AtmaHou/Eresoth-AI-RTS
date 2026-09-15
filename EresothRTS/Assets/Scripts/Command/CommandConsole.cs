using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Eresoth
{
    /// <summary>指挥台：右侧可收放文字指挥栏（OnGUI，零资源）。
    /// 回车发送 → 指令缓存命中则直接回放（省延迟省 token）→ 未命中走 LLM 解析（失败走本地兜底）
    /// → 结构化军令执行 → 参谋回复。参谋回复带 ✔/✘ 反馈按钮：标记成功把"指令→格式化操作"写入
    /// command_cache.json（可手编辑），标记失败移除对应条目。输入框经 ImeTextField 支持中文输入法；
    /// 原始输出折叠区可滚动查看。常用指令（favorite_commands.json）优先进入"高频命令"tips。
    /// 全部调用落盘 command_log.jsonl（将来评测/蒸馏的数据源）。LLM 配置在开局菜单填写。</summary>
    public class CommandConsole : MonoBehaviour
    {
        public static CommandConsole I;

        /// <summary>输入框聚焦中：镜头与选择快捷键静默。</summary>
        public static bool TypingActive { get; private set; }
        /// <summary>鼠标悬停在展开的指挥栏上：点击不穿透到世界。</summary>
        public static bool PointerOver { get; private set; }

        struct ChatMsg
        {
            public string who;        // 你 / 参谋 / 系统
            public string text;       // 正文
            public string raw;        // 原始输出（LLM 原文 / API 错误），折叠展示
            public string playerText; // 触发本条回复的玩家指令（反馈标记用）
            public string actionJson; // 可缓存的格式化指令；非空才显示 ✔/✘
            public int feedback;      // 0 未标记 / 1 成功(已入缓存) / -1 失败(已移出)
            public bool rawOpen;
            public Vector2 rawScroll;
            public float time;
        }

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

            // 指令缓存优先：标记过"成功"的同文指令直接回放缓存的格式化结果，跳过 LLM
            string cachedJson = CommandCache.Lookup(text);
            if (cachedJson != null)
            {
                HandleLlmJson(text, null, cachedJson, true);
                return;
            }

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

        void HandleLlmJson(string text, string digest, string json, bool fromCache = false)
        {
            waiting = false;
            json = LlmClient.StripCodeFence(json);   // Kimi 等无 response_format 的模型可能包 ```json 围栏
            DebugCommandRunner.CommandRequest req = null;
            try { req = JsonUtility.FromJson<DebugCommandRunner.CommandRequest>(json); } catch { }

            if (req == null) { HandleFallback(text, digest, fromCache ? "缓存内容解析失败" : "模型输出非法 JSON", json); return; }

            // 追问分支：不执行任何军令
            if (req.clarification_needed && !string.IsNullOrEmpty(req.question))
            {
                Add("参谋", req.question, json, text);
                Log(text, digest, json, fromCache ? "cache_hit_clarification" : "clarification");
                PushTurn(text, req.question);
                return;
            }

            req.player_text = text;
            int executed = Execute(req, out string execSummary);
            string reply = !string.IsNullOrEmpty(req.player_reply) ? req.player_reply
                : executed > 0 ? "收到，已下达。" : "没有可执行的指令。";
            if (fromCache) reply += "［缓存命中］";
            Add("参谋", reply, json, text, json);
            Log(text, digest, json,
                fromCache ? "cache_hit" : executed > 0 ? "executed" : "no-op", execSummary);
            PushTurn(text, reply);
        }

        void HandleFallback(string text, string digest, string reason, string raw)
        {
            waiting = false;
            var req = LocalFallbackParser.TryParse(text);
            if (req == null)
            {
                Add("参谋", $"没听懂（{reason}）。试试：\"一军团防守家门口\"、\"二军团去打东矿\"、\"造4个弓箭手\"。", raw, text);
                Log(text, digest, null, "fallback_failed: " + reason);
                return;
            }
            int executed = Execute(req, out string fbSummary);
            Add("参谋", (req.player_reply ?? "收到。") + "［离线兜底］", raw, text, JsonUtility.ToJson(req));
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

        /// <summary>反馈标记：✔ 成功 → 写入/更新指令缓存；✘ 失败 → 移除对应条目，防止错误指令被拦截。</summary>
        void MarkFeedback(int i, bool ok)
        {
            var m = msgs[i];
            if (ok) CommandCache.Put(m.playerText, m.actionJson);
            else CommandCache.Remove(m.playerText);
            m.feedback = ok ? 1 : -1;
            msgs[i] = m;
        }

        void PushTurn(string player, string reply)
        {
            recentTurns.Enqueue($"玩家：{player}\n参谋：{reply}");
            while (recentTurns.Count > 4) recentTurns.Dequeue();
        }

        void Add(string who, string text, string raw = null, string playerText = null, string actionJson = null)
        {
            msgs.Add(new ChatMsg { who = who, text = text, raw = raw, playerText = playerText, actionJson = actionJson, time = Time.time });
            if (msgs.Count > 50) msgs.RemoveAt(0);
            scroll.y = float.MaxValue;   // 滚到底
        }

        void Log(string text, string digest, string modelJson, string result, string exec = null, bool cacheHit = false)
        {
            try
            {
                // 完整现场：墙上时间 + 发给 LLM 的请求体（含 prompt）+ API 原始响应；均为最近一次调用。
                // 缓存命中没有本次请求，写 null 以免带上一次调用的现场误导复盘
                string reqBody = !cacheHit && LlmClient.I != null ? LlmClient.I.LastRequestBody : null;
                string respBody = !cacheHit && LlmClient.I != null ? LlmClient.I.LastResponseBody : null;
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

        /// <summary>单条消息高度（正文 42 + 折叠的原始输出行；展开时按内容估算，封顶 156 可视 + 边距）。</summary>
        static float MsgHeight(ChatMsg m, float width)
        {
            float h = 42;
            if (!string.IsNullOrEmpty(m.raw))
            {
                h += 17;
                if (m.rawOpen) h += Mathf.Min(RawHeight(m.raw, width - 10, 1000) + 8, 156);
            }
            return h;
        }

        /// <summary>原始输出展示高度：按字符数折行估算；maxLines 封顶（展开可视区与内容高度共用本估算）。</summary>
        static float RawHeight(string raw, float width, int maxLines = 6)
        {
            int charsPerLine = Mathf.Max(20, (int)(width / 6.5f));
            int lines = 0;
            foreach (var seg in raw.Split('\n'))
                lines += Mathf.Max(1, Mathf.CeilToInt(seg.Length / (float)charsPerLine));
            return Mathf.Min(lines, maxLines) * 14 + 4;
        }

        // ---------------- UI ----------------

        /// <summary>高频命令 tips：常用指令（开始界面配置）优先；未配置时用结合本局军团名的默认样例。点击填入输入框。</summary>
        List<string> BuildTips()
        {
            var fav = FavoriteCommands.Items;
            if (fav.Count > 0) return new List<string>(fav);

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

            // 消息区（带"原始输出"折叠：LLM 原始 JSON / API 错误默认收起，点开可滚动查看全文）
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
                GUI.Label(new Rect(0, y, w - (string.IsNullOrEmpty(m.actionJson) ? 46 : 122), 40), $"{m.who}：{m.text}", style);
                GUI.color = Color.white;

                // 反馈标记：✔ 成功入缓存 / ✘ 失败移出（只对执行了格式化指令的参谋回复显示）
                if (!string.IsNullOrEmpty(m.actionJson))
                {
                    if (m.feedback == 0)
                    {
                        GUI.color = new Color(.55f, 1f, .6f);
                        if (GUI.Button(new Rect(w - 92, y, 20, 16),
                            new GUIContent("✔", "标记指令成功：存入指令缓存，下次同输入直接执行（省延迟省 token）")))
                            MarkFeedback(i, true);
                        GUI.color = new Color(1f, .6f, .55f);
                        if (GUI.Button(new Rect(w - 68, y, 20, 16),
                            new GUIContent("✘", "标记指令失败：若缓存中有该指令则移除，不再拦截")))
                            MarkFeedback(i, false);
                        GUI.color = Color.white;
                    }
                    else
                    {
                        GUI.color = m.feedback > 0 ? new Color(.55f, 1f, .6f) : new Color(1f, .6f, .55f);
                        GUI.Label(new Rect(w - 120, y + 1, 112, 16),
                            m.feedback > 0 ? "✔ 已存指令缓存" : "✘ 已移出缓存", rawStyle);
                        GUI.color = Color.white;
                    }
                }
                y += 42;

                if (!string.IsNullOrEmpty(m.raw))
                {
                    if (GUI.Button(new Rect(0, y, 110, 15), m.rawOpen ? "▾ 收起原始输出" : "▸ 查看原始输出"))
                    { m.rawOpen = !m.rawOpen; msgs[i] = m; }
                    y += 17;
                    if (m.rawOpen)
                    {
                        // 可视区封顶 148px，内容按全文高度铺，超出部分滚动查看
                        float fullH = RawHeight(m.raw, w - 66, 1000) + 4;
                        float viewRawH = Mathf.Min(fullH, 148);
                        GUI.Box(new Rect(0, y, w - 44, viewRawH + 6), GUIContent.none);
                        m.rawScroll = GUI.BeginScrollView(new Rect(1, y + 3, w - 46, viewRawH), m.rawScroll,
                            new Rect(0, 0, w - 68, fullH));
                        GUI.Label(new Rect(2, 0, w - 72, fullH), m.raw, rawStyle);
                        GUI.EndScrollView();
                        msgs[i] = m;
                        y += viewRawH + 8;
                    }
                }
            }
            GUI.EndScrollView();

            // 输入行（ImeTextField 支持中文输入法：末尾追加/退格/粘贴，合成串实时预览）
            float iy = top + 28 + msgH + 4;
            input = ImeTextField.Draw(new Rect(x + 8, iy, w - 92, 28), input, "cmdInput", style);
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
            GUI.Label(new Rect(x + 8, ty, w - 16, 20),
                FavoriteCommands.Items.Count > 0 ? "常用指令（点击填入）：" : "高频命令（点击填入）:", style);
            for (int i = 0; i < tips.Count; i++)
            {
                float bx = x + 8 + (i % 2) * ((w - 20) / 2f);
                float by = ty + 20 + (i / 2) * 24;
                string tip = tips[i];
                string label = tip.Length > 16 ? tip.Substring(0, 16) + "…" : tip;
                if (GUI.Button(new Rect(bx, by, (w - 24) / 2f, 22), new GUIContent(label, tip))) { input = tip; focusInput = true; }
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
