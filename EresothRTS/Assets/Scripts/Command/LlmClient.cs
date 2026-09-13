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
    /// 都没有则走本地兜底解析。Demo 期允许客户端直连；正式版必须迁到独立 AI Gateway（见计划文档 §4.2）。</summary>
    public class LlmClient : MonoBehaviour
    {
        public static LlmClient I;

        [Serializable]
        class LlmConfig { public string base_url = "https://api.openai.com/v1"; public string api_key = ""; public string model = "gpt-4o-mini"; }

        LlmConfig cfg;
        bool configLoaded;
        public bool Available { get; private set; }
        public string UnavailableReason { get; private set; }
        /// <summary>是否存在运行时保存的本机配置（抽屉里"清除本机配置"按钮用）。</summary>
        public bool HasLocalConfig => File.Exists(LocalCfgPath);

        const float TimeoutSeconds = 12f;

        static string LocalCfgPath => Path.Combine(Application.persistentDataPath, "llm_config.json");
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
        public void GetConfig(out string baseUrl, out string apiKey, out string model)
        {
            baseUrl = cfg?.base_url ?? ""; apiKey = cfg?.api_key ?? ""; model = cfg?.model ?? "";
        }

        /// <summary>运行时保存：写仓库目录之外的本机文件，随后立即生效。</summary>
        public void SaveLocal(string baseUrl, string apiKey, string model)
        {
            try
            {
                var c = new LlmConfig { base_url = baseUrl, api_key = apiKey, model = model };
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

        [Serializable] class ChatResponse { public System.Collections.Generic.List<Choice> choices = null; }
        [Serializable] class Choice { public Message message = null; }
        [Serializable] class Message { public string content = null; }

        static string JsonString(string s)
            => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    }
}
