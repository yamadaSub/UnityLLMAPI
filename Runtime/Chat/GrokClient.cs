using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityLLMAPI.Common;
using UnityLLMAPI.Schema;

namespace UnityLLMAPI.Chat
{
    // Grok (x.ai) 向けの HTTP 実装をまとめた ProviderClient
    internal sealed class GrokClient : IProviderClient
    {
        private const string ChatEndpoint = "https://api.x.ai/v1/chat/completions";

        public AIProvider Provider => AIProvider.Grok;

        /// <summary>
        /// Grok のチャットエンドポイントへ通常チャットを送信する。
        /// </summary>
        public async Task<RawChatResult> SendChatAsync(
            ModelSpec model,
            IReadOnlyList<Message> messages,
            ChatRequestOptions options,
            CancellationToken ct)
        {
            var apiKey = ApiKeyResolver.GrokApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError($"API key not configured. {ApiKeyResolver.GetRequiredEnvHint(model)}");
                return FailureChatResult(model, "Missing Grok API key");
            }

            var body = new Dictionary<string, object>
            {
                { "model", model.ModelId },
                { "messages", MessagePayloadBuilder.BuildOpenAiMessages(messages?.ToList() ?? new List<Message>()) }
            };

            AddFunctions(body, options?.Functions);
            MergeAdditionalBody(body, options?.AdditionalBody);

            var jsonBody = JsonConvert.SerializeObject(body);
            using var req = BuildRequest(apiKey, jsonBody);
            await UnityWebRequestUtils.SendAsync(req, ct, options?.TimeoutSeconds ?? -1);

            return BuildRawChatResult(model, req);
        }

        public async Task<RawChatStreamResult> SendChatStreamAsync(
            ModelSpec model,
            IReadOnlyList<Message> messages,
            ChatRequestOptions options,
            System.Action<string> onContentDelta,
            CancellationToken ct)
        {
            var apiKey = ApiKeyResolver.GrokApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError($"API key not configured. {ApiKeyResolver.GetRequiredEnvHint(model)}");
                return FailureChatStreamResult(model, "Missing Grok API key");
            }

            var body = new Dictionary<string, object>
            {
                { "model", model.ModelId },
                { "messages", MessagePayloadBuilder.BuildOpenAiMessages(messages?.ToList() ?? new List<Message>()) },
                { "stream", true }
            };

            AddFunctions(body, options?.Functions);
            MergeAdditionalBody(body, options?.AdditionalBody);
            body["stream"] = true;

            var content = new StringBuilder();
            var sse = new SseEventParser(payload =>
            {
                if (string.IsNullOrEmpty(payload)) return;
                if (payload.Trim() == "[DONE]") return;
                TryConsumeGrokStreamEvent(payload, content, onContentDelta);
            });

            var jsonBody = JsonConvert.SerializeObject(body);
            using var req = BuildStreamRequest(apiKey, jsonBody, sse);
            await UnityWebRequestUtils.SendAsync(req, ct, options?.TimeoutSeconds ?? -1);
            sse.Complete();

