using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityLLMAPI.Common;
using UnityLLMAPI.Embedding;
using UnityLLMAPI.Schema;

namespace UnityLLMAPI.Chat
{
    // OpenAI 向けの HTTP 実装をまとめた ProviderClient
    internal sealed class OpenAIClient : IProviderClient
    {
        private const string ChatEndpoint = "https://api.openai.com/v1/chat/completions";
        private const string EmbeddingsEndpoint = "https://api.openai.com/v1/embeddings";

        public AIProvider Provider => AIProvider.OpenAI;

        /// <summary>
        /// OpenAI Chat Completions で通常チャットを送信する。
        /// </summary>
        public async Task<RawChatResult> SendChatAsync(
            ModelSpec model,
            IReadOnlyList<Message> messages,
            ChatRequestOptions options,
            CancellationToken ct)
        {
            var apiKey = ApiKeyResolver.OpenAIApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError($"API key not configured. {ApiKeyResolver.GetRequiredEnvHint(model)}");
                return FailureChatResult(model, "Missing OpenAI API key");
            }

            var body = new Dictionary<string, object>
            {
                { "model", model.ModelId },
                { "messages", MessagePayloadBuilder.BuildOpenAiMessages(messages?.ToList() ?? new List<Message>()) }
            };

            AddFunctions(body, options?.Functions);
            MergeAdditionalBody(body, options?.AdditionalBody);

            var jsonBody = JsonConvert.SerializeObject(body);
            using var req = BuildRequest(ChatEndpoint, apiKey, jsonBody);
            await UnityWebRequestUtils.SendAsync(req, ct, options?.TimeoutSeconds ?? -1);

            return BuildRawChatResult(model, req);
        }

        public async Task<RawChatResult> SendStructuredAsync(
            ModelSpec model,
            IReadOnlyList<Message> messages,
            string jsonSchema,
            ChatRequestOptions options,
            CancellationToken ct)
        {
            var apiKey = ApiKeyResolver.OpenAIApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError($"API key not configured. {ApiKeyResolver.GetRequiredEnvHint(model)}");
                return FailureChatResult(model, "Missing OpenAI API key");
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
            using var req = BuildRequest(ChatEndpoint, apiKey, jsonBody);
            await UnityWebRequestUtils.SendAsync(req, ct, options?.TimeoutSeconds ?? -1);

            return BuildRawChatResult(model, req);
        }

        public Task<RawImageResult> GenerateImageAsync(
            ModelSpec model,
            ImageGenerationRequest request,
            CancellationToken ct)
        {
            throw new System.NotSupportedException("OpenAI image generation is not implemented in this client.");
        }

        /// <summary>
        /// OpenAI Embeddings API を呼び出しベクトルを取得する。
        /// </summary>
        public async Task<RawEmbeddingResult> CreateEmbeddingAsync(
            ModelSpec model,
            IReadOnlyList<string> texts,
            CancellationToken ct)
        {
            var apiKey = ApiKeyResolver.OpenAIApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError($"API key not configured. {ApiKeyResolver.GetRequiredEnvHint(model)}");
                return FailureEmbeddingResult(model, "Missing OpenAI API key");
            }

            if (texts == null || texts.Count == 0)
            {
                return FailureEmbeddingResult(model, "No input text provided for embedding.");
            }

            var body = new Dictionary<string, object>
            {
                { "model", model.ModelId },
                { "input", texts }
            };

            var jsonBody = JsonConvert.SerializeObject(body);
            using var req = BuildRequest(EmbeddingsEndpoint, apiKey, jsonBody);
            await UnityWebRequestUtils.SendAsync(req, ct, -1);

            var rawJson = req.downloadHandler?.text;
            var parsed = TryParse(rawJson);

            var embeddings = new List<SerializableEmbedding>();
            if (req.result == UnityWebRequest.Result.Success && parsed != null)
            {
                try
                {
                    var dto = parsed.ToObject<OpenAIEmbeddingResponse>();
                    if (dto?.data != null)
                    {
                        foreach (var item in dto.data)
                        {
                            if (item?.embedding == null) continue;
                            var emb = new SerializableEmbedding(model.ModelId);
                            emb.SetFromFloatArray(item.embedding);
                            embeddings.Add(emb);
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError("OpenAI embedding parse error: " + ex.Message);
                }
            }

            return new RawEmbeddingResult
            {
                Provider = Provider,
                ModelId = model.ModelId,
                IsSuccess = req.result == UnityWebRequest.Result.Success,
                StatusCode = req.responseCode,
                ErrorMessage = req.result == UnityWebRequest.Result.Success ? null : req.error,
                RawJson = rawJson,
                Embeddings = embeddings
            };
        }

        /// <summary>
        /// OpenAI 向けの UnityWebRequest を生成する。
        /// </summary>
        private static UnityWebRequest BuildRequest(string endpoint, string apiKey, string jsonBody)
        {
            var bodyBytes = System.Text.Encoding.UTF8.GetBytes(jsonBody);
            var req = new UnityWebRequest(endpoint, "POST")
            {
                uploadHandler = new UploadHandlerRaw(bodyBytes),
                downloadHandler = new DownloadHandlerBuffer()
            };
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + apiKey);
            return req;
        }

        /// <summary>
        /// UnityWebRequest から共通の RawChatResult を生成する。
        /// </summary>
        private static RawChatResult BuildRawChatResult(ModelSpec model, UnityWebRequest req)
        {
            var rawJson = req.downloadHandler?.text;
            return new RawChatResult
            {
                Provider = AIProvider.OpenAI,
                ModelId = model.ModelId,
                IsSuccess = req.result == UnityWebRequest.Result.Success,
                StatusCode = req.responseCode,
                ErrorMessage = req.result == UnityWebRequest.Result.Success ? null : req.error,
                RawJson = rawJson,
                Body = TryParse(rawJson)
            };
        }

        /// <summary>
        /// 追加パラメータをボディへマージする。
        /// </summary>
        private static void MergeAdditionalBody(Dictionary<string, object> body, Dictionary<string, object> additional)
        {
            if (body == null || additional == null) return;
            foreach (var kv in additional)
            {
                body[kv.Key] = kv.Value;
            }
        }

        /// <summary>
        /// Function Calling 用のパラメータを OpenAI ボディに追加する。
        /// </summary>
        private static void AddFunctions(Dictionary<string, object> body, IReadOnlyList<IJsonSchema> functions)
        {
            if (functions == null || functions.Count == 0) return;
            body["functions"] = functions.Select(f => f.GenerateJsonSchema()).ToList();
            body["function_call"] = "auto";
        }

        /// <summary>
        /// JSON をパースして JObject を返す。失敗時は null。
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

        private static RawChatResult FailureChatResult(ModelSpec model, string message)
        {
            return new RawChatResult
            {
                Provider = AIProvider.OpenAI,
                ModelId = model.ModelId,
                IsSuccess = false,
                StatusCode = 0,
                ErrorMessage = message,
                RawJson = string.Empty,
                Body = null
            };
        }

        private static RawEmbeddingResult FailureEmbeddingResult(ModelSpec model, string message)
        {
            return new RawEmbeddingResult
            {
                Provider = AIProvider.OpenAI,
                ModelId = model.ModelId,
                IsSuccess = false,
                StatusCode = 0,
                ErrorMessage = message,
                RawJson = string.Empty,
                Embeddings = new List<SerializableEmbedding>()
            };
        }

        private class OpenAIEmbeddingResponse
        {
            public List<OpenAIEmbeddingData> data;
        }

        private class OpenAIEmbeddingData
        {
            public float[] embedding;
        }
    }
}
