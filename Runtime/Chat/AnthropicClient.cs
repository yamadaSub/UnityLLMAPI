using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityLLMAPI.Common;
using UnityLLMAPI.Schema;

namespace UnityLLMAPI.Chat
{
    internal sealed class AnthropicClient : IProviderClient
    {
        private const string MessagesEndpoint = "https://api.anthropic.com/v1/messages";
        private const string AnthropicVersion = "2023-06-01";
        private const int DefaultMaxTokens = 4096;
        private const string StructuredOutputToolName = "structured_output";

        public AIProvider Provider => AIProvider.Anthropic;

        public async Task<RawChatResult> SendChatAsync(
            ModelSpec model,
            IReadOnlyList<Message> messages,
            ChatRequestOptions options,
            CancellationToken ct)
        {
            var apiKey = ApiKeyResolver.AnthropicApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError($"API key not configured. {ApiKeyResolver.GetRequiredEnvHint(model)}");
                return FailureChatResult(model, "Missing Anthropic API key");
            }

            var body = BuildBaseMessageBody(model, messages, options?.AdditionalBody, false);
            AddFunctions(body, options?.Functions);

            var jsonBody = JsonConvert.SerializeObject(body);
            using var req = BuildRequest(MessagesEndpoint, apiKey, jsonBody);
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
            var apiKey = ApiKeyResolver.AnthropicApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError($"API key not configured. {ApiKeyResolver.GetRequiredEnvHint(model)}");
                return FailureChatStreamResult(model, "Missing Anthropic API key");
            }

            var body = BuildBaseMessageBody(model, messages, options?.AdditionalBody, true);
            AddFunctions(body, options?.Functions);

            var content = new StringBuilder();
            var streamHandler = StreamingDownloadHandler.ForServerSentEvents(payload =>
            {
                if (string.IsNullOrEmpty(payload)) return;
                TryConsumeAnthropicStreamEvent(payload, content, onContentDelta);
            });

            var jsonBody = JsonConvert.SerializeObject(body);
            using var req = BuildStreamRequest(MessagesEndpoint, apiKey, jsonBody, streamHandler);
            await UnityWebRequestUtils.SendAsync(req, ct, options?.TimeoutSeconds ?? -1);
            streamHandler.CompleteServerSentEvents();

            var rawText = req.downloadHandler?.text;
            return new RawChatStreamResult
            {
                Provider = Provider,
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
            var apiKey = ApiKeyResolver.AnthropicApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError($"API key not configured. {ApiKeyResolver.GetRequiredEnvHint(model)}");
                return FailureChatResult(model, "Missing Anthropic API key");
            }

            var body = BuildBaseMessageBody(model, messages, options?.AdditionalBody, false);
            AddStructuredOutputTool(body, ParseSchema(jsonSchema));

            var jsonBody = JsonConvert.SerializeObject(body);
            using var req = BuildRequest(MessagesEndpoint, apiKey, jsonBody);
            await UnityWebRequestUtils.SendAsync(req, ct, options?.TimeoutSeconds ?? -1);