            var rawText = req.downloadHandler?.text;
            return new RawChatStreamResult
            {
                Provider = AIProvider.Grok,
                ModelId = model.ModelId,
                IsSuccess = req.result == UnityWebRequest.Result.Success,
                StatusCode = req.responseCode,
                ErrorMessage = req.result == UnityWebRequest.Result.Success ? null : req.error,
                RawText = rawText,
                Content = content.ToString()
            };
        }

        public async Task<RawChatResult> SendStructuredAsync(
            ModelSpec model,
            IReadOnlyList<Message> messages,
            string jsonSchema,
            ChatRequestOptions options,
            CancellationToken ct)
        {
            var apiKey = ApiKeyResolver.GrokApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError($"API key not configured. {ApiKeyResolver.GetRequiredEnvHint(model)}");
                return FailureChatResult(model, "Missing Grok API key");
            }

            var parsedSchema = string.IsNullOrEmpty(jsonSchema)
                ? new Dictionary<string, object>()
                : JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonSchema);

            var body = new Dictionary<string, object>
            {
                { "model", model.ModelId },
                { "messages", MessagePayloadBuilder.BuildOpenAiMessages(messages?.ToList() ?? new List<Message>()) },
                { "response_format", new Dictionary<string, object>
                    {
                        { "type", "json_schema" },
                        { "json_schema", parsedSchema ?? new Dictionary<string, object>() }
                    }
                }
            };

            MergeAdditionalBody(body, options?.AdditionalBody);

            var jsonBody = JsonConvert.SerializeObject(body);
            using var req = BuildRequest(apiKey, jsonBody);
            await UnityWebRequestUtils.SendAsync(req, ct, options?.TimeoutSeconds ?? -1);

            return BuildRawChatResult(model, req);
        }

        public Task<RawImageResult> GenerateImageAsync(
            ModelSpec model,
            ImageGenerationRequest request,
            CancellationToken ct)
        {
            throw new System.NotSupportedException("Grok image generation is not implemented.");
        }

        public Task<RawEmbeddingResult> CreateEmbeddingAsync(
            ModelSpec model,
            IReadOnlyList<string> texts,
            CancellationToken ct)
        {
            throw new System.NotSupportedException("Grok embeddings are not implemented.");
        }

        /// <summary>
        /// Grok 用の UnityWebRequest を生成する。
        /// </summary>
        /// <summary>
        /// Grok 用の UnityWebRequest を生成する。
        /// </summary>
        private static UnityWebRequest BuildRequest(string apiKey, string jsonBody)
        {
            var req = new UnityWebRequest(ChatEndpoint, "POST")
            {
                uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonBody)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + apiKey);
            return req;
        }

        private static UnityWebRequest BuildStreamRequest(string apiKey, string jsonBody, SseEventParser sse)
        {
            var req = new UnityWebRequest(ChatEndpoint, "POST")
            {
                uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonBody)),
                downloadHandler = new StreamingDownloadHandler(chunk => sse?.Feed(chunk))
            };
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + apiKey);
            return req;
        }

        private static void TryConsumeGrokStreamEvent(
            string payload,
            StringBuilder content,
            System.Action<string> onContentDelta)
        {
            try
            {
                var obj = JObject.Parse(payload);
                var delta = obj?["choices"]?[0]?["delta"] as JObject;
                if (delta == null) return;

                var contentDelta = delta["content"]?.ToString();
                if (!string.IsNullOrEmpty(contentDelta))
                {
                    content?.Append(contentDelta);
                    onContentDelta?.Invoke(contentDelta);
                }
            }
            catch
            {
                // ignore bad stream chunks
            }
        }

        /// <summary>
        /// Function Calling 用の関数一覧をボディに追加する。
        /// </summary>
        private static void AddFunctions(Dictionary<string, object> body, IReadOnlyList<IJsonSchema> functions)
        {
            if (functions == null || functions.Count == 0) return;
            body["tools"] = functions.Select(BuildFunctionTool).ToList();
            body["tool_choice"] = "auto";
        }

        private static Dictionary<string, object> BuildFunctionTool(IJsonSchema functionSchema)
        {
            var schema = functionSchema?.GenerateJsonSchema() ?? new Dictionary<string, object>();
            var name = schema.TryGetValue("name", out var nObj) ? nObj?.ToString() : functionSchema?.Name;
            var description = schema.TryGetValue("description", out var dObj) ? dObj?.ToString() : string.Empty;

            object parameters = null;
            if (schema.TryGetValue("parameters", out var pObj)) parameters = pObj;
            else if (schema.TryGetValue("schema", out var sObj)) parameters = sObj;

            var function = new Dictionary<string, object>
            {
                { "name", name ?? string.Empty },
                { "parameters", parameters ?? new Dictionary<string, object> { { "type", "object" } } }
            };
            if (!string.IsNullOrEmpty(description))
            {
                function["description"] = description;
            }

            return new Dictionary<string, object>
            {
                { "type", "function" },
                { "function", function }
            };
        }

        /// <summary>
        /// 追加パラメータをボディへマージする。
        /// </summary>
        private static void MergeAdditionalBody(Dictionary<string, object> body, Dictionary<string, object> additional)
        {
            if (body == null || additional == null) return;
            foreach (var kv in additional) body[kv.Key] = kv.Value;
        }

        /// <summary>
        /// 生 JSON を JObject にパースする。失敗時は null。
        /// </summary>
        private static JObject TryParse(string rawJson)
        {
            if (string.IsNullOrEmpty(rawJson)) return null;
            try
            {
                return JObject.Parse(rawJson);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// UnityWebRequest のレスポンスから RawChatResult を生成する。
        /// </summary>
        private static RawChatResult BuildRawChatResult(ModelSpec model, UnityWebRequest req)
        {
            var rawJson = req.downloadHandler?.text;
            return new RawChatResult
            {
                Provider = AIProvider.Grok,
                ModelId = model.ModelId,
                IsSuccess = req.result == UnityWebRequest.Result.Success,
                StatusCode = req.responseCode,
                ErrorMessage = req.result == UnityWebRequest.Result.Success ? null : req.error,
                RawJson = rawJson,
                Body = TryParse(rawJson)
            };
        }

        /// <summary>
        /// API キー不足などで即失敗を返すためのヘルパー。
        /// </summary>
        private static RawChatResult FailureChatResult(ModelSpec model, string message)
        {
            return new RawChatResult
            {
                Provider = AIProvider.Grok,
                ModelId = model.ModelId,
                IsSuccess = false,
                StatusCode = 0,
                ErrorMessage = message,
                RawJson = string.Empty,
                Body = null
            };
        }

        private static RawChatStreamResult FailureChatStreamResult(ModelSpec model, string message)
        {
            return new RawChatStreamResult
            {
                Provider = AIProvider.Grok,
                ModelId = model.ModelId,
                IsSuccess = false,
                StatusCode = 0,
                ErrorMessage = message,
                RawText = string.Empty,
                Content = string.Empty
            };
        }
    }
}
