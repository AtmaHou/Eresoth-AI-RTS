using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Eresoth
{
    /// <summary>LLM 客户端：OpenAI 兼容 API（POST {base_url}/chat/completions）。
    /// 配置两处来源（前者优先）：1) 对局内按 F10 / 指挥台"设置"按钮运行时填写，存 persistentDataPath
    /// 本机文件（仓库目录之外，git 永远碰不到）；2) 项目根目录手动放置的 llm_config.json（已加 .gitignore）。
    /// 可配项：base_url / api_key / model / thinking（推理模式开关，名字不含 thinking/reason 的推理模型
    /// 如 kimi-k2.6 需手动开启；名字能识别的始终强制按推理对待）。都没有则走本地兜底解析。
    /// Demo 期允许客户端直连；正式版必须迁到独立 AI Gateway（见计划文档 §4.2）。</summary>
    public class LlmClient : MonoBehaviour
    {
        public static LlmClient I;

        [Serializable]
        class LlmConfig
        {
            public string base_url = "https://api.openai.com/v1";
            public string api_key = "";
            public string model = "gpt-4o-mini";
            /// <summary>推理模式：模型会先输出推理过程再给出正文。名字可识别的推理模型始终强制开启，
            /// 此处用于手动标记 kimi-k2.6 这类"隐性"推理模型；旧配置文件缺该字段时默认关。</summary>
            public bool thinking = false;
        }

        LlmConfig cfg;
        bool configLoaded;
        public bool Available { get; private set; }
        public string UnavailableReason { get; private set; }
        /// <summary>最近一次解析调用的请求体/原始响应（command_log.jsonl 落盘用，含完整 prompt 现场）。</summary>
        public string LastRequestBody { get; private set; }
        public string LastResponseBody { get; private set; }
        /// <summary>是否存在运行时保存的本机配置（"清除本机配置"按钮用）。</summary>
        public bool HasLocalConfig => File.Exists(LocalConfigPath);

        /// <summary>运行时配置的本机保存路径（persistentDataPath，仓库目录之外），菜单界面直接展示。</summary>
        public static string LocalConfigPath => Path.Combine(Application.persistentDataPath, "llm_config.json");

        const float TimeoutSeconds = 30f;

        static string LocalCfgPath => LocalConfigPath;
        static string ProjectCfgPath => Path.Combine(Application.dataPath, "../llm_config.json");

        void OnEnable() { I = this; TryLoadConfig(); }
        void OnDestroy() { if (I == this) I = null; }

        void TryLoadConfig()
        {
            configLoaded = true;
            string path = File.Exists(LocalCfgPath) ? LocalCfgPath
                : File.Exists(ProjectCfgPath) ? ProjectCfgPath : null;
            if (path == null)
            {
                Available = false;
                UnavailableReason = "未配置 LLM（对局中按 F10 或指挥台\"设置\"按钮填写 base_url/api_key/model，只存本机不入库），已启用本地兜底解析";
                return;
            }
            try
            {
                cfg = JsonUtility.FromJson<LlmConfig>(File.ReadAllText(path));
                Available = cfg != null && !string.IsNullOrEmpty(cfg.api_key);
                if (!Available) UnavailableReason = "llm_config.json 缺少 api_key（可运行时按 F10 填写），已启用本地兜底解析";
            }
            catch (Exception e)
            {
                Available = false;
                UnavailableReason = $"llm_config.json 读取失败：{e.Message}";
            }
        }

        /// <summary>当前生效配置（配置抽屉回填用；api_key 原样返回，仅用于本机编辑框）。</summary>
        public void GetConfig(out string baseUrl, out string apiKey, out string model, out bool thinking)
        {
            baseUrl = cfg?.base_url ?? ""; apiKey = cfg?.api_key ?? ""; model = cfg?.model ?? "";
            thinking = cfg?.thinking ?? false;
        }

        /// <summary>运行时保存：写仓库目录之外的本机文件，随后立即生效。</summary>
        public void SaveLocal(string baseUrl, string apiKey, string model, bool thinking)
        {
            try
            {
                var c = new LlmConfig { base_url = baseUrl, api_key = apiKey, model = model, thinking = thinking };
                File.WriteAllText(LocalCfgPath, JsonUtility.ToJson(c, true));
                cfg = c; configLoaded = true;
                Available = !string.IsNullOrEmpty(c.api_key);
                UnavailableReason = Available ? null : "缺少 api_key";
            }
            catch (Exception e)
            {
                Available = false;
                UnavailableReason = $"配置保存失败：{e.Message}";
            }
        }

        /// <summary>删除本机配置并重新加载（回退到项目根配置或转为不可用）。</summary>
        public void ClearLocal()
        {
            try { if (File.Exists(LocalCfgPath)) File.Delete(LocalCfgPath); } catch { }
            cfg = null;
            TryLoadConfig();
        }

        /// <summary>解析玩家指令：构建 Prompt → 调 API → 回调原始 JSON（成功）或 null（失败，走兜底）。
        /// onError 附带 API 原始返回/错误详情，供指挥台"原始输出"折叠展示。</summary>
        public void Parse(string playerText, string digestJson, string historyText, Action<string> onJson, Action<string, string> onError)
        {
            if (!configLoaded) TryLoadConfig();
            if (!Available) { onError?.Invoke(UnavailableReason, null); return; }
            StartCoroutine(Request(PromptBuilder.SystemPrompt(), PromptBuilder.UserPrompt(playerText, digestJson, historyText), onJson, onError));
        }

        IEnumerator Request(string system, string user, Action<string> onJson, Action<string, string> onError)
        {
            // 参数兼容：Kimi 全系只能 temperature=1 且不支持 response_format；思考模型同理，超时放宽
            bool thinking = cfg.thinking || IsThinkingModel(cfg.model);
            string body = "{\"model\":" + JsonString(cfg.model)
                + ",\"temperature\":" + PickTemperature(cfg.model, cfg.base_url, thinking)
                + (SupportsJsonMode(cfg.model, cfg.base_url, thinking) ? ",\"response_format\":{\"type\":\"json_object\"}" : "")
                + ",\"messages\":["
                + "{\"role\":\"system\",\"content\":" + JsonString(system) + "},"
                + "{\"role\":\"user\",\"content\":" + JsonString(user) + "}]}";

            using var req = new UnityWebRequest(cfg.base_url.TrimEnd('/') + "/chat/completions", "POST");
            req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + cfg.api_key);
            req.timeout = thinking ? 60 : (int)TimeoutSeconds;

            float t0 = Time.realtimeSinceStartup;
            LastRequestBody = body;
            yield return req.SendWebRequest();
            int elapsed = Mathf.RoundToInt((Time.realtimeSinceStartup - t0) * 1000f);
            string url = cfg.base_url.TrimEnd('/') + "/chat/completions";
            string respText = req.downloadHandler?.text;
            LastResponseBody = respText;

            if (req.result != UnityWebRequest.Result.Success)
            {
                string errMsg = $"API 请求失败：{req.error}{FmtServerError(respText)}";
                LlmLogger.Log("parse", cfg.model, url, body, req.responseCode, respText, elapsed, errMsg);
                onError?.Invoke(errMsg, Truncate(respText));
                yield break;
            }

            string content = ExtractContent(respText);
            if (content == null || string.IsNullOrWhiteSpace(content))
            {
                string errMsg = "API 返回格式异常（思考模型可能因 max_tokens 截断导致 content 为空）";
                LlmLogger.Log("parse", cfg.model, url, body, req.responseCode, respText, elapsed, errMsg);
                onError?.Invoke(errMsg, Truncate(respText));
                yield break;
            }
            LlmLogger.Log("parse", cfg.model, url, body, req.responseCode, respText, elapsed, null);
            onJson?.Invoke(content);
        }

        /// <summary>是否思考/推理模型：名字含 thinking/reason 的走兼容参数与放宽超时。</summary>
        public static bool IsThinkingModel(string model)
            => model != null && (model.ToLowerInvariant().Contains("thinking")
                || model.ToLowerInvariant().Contains("reason"));

        /// <summary>是否 Kimi/Moonshot 系：kimi-k2 等全系只接受 temperature=1，且不支持 response_format；
        /// 按模型名（kimi/moonshot）或接口地址（moonshot）识别。</summary>
        public static bool IsKimiModel(string model, string baseUrl)
        {
            bool Match(string s) => s != null
                && (s.ToLowerInvariant().Contains("kimi") || s.ToLowerInvariant().Contains("moonshot"));
            return Match(model) || Match(baseUrl);
        }

        /// <summary>temperature 取值：Kimi 系与思考模型只能为 1，其余 0.2。</summary>
        static float PickTemperature(string model, string baseUrl, bool thinking)
            => (IsKimiModel(model, baseUrl) || thinking) ? 1f : 0.2f;

        /// <summary>是否可带 response_format json_object：Kimi 系与思考模型不支持。</summary>
        static bool SupportsJsonMode(string model, string baseUrl, bool thinking)
            => !IsKimiModel(model, baseUrl) && !thinking;

        /// <summary>从 OpenAI 风格错误响应体里取服务端给出的具体原因（Moonshot/DeepSeek 都返回 error.message）。</summary>
        static string ExtractServerError(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var resp = JsonUtility.FromJson<ErrorResponse>(json);
                if (resp == null) return null;
                if (resp.error != null && !string.IsNullOrEmpty(resp.error.message)) return resp.error.message;
                if (!string.IsNullOrEmpty(resp.error_message)) return resp.error_message;
                if (!string.IsNullOrEmpty(resp.message)) return resp.message;
            }
            catch { }
            return null;
        }

        /// <summary>拼进错误提示的服务端原因，如 "（The model `xxx` does not exist）"。</summary>
        static string FmtServerError(string rawBody)
        {
            string msg = ExtractServerError(rawBody);
            return msg == null ? "" : $"（{Truncate(msg, 300)}）";
        }

        [Serializable] class ErrorResponse { public ErrorMsg error = null; public string error_message = null; public string message = null; }
        [Serializable] class ErrorMsg { public string message = null; public string type = null; }

        static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return null;
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        static string Truncate(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            const int max = 2000;
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        // ---------------- 连通性测试（开局菜单"测试连通性"按钮，不改动已保存配置） ----------------

        /// <summary>用给定参数发一次最小 chat 请求，回报是否连通与延迟。仅测试，不保存、不切换配置。
        /// thinking 为手动推理模式开关，与名字识别结果取或：显式开启时直接用大 token 预算。</summary>
        public void TestConnection(string baseUrl, string apiKey, string model, bool thinking, Action<bool, string> onDone)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) { onDone?.Invoke(false, "接口地址为空"); return; }
            if (string.IsNullOrWhiteSpace(apiKey)) { onDone?.Invoke(false, "密钥为空"); return; }
            if (string.IsNullOrWhiteSpace(model)) { onDone?.Invoke(false, "模型名为空"); return; }
            StartCoroutine(TestRequest(baseUrl.TrimEnd('/'), apiKey.Trim(), model.Trim(), thinking, onDone));
        }

        IEnumerator TestRequest(string baseUrl, string apiKey, string model, bool forceThinking, Action<bool, string> onDone)
        {
            float t0 = Time.realtimeSinceStartup;
            bool thinking = forceThinking || IsThinkingModel(model);
            // 推理模型的输出 token 先消耗在推理内容上，max_tokens 太小会导致 content 为空。
            // 手动或自动判定为推理 → 直接用 512 预算；未判定的普通模型先给 16 测延迟，
            // 若返回 finish_reason=length（实为隐性推理模型，预算被推理占光）再放大重试一次
            int maxTokens = thinking ? 512 : 16;
            while (true)
            {
                string body = "{\"model\":" + JsonString(model)
                    + ",\"max_tokens\":" + maxTokens
                    + ",\"temperature\":" + PickTemperature(model, baseUrl, thinking)
                    + ",\"messages\":[{\"role\":\"user\",\"content\":\"reply with the single word: pong\"}]}";
                using var req = new UnityWebRequest(baseUrl + "/chat/completions", "POST");
                req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Authorization", "Bearer " + apiKey);
                req.timeout = maxTokens > 16 ? 45 : 15;

                yield return req.SendWebRequest();
                int ms = Mathf.RoundToInt((Time.realtimeSinceStartup - t0) * 1000f);
                string respText = req.downloadHandler?.text;

                if (req.result != UnityWebRequest.Result.Success)
                {
                    string errMsg = $"连接失败（{ms}ms）：{req.error}{FmtServerError(respText)}";
                    LlmLogger.Log("test", model, baseUrl + "/chat/completions", body, req.responseCode, respText, ms, errMsg);
                    onDone?.Invoke(false, errMsg);
                    yield break;
                }
                string content = ExtractContent(respText);
                if (content != null && !string.IsNullOrWhiteSpace(content))
                {
                    LlmLogger.Log("test", model, baseUrl + "/chat/completions", body, req.responseCode, respText, ms, null);
                    onDone?.Invoke(true, $"连通正常 · {model} · {ms}ms");
                    yield break;
                }
                if (!thinking && maxTokens <= 16 && FinishReason(respText) == "length")
                {
                    maxTokens = 512;
                    continue;
                }
                string err = $"已连通但返回异常（{ms}ms）{FmtServerError(respText)}";
                LlmLogger.Log("test", model, baseUrl + "/chat/completions", body, req.responseCode, respText, ms, err);
                onDone?.Invoke(false, err);
                yield break;
            }
        }

        /// <summary>取 choices[0].finish_reason（如 length/stop），用于识别 max_tokens 截断。</summary>
        static string FinishReason(string json)
        {
            try
            {
                var resp = JsonUtility.FromJson<ChatResponse>(json);
                if (resp?.choices != null && resp.choices.Count > 0)
                    return resp.choices[0].finish_reason;
            }
            catch { }
            return null;
        }

        /// <summary>从 OpenAI 响应中取 choices[0].message.content（最小解析，不引第三方库）。</summary>
        static string ExtractContent(string json)
        {
            try
            {
                var resp = JsonUtility.FromJson<ChatResponse>(json);
                if (resp?.choices != null && resp.choices.Count > 0)
                    return resp.choices[0].message.content;
            }
            catch { }
            return null;
        }

        /// <summary>剥离模型可能包裹的 ```json 代码围栏：没开 response_format 的模型（如 Kimi）常这样输出。</summary>
        public static string StripCodeFence(string content)
        {
            if (string.IsNullOrEmpty(content)) return content;
            string s = content.Trim();
            if (s.StartsWith("```"))
            {
                int firstNl = s.IndexOf('\n');
                if (firstNl > 0) s = s.Substring(firstNl + 1);
                int end = s.LastIndexOf("```");
                if (end > 0) s = s.Substring(0, end);
            }
            return s.Trim();
        }

        [Serializable] class ChatResponse { public System.Collections.Generic.List<Choice> choices = null; }
        [Serializable] class Choice { public Message message = null; public string finish_reason = null; }
        [Serializable] class Message { public string content = null; }

        static string JsonString(string s)
            => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    }
}
