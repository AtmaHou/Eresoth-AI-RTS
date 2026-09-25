using System;
using System.IO;
using UnityEngine;

namespace Eresoth
{
    /// <summary>LLM 连接日志 v2：每次 API 调用（解析/连通性测试）全量落盘 llm_log.jsonl（与 command_log.jsonl 同目录，已 gitignore）。
    /// v2 设计目标：人与工具都能直接看清"发了什么、回了什么"——
    /// system/user 是完整 prompt 原文（上限 64KB，不再 4000 截断）；content/reasoning 分开存；
    /// usage 记 token 消耗；response_raw 只在出错时保留（截断 4000），成功时省略。
    /// prompt_src 记录当时生效的 prompt 文件版本（文件名@修改时间 或 embedded）。
    /// 另附人类可读副本 llm_io.log：每次解析调用追加一段"输入/输出"全文（编辑器里直接打开就能看，
    /// 不用解析 JSON）。配套查看工具：AI_RTS/tools/prompt_lab.py。</summary>
    public static class LlmLogger
    {
        static string path;
        static string LogPath
            => path ??= Path.Combine(Application.dataPath, "../llm_log.jsonl");

        static string ioPath;
        static string IoPath
            => ioPath ??= Path.Combine(Application.dataPath, "../llm_io.log");

        const int MaxPrompt = 64 * 1024;    // system/user 上限：远超实际（~4KB），防爆盘
        const int MaxOutput = 32 * 1024;    // content/reasoning 上限
        const int MaxRaw = 4000;            // 出错时的原始响应上限

        /// <summary>记录一次完整的 LLM 调用现场。</summary>
        /// <param name="phase">parse（对局解析）/ test（菜单连通性测试）</param>
        /// <param name="label">触发本次调用的玩家指令（llm_io.log 的标题行；测试传 null）</param>
        /// <param name="status">HTTP 状态码（网络层失败时为 0）</param>
        /// <param name="usage">"prompt,completion,reasoning" 三元组；全 0 表示未上报</param>
        /// <param name="responseRaw">原始响应体，仅出错排查用，成功传 null</param>
        public static void Log(string phase, string model, string url, string promptSrc, string label,
            string system, string user, long status, int ms,
            string content, string reasoning, (int prompt, int completion, int reasoning) usage,
            string err, string responseRaw)
        {
            try
            {
                var sb = new System.Text.StringBuilder(1024);
                sb.Append("{\"wall\":").Append(J(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
                sb.Append(",\"t\":").Append(Time.realtimeSinceStartup.ToString("0.0"));
                sb.Append(",\"v\":2,\"phase\":").Append(J(phase));
                sb.Append(",\"model\":").Append(J(model));
                sb.Append(",\"url\":").Append(J(url));
                sb.Append(",\"prompt_src\":").Append(J(promptSrc));
                sb.Append(",\"label\":").Append(J(label));
                sb.Append(",\"ms\":").Append(ms);
                sb.Append(",\"status\":").Append(status);
                sb.Append(",\"system\":").Append(J(Truncate(system, MaxPrompt)));
                sb.Append(",\"user\":").Append(J(Truncate(user, MaxPrompt)));
                sb.Append(",\"content\":").Append(J(Truncate(content, MaxOutput)));
                sb.Append(",\"reasoning\":").Append(J(Truncate(reasoning, MaxOutput)));
                if (usage.prompt > 0 || usage.completion > 0)
                {
                    sb.Append(",\"usage\":{\"prompt_tokens\":").Append(usage.prompt)
                      .Append(",\"completion_tokens\":").Append(usage.completion)
                      .Append(",\"reasoning_tokens\":").Append(usage.reasoning)
                      .Append(",\"total_tokens\":").Append(usage.prompt + usage.completion).Append('}');
                }
                else sb.Append(",\"usage\":null");
                sb.Append(",\"error\":").Append(J(err));
                sb.Append(",\"response_raw\":").Append(J(Truncate(responseRaw, MaxRaw)));
                sb.Append('}');
                File.AppendAllText(LogPath, sb + "\n");
            }
            catch { /* 日志失败不影响游戏 */ }

            try { WriteIo(phase, model, label, ms, status, system, user, content, reasoning, usage, err); }
            catch { /* 同上 */ }
        }

        /// <summary>可读副本：一次调用 = 一段"指令 + INPUT(system/user) + OUTPUT(content/reasoning/错误)"全文。
        /// 只记 parse（对局解析），连通性测试不写。</summary>
        static void WriteIo(string phase, string model, string label, int ms, long status,
            string system, string user, string content, string reasoning,
            (int prompt, int completion, int reasoning) usage, string err)
        {
            if (phase != "parse") return;
            var sb = new System.Text.StringBuilder(4096);
            sb.AppendLine("════════════════════════════════════════════════════════");
            sb.Append("● ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
              .Append(" · ").Append(model ?? "?")
              .Append(" · ").Append(ms).Append("ms · HTTP ").Append(status);
            if (usage.prompt > 0 || usage.completion > 0)
                sb.Append($" · tokens in{usage.prompt}/out{usage.completion}"
                    + (usage.reasoning > 0 ? $"/think{usage.reasoning}" : ""));
            sb.Append(err != null ? " · ✗ 失败" : " · ✓ 成功");
            if (!string.IsNullOrEmpty(label)) sb.Append("\n指令：").Append(label);
            sb.Append("\n──── INPUT · system ────\n").Append(system)
              .Append("\n──── INPUT · user ────\n").Append(user)
              .Append("\n──── OUTPUT · content ────\n").Append(string.IsNullOrEmpty(content) ? "（空）" : content);
            if (!string.IsNullOrEmpty(reasoning))
                sb.Append("\n──── OUTPUT · reasoning ────\n").Append(reasoning);
            if (err != null) sb.Append("\n✗ 错误：").Append(err);
            sb.Append('\n');
            File.AppendAllText(IoPath, sb.ToString());
        }

        static string Truncate(string s, int max)
            => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max) + "…");

        static string J(string s)
            => s == null ? "null"
                : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    }
}
