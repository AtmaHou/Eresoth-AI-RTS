using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Eresoth
{
    /// <summary>指挥台：文字指挥 UI（OnGUI，零资源）。
    /// 回车发送 → LLM 解析（失败走本地兜底）→ 结构化军令执行 → 参谋回复。
    /// 全部调用落盘 command_log.jsonl（将来评测/蒸馏的数据源）。</summary>
    public class CommandConsole : MonoBehaviour
    {
        public static CommandConsole I;

        struct ChatMsg { public string who; public string text; public float time; }

        readonly List<ChatMsg> msgs = new();
        string input = "";
        bool waiting;
        bool focusInput;
        GUIStyle style;
        Vector2 scroll;
        string logPath;
        readonly Queue<string> recentTurns = new();   // 多轮上下文（最近 4 轮）

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
                    onError: err => HandleFallback(text, digest, err));
            }
            else HandleFallback(text, digest, LlmClient.I != null ? LlmClient.I.UnavailableReason : "LLM 不可用");
        }

        void HandleLlmJson(string text, string digest, string json)
        {
            waiting = false;
            DebugCommandRunner.CommandRequest req = null;
            try { req = JsonUtility.FromJson<DebugCommandRunner.CommandRequest>(json); } catch { }

            if (req == null) { HandleFallback(text, digest, "模型输出非法 JSON"); return; }

            // 追问分支：不执行任何军令
            if (req.clarification_needed && !string.IsNullOrEmpty(req.question))
            {
                Add("参谋", req.question);
                Log(text, digest, json, "clarification");
                PushTurn(text, req.question);
                return;
            }

            req.player_text = text;
            int executed = Execute(req);
            string reply = !string.IsNullOrEmpty(req.player_reply) ? req.player_reply
                : executed > 0 ? "收到，已下达。" : "没有可执行的指令。";
            Add("参谋", reply);
            Log(text, digest, json, executed > 0 ? "executed" : "no-op");
            PushTurn(text, reply);
        }

        void HandleFallback(string text, string digest, string reason)
        {
            waiting = false;
            var req = LocalFallbackParser.TryParse(text);
            if (req == null)
            {
                Add("参谋", $"没听懂（{reason}）。试试：\"一军团防守家门口\"、\"二军团去打东矿\"、\"造4个弓箭手\"。");
                Log(text, digest, null, "fallback_failed: " + reason);
                return;
            }
            int executed = Execute(req);
            Add("参谋", (req.player_reply ?? "收到。") + "［离线兜底］");
            Log(text, digest, null, $"fallback_executed({executed}): {reason}");
            PushTurn(text, req.player_reply ?? "");
        }

        /// <summary>执行解析结果：军事军令 + 经济计划逐条提交，返回成功条数。</summary>
        int Execute(DebugCommandRunner.CommandRequest req)
        {
            int ok = 0;
            if (OrderDispatcher.I == null) return 0;
            foreach (var dto in req.orders)
            {
                var o = DebugCommandRunner.Map(dto, req.player_text, out string err);
                if (o == null) { Add("系统", $"军令无效：{err}"); continue; }
                if (OrderDispatcher.I.SubmitOrder(o)) ok++;
                else Add("系统", $"军令被拒：{o.failReason}");
            }
            if (req.economy != null)
                foreach (var dto in req.economy)
                {
                    var o = DebugCommandRunner.Map(dto, req.player_text, out string err);
                    if (o == null) { Add("系统", $"经济计划无效：{err}"); continue; }
                    if (OrderDispatcher.I.SubmitOrder(o)) ok++;
                    else Add("系统", $"计划被拒：{o.failReason}");
                }
            return ok;
        }

        void PushTurn(string player, string reply)
        {
            recentTurns.Enqueue($"玩家：{player}\n参谋：{reply}");
            while (recentTurns.Count > 4) recentTurns.Dequeue();
        }

        void Add(string who, string text)
        {
            msgs.Add(new ChatMsg { who = who, text = text, time = Time.time });
            if (msgs.Count > 50) msgs.RemoveAt(0);
            scroll.y = float.MaxValue;   // 滚到底
        }

        void Log(string text, string digest, string modelJson, string result)
        {
            try
            {
                string line = "{\"t\":" + Time.time.ToString("0")
                    + ",\"text\":" + J(text) + ",\"digest\":" + (digest ?? "null")
                    + ",\"model\":" + (modelJson != null ? J(modelJson) : "null")
                    + ",\"result\":" + J(result) + "}";
                File.AppendAllText(logPath, line + "\n");
            }
            catch { /* 日志失败不影响游戏 */ }
        }

        static string J(string s)
            => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

        // ---------------- UI ----------------

        void OnGUI()
        {
            var g = Game.I;
            if (g == null || !g.started || g.over) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
                style.normal.textColor = Color.white;
            }

            float w = Mathf.Min(560, Screen.width - 20);
            float h = 190;
            var r = new Rect(10, Screen.height - 120 - h - 10, w, h);
            GUI.Box(r, GUIContent.none);

            // 消息区
            var viewH = msgs.Count * 42 + 20;
            scroll = GUI.BeginScrollView(new Rect(r.x + 6, r.y + 6, w - 12, h - 46),
                scroll, new Rect(0, 0, w - 30, Mathf.Max(viewH, h - 50)));
            float y = 4;
            foreach (var m in msgs)
            {
                GUI.color = m.who == "你" ? new Color(.6f, .9f, 1f)
                    : m.who == "参谋" ? new Color(.6f, 1f, .7f) : new Color(1f, .8f, .5f);
                GUI.Label(new Rect(0, y, w - 34, 40), $"{m.who}：{m.text}", style);
                y += 42;
            }
            GUI.color = Color.white;
            GUI.EndScrollView();

            // 输入行
            var ir = new Rect(r.x + 6, r.y + h - 34, w - 92, 28);
            GUI.SetNextControlName("cmdInput");
            input = GUI.TextField(ir, input, 200);
            if (focusInput) { GUI.FocusControl("cmdInput"); focusInput = false; }
            TypingActive = GUI.GetNameOfFocusedControl() == "cmdInput";

            bool enter = Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                && GUI.GetNameOfFocusedControl() == "cmdInput";
            GUI.enabled = !waiting;
            if (GUI.Button(new Rect(r.x + w - 80, r.y + h - 34, 74, 28), waiting ? "思考中…" : "发送"))
                enter = true;
            GUI.enabled = true;
            if (enter)
            {
                var t = input; input = "";
                Send(t);
                focusInput = true;
                Event.current.Use();
            }
        }
    }
}