            return BuildRawChatResult(model, req);
        }

        public Task<RawImageResult> GenerateImageAsync(
            ModelSpec model,
            ImageGenerationRequest request,
            CancellationToken ct)
        {
            throw new System.NotSupportedException("Anthropic image generation is not implemented.");
        }

        public Task<RawEmbeddingResult> CreateEmbeddingAsync(
            ModelSpec model,
            IReadOnlyList<string> texts,
            CancellationToken ct)
        {
            throw new System.NotSupportedException("Anthropic embeddings are not implemented.");
        }

        private static Dictionary<string, object> BuildBaseMessageBody(
            ModelSpec model,
            IReadOnlyList<Message> messages,
            Dictionary<string, object> additionalBody,
            bool stream)
        {
            var messageList = messages?.ToList() ?? new List<Message>();
            var body = new Dictionary<string, object>
            {
                { "model", model.ModelId },
                { "max_tokens", DefaultMaxTokens },
                { "messages", MessagePayloadBuilder.BuildAnthropicMessages(messageList) }
            };

            var system = MessagePayloadBuilder.BuildAnthropicSystem(messageList);
            if (!string.IsNullOrEmpty(system))
            {
                body["system"] = system;
            }

            if (stream)
            {
                body["stream"] = true;
            }

            MergeAdditionalBody(body, additionalBody);
            return body;
        }

        private static void AddFunctions(Dictionary<string, object> body, IReadOnlyList<IJsonSchema> functions)
        {
            if (functions == null || functions.Count == 0) return;

            body["tools"] = functions.Select(BuildFunctionTool).ToList();
            body["tool_choice"] = new Dictionary<string, object>
            {
                { "type", "any" }
            };
        }

        private static Dictionary<string, object> BuildFunctionTool(IJsonSchema functionSchema)
        {
            var schema = functionSchema?.GenerateJsonSchema() ?? new Dictionary<string, object>();
            var name = schema.TryGetValue("name", out var nameObj) ? nameObj?.ToString() : functionSchema?.Name;
            var description = schema.TryGetValue("description", out var descObj) ? descObj?.ToString() : string.Empty;

            object inputSchema = null;
            if (schema.TryGetValue("parameters", out var parametersObj)) inputSchema = parametersObj;
            else if (schema.TryGetValue("schema", out var schemaObj)) inputSchema = schemaObj;

            return new Dictionary<string, object>
            {
                { "name", name ?? string.Empty },
                { "description", description ?? string.Empty },
                { "input_schema", inputSchema ?? new Dictionary<string, object> { { "type", "object" } } },
                { "strict", true }
            };
        }

        private static void AddStructuredOutputTool(Dictionary<string, object> body, object schema)
        {
            body["tools"] = new[]
            {
                new Dictionary<string, object>
                {
                    { "name", StructuredOutputToolName },
                    { "description", "Return the final answer as JSON that matches the schema exactly." },
                    { "input_schema", schema ?? new Dictionary<string, object> { { "type", "object" } } },
                    { "strict", true }
                }
            };

            body["tool_choice"] = new Dictionary<string, object>
            {
                { "type", "tool" },
                { "name", StructuredOutputToolName }
            };
        }

        private static void TryConsumeAnthropicStreamEvent(
            string payload,
            StringBuilder content,
            System.Action<string> onContentDelta)
        {
            try
            {
                var obj = JObject.Parse(payload);
                if ((obj["type"]?.ToString() ?? string.Empty) != "content_block_delta") return;

                var delta = obj["delta"] as JObject;
                if (delta == null) return;
                if ((delta["type"]?.ToString() ?? string.Empty) != "text_delta") return;

                var text = delta["text"]?.ToString();
                if (string.IsNullOrEmpty(text)) return;

                content?.Append(text);
                onContentDelta?.Invoke(text);
            }
            catch
            {
                // ignore malformed stream events
            }
        }

        private static UnityWebRequest BuildRequest(string endpoint, string apiKey, string jsonBody)
        {
            var req = new UnityWebRequest(endpoint, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("x-api-key", apiKey);
            req.SetRequestHeader("anthropic-version", AnthropicVersion);
            return req;
        }

        private static UnityWebRequest BuildStreamRequest(
            string endpoint,
            string apiKey,
            string jsonBody,
            StreamingDownloadHandler streamHandler)
        {
            var req = new UnityWebRequest(endpoint, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody)),
                downloadHandler = streamHandler
            };
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("x-api-key", apiKey);
            req.SetRequestHeader("anthropic-version", AnthropicVersion);
            return req;
        }

        private static void MergeAdditionalBody(Dictionary<string, object> body, Dictionary<string, object> additional)
        {
            if (body == null || additional == null) return;
            foreach (var kv in additional)
            {
                body[kv.Key] = kv.Value;
            }
        }

        private static object ParseSchema(string jsonSchema)
        {
            if (string.IsNullOrEmpty(jsonSchema)) return new Dictionary<string, object> { { "type", "object" } };

            try
            {
                var dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonSchema);
                if (dict != null && dict.TryGetValue("schema", out var schemaObj))
                {
                    return schemaObj;
                }
                return dict;
            }
            catch
            {
                return new Dictionary<string, object> { { "type", "object" } };
            }
        }

        private static RawChatResult BuildRawChatResult(ModelSpec model, UnityWebRequest req)
        {
            var rawJson = req.downloadHandler?.text;
            return new RawChatResult
            {
                Provider = AIProvider.Anthropic,
                ModelId = model.ModelId,
                IsSuccess = req.result == UnityWebRequest.Result.Success,
                StatusCode = req.responseCode,
                ErrorMessage = req.result == UnityWebRequest.Result.Success ? null : req.error,
                RawJson = rawJson,
                Body = TryParse(rawJson)
            };
        }

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

        private static RawChatResult FailureChatResult(ModelSpec model, string message)
        {
            return new RawChatResult
            {
                Provider = AIProvider.Anthropic,
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
                Provider = AIProvider.Anthropic,
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
