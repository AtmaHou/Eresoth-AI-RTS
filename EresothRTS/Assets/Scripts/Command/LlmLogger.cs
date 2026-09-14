using System;
using System.IO;
using UnityEngine;

namespace Eresoth
{
    /// <summary>LLM 连接日志：每次 API 调用（解析/连通性测试）的请求体、HTTP 状态、响应体、
    /// 延迟与错误全量落盘 llm_log.jsonl（与 command_log.jsonl 同目录，已 gitignore）。
    /// 定位"连接失败/输出非法/格式不兼容"的第一手现场。</summary>
    public static class LlmLogger
    {
        static string path;
        static string LogPath
            => path ??= Path.Combine(Application.dataPath, "../llm_log.jsonl");

        /// <summary>记录一次完整的 LLM 调用现场。</summary>
        /// <param name="phase">parse（对局解析）/ test（菜单连通性测试）</param>
        /// <param name="status">HTTP 状态码（网络层失败时为 0）</param>
        public static void Log(string phase, string model, string url, string request,
            long status, string response, int ms, string err)
        {
            try
            {
                string line = "{"
                    + "\"wall\":" + J(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                    + ",\"t\":" + Time.realtimeSinceStartup.ToString("0.0")
                    + ",\"phase\":" + J(phase)
                    + ",\"model\":" + J(model)
                    + ",\"url\":" + J(url)
                    + ",\"request\":" + J(Truncate(request, 4000))
                    + ",\"status\":" + status
                    + ",\"ms\":" + ms
                    + ",\"response\":" + J(Truncate(response, 4000))
                    + ",\"error\":" + J(err)
                    + "}";
                File.AppendAllText(LogPath, line + "\n");
            }
            catch { /* 日志失败不影响游戏 */ }
        }

        static string Truncate(string s, int max)
            => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max) + "…");

        static string J(string s)
            => s == null ? "null"
                : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    }
}
