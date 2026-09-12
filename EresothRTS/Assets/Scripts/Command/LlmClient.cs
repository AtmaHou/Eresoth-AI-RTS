using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Eresoth
{
    /// <summary>LLM 客户端：OpenAI 兼容 API（POST {base_url}/chat/completions）。
    /// 配置读项目根目录 llm_config.json（base_url/api_key/model，已加 .gitignore，不进版本库）。
    /// Demo 期允许客户端直连；正式版必须迁到独立 AI Gateway（见计划文档 §4.2）。</summary>
    public class LlmClient : MonoBehaviour
    {
        public static LlmClient I;

        [Serializable]
        class LlmConfig { public string base_url = "https://api.openai.com/v1"; public string api_key = ""; public string model = "gpt-4o-mini"; }

        LlmConfig cfg;
        bool configLoaded;
        public bool Available { get; private set; }
        public string UnavailableReason { get; private set; }

        const float TimeoutSeconds = 12f;

        void OnEnable() { I = this; TryLoadConfig(); }
        void OnDestroy() { if (I == this) I = null; }

        void TryLoadConfig()
        {
            configLoaded = true;
            try
            {
                // 项目根目录（编辑器）或 exe 同级目录（打包后）
                string path = Path.Combine(Application.dataPath, "../llm_config.json");
                if (!File.Exists(path))
                {
                    Available = false;
                    UnavailableReason = "未找到 llm_config.json（项目根目录，含 base_url/api_key/model），已启用本地兜底解析";
                    return;
                }
                cfg = JsonUtility.FromJson<LlmConfig>(File.ReadAllText(path));
                Available = cfg != null && !string.IsNullOrEmpty(cfg.api_key);
                if (!Available) UnavailableReason = "llm_config.json 缺少 api_key，已启用本地兜底解析";
            }
            catch (Exception e)
            {
                Available = false;
                UnavailableReason = $"llm_config.json 读取失败：{e.Message}";
            }
        }

        /// <summary>解析玩家指令：构建 Prompt → 调 API → 回调原始 JSON（成功）或 null（失败，走兜底）。</summary>
        public void Parse(string playerText, string digestJson, string historyText, Action<string> onJson, Action<string> onError)
        {
            if (!configLoaded) TryLoadConfig();
            if (!Available) { onError?.Invoke(UnavailableReason); return; }
            StartCoroutine(Request(PromptBuilder.SystemPrompt(), PromptBuilder.UserPrompt(playerText, digestJson, historyText), onJson, onError));
        }

        IEnumerator Request(string system, string user, Action<string> onJson, Action<string> onError)
        {
            string body = "{"
                + "\"model\":\"" + cfg.model + "\","
                + "\"temperature\":0.2,"
                + "\"response_format\":{\"type\":\"json_object\"},"
                + "\"messages\":["
                + "{\"role\":\"system\",\"content\":" + JsonString(system) + "},"
                + "{\"role\":\"user\",\"content\":" + JsonString(user) + "}]}";

            using var req = new UnityWebRequest(cfg.base_url.TrimEnd('/') + "/chat/completions", "POST");
            req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + cfg.api_key);
            req.timeout = (int)TimeoutSeconds;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            { onError?.Invoke($"API 请求失败：{req.error}"); yield break; }

            string content = ExtractContent(req.downloadHandler.text);
            if (content == null) { onError?.Invoke("API 返回格式异常"); yield break; }
            onJson?.Invoke(content);
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

        [Serializable] class ChatResponse { public System.Collections.Generic.List<Choice> choices; }
        [Serializable] class Choice { public Message message; }
        [Serializable] class Message { public string content; }

        static string JsonString(string s)
            => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    }
}
