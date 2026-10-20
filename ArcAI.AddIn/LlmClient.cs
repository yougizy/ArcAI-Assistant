using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ArcAI.AddIn
{
    internal sealed class ChatMessage
    {
        public string Role { get; private set; }
        public string Content { get; private set; }

        public ChatMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }
    }

    internal sealed class LlmException : Exception
    {
        public LlmException(string message) : base(message) { }
        public LlmException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>Minimal OpenAI-compatible /chat/completions client.</summary>
    internal static class LlmClient
    {
        static LlmClient()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;
            }
            catch
            {
            }
        }

        public static string NormalizeEndpoint(string baseUrl)
        {
            string url = (baseUrl ?? string.Empty).Trim();
            if (url.Length == 0)
                throw new LlmException("请先在设置中填写 API Base URL。");
            if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                return url;
            return url.TrimEnd('/') + "/chat/completions";
        }

        public static async Task<string> ChatAsync(AppSettings settings, IList<ChatMessage> messages,
            CancellationToken cancellationToken)
        {
            if (settings == null)
                throw new ArgumentNullException("settings");
            if (messages == null || messages.Count == 0)
                throw new LlmException("没有可发送的消息。");

            string endpoint = NormalizeEndpoint(settings.BaseUrl);
            if (string.IsNullOrWhiteSpace(settings.Model))
                throw new LlmException("请先在设置中填写模型名称。");
            bool local = endpoint.IndexOf("localhost", StringComparison.OrdinalIgnoreCase) >= 0
                         || endpoint.IndexOf("127.0.0.1", StringComparison.OrdinalIgnoreCase) >= 0;
            if (string.IsNullOrWhiteSpace(settings.ApiKey) && !local)
                throw new LlmException("请先在设置中填写 API Key。");

            if (!settings.VerifySsl)
            {
                // The user explicitly opted out. This is process-wide, so it is only
                // applied when the checkbox is off (default is on).
                ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
                Log.Write("WARNING: SSL certificate validation is disabled by the user.");
            }

            var payload = JObject.FromObject(new
            {
                model = settings.Model,
                messages = ToPayload(messages),
                temperature = settings.Temperature,
                max_tokens = settings.MaxTokens,
                stream = false
            });
            ApplyThinking(payload, settings);

            using (var handler = new HttpClientHandler())
            using (var client = new HttpClient(handler))
            {
                // Many corporate/ArcMap machines need the Windows proxy with
                // default credentials; HttpClientHandler's default proxy does
                // not always supply them.
                try
                {
                    IWebProxy systemProxy = WebRequest.GetSystemWebProxy();
                    if (systemProxy != null)
                    {
                        systemProxy.Credentials = CredentialCache.DefaultCredentials;
                        handler.Proxy = systemProxy;
                        handler.UseProxy = true;
                    }
                }
                catch (Exception proxyError)
                {
                    Log.Error("Could not configure system proxy for the LLM request.", proxyError);
                }
                client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
                using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    request.Headers.UserAgent.ParseAdd("ArcAI/1.0");
                    string body = JsonConvert.SerializeObject(payload);
                    request.Content = new StringContent(body, new UTF8Encoding(false), "application/json");

                    HttpResponseMessage response;
                    try
                    {
                        response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw new LlmException("请求已取消。");
                    }
                    catch (TaskCanceledException ex)
                    {
                        throw new LlmException("请求超时（" + settings.TimeoutSeconds + " 秒）。", ex);
                    }
                    catch (Exception ex)
                    {
                        string detail = ex.GetType().Name + ": " + ex.Message;
                        Exception inner = ex.InnerException;
                        while (inner != null)
                        {
                            detail += " → " + inner.GetType().Name + ": " + inner.Message;
                            inner = inner.InnerException;
                        }
                        throw new LlmException("网络请求失败：" + detail
                            + "\n请检查网络/代理/SSL；可在设置中点击“网络诊断”。", ex);
                    }

                    using (response)
                    {
                        string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                        {
                            throw new LlmException("API 返回 HTTP " + (int)response.StatusCode + " "
                                + response.ReasonPhrase + "\n" + Truncate(responseBody, 3000));
                        }

                        string content = ExtractContent(responseBody);
                        if (string.IsNullOrWhiteSpace(content))
                            throw new LlmException("API 返回中没有 choices[0].message.content：\n"
                                + Truncate(responseBody, 3000));
                        return content.Trim();
                    }
                }
            }
        }

        /// <summary>
        /// Maps the UI thinking level to the provider's request fields. "自动"
        /// sends nothing; Qwen uses enable_thinking/thinking_budget; OpenAI-
        /// compatible endpoints use reasoning_effort. DeepSeek reasoner always
        /// thinks, so no extra field is added for it.
        /// </summary>
        private static void ApplyThinking(JObject payload, AppSettings settings)
        {
            string level = (settings.ThinkingLevel ?? string.Empty).Trim();
            if (level.Length == 0 || level == "自动")
                return;

            string effort;
            int budget;
            if (level == "低")
            {
                effort = "low";
                budget = 1024;
            }
            else if (level == "中")
            {
                effort = "medium";
                budget = 4096;
            }
            else if (level == "高")
            {
                effort = "high";
                budget = 8192;
            }
            else
            {
                return;
            }

            string provider = settings.Provider ?? string.Empty;
            string model = settings.Model ?? string.Empty;
            string url = settings.BaseUrl ?? string.Empty;
            bool qwen = provider.IndexOf("通义", StringComparison.OrdinalIgnoreCase) >= 0
                || provider.IndexOf("qwen", StringComparison.OrdinalIgnoreCase) >= 0
                || url.IndexOf("dashscope", StringComparison.OrdinalIgnoreCase) >= 0;
            if (qwen)
            {
                payload["enable_thinking"] = true;
                payload["thinking_budget"] = budget;
                return;
            }

            bool deepSeekReasoner = provider.IndexOf("deepseek", StringComparison.OrdinalIgnoreCase) >= 0
                && (model.IndexOf("reasoner", StringComparison.OrdinalIgnoreCase) >= 0
                    || model.IndexOf("r1", StringComparison.OrdinalIgnoreCase) >= 0);
            if (deepSeekReasoner)
                return;

            payload["reasoning_effort"] = effort;
        }

        private static object[] ToPayload(IList<ChatMessage> messages)
        {
            var result = new object[messages.Count];
            for (int i = 0; i < messages.Count; i++)
            {
                result[i] = new { role = messages[i].Role, content = messages[i].Content };
            }
            return result;
        }

        private static string ExtractContent(string json)
        {
            try
            {
                JObject root = JObject.Parse(json);
                JArray choices = root["choices"] as JArray;
                if (choices == null || choices.Count == 0)
                    return null;
                JToken message = choices[0]["message"];
                if (message == null)
                    return null;
                JToken content = message["content"];
                return content == null ? null : content.ToString();
            }
            catch (Exception ex)
            {
                throw new LlmException("无法解析 API 响应 JSON：" + ex.Message, ex);
            }
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Length <= max ? value : value.Substring(0, max) + "\n…（已截断）";
        }
    }
}
