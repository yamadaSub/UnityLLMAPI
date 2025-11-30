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
    // Gemini (Google) 向けの HTTP 実装をまとめた ProviderClient
    internal sealed class GeminiClient : IProviderClient
    {
        private const string ApiBase = "https://generativelanguage.googleapis.com/v1beta/models";
        private const string EmbeddingEndpoint = "https://generativelanguage.googleapis.com/v1beta/models/gemini-embedding-001:embedContent";

        public AIProvider Provider => AIProvider.Gemini;

        /// <summary>
        /// Gemini generateContent を用いて通常チャットを送信する。
        /// </summary>
        public async Task<RawChatResult> SendChatAsync(
            ModelSpec model,
            IReadOnlyList<Message> messages,
            ChatRequestOptions options,
            CancellationToken ct)
        {
            var apiKey = ApiKeyResolver.GoogleApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError($"API key not configured. {ApiKeyResolver.GetRequiredEnvHint(model)}");
                return FailureChatResult(model, "Missing Google API key");
            }

            var endpoint = $"{ApiBase}/{model.ModelId}:generateContent";
            var contents = MessagePayloadBuilder.BuildGeminiContents(messages?.ToList() ?? new List<Message>());

            var body = new Dictionary<string, object> { { "contents", contents } };
            AddFunctionDeclarations(body, options?.Functions);
            MergeAdditionalBody(body, options?.AdditionalBody);

            var jsonBody = JsonConvert.SerializeObject(body);
            using var req = BuildRequest(endpoint, apiKey, jsonBody);
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
            var apiKey = ApiKeyResolver.GoogleApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError($"API key not configured. {ApiKeyResolver.GetRequiredEnvHint(model)}");
                return FailureChatResult(model, "Missing Google API key");
            }

            var endpoint = $"{ApiBase}/{model.ModelId}:generateContent";
            var contents = MessagePayloadBuilder.BuildGeminiContents(messages?.ToList() ?? new List<Message>());

            var schemaObj = ParseSchema(jsonSchema);
            var body = new Dictionary<string, object>
            {
                { "contents", contents },
                { "generationConfig", new Dictionary<string, object>
                    {
                        { "responseMimeType", "application/json" },
                        { "responseSchema", schemaObj ?? new Dictionary<string, object>{{"type","object"}} }
                    }
                }
            };

            MergeAdditionalBody(body, options?.AdditionalBody);

            var jsonBody = JsonConvert.SerializeObject(body);
            using var req = BuildRequest(endpoint, apiKey, jsonBody);
            await UnityWebRequestUtils.SendAsync(req, ct, options?.TimeoutSeconds ?? -1);

            return BuildRawChatResult(model, req);
        }

        public async Task<RawImageResult> GenerateImageAsync(
            ModelSpec model,
            ImageGenerationRequest request,
            CancellationToken ct)
        {
            var apiKey = ApiKeyResolver.GoogleApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError($"API key not configured. {ApiKeyResolver.GetRequiredEnvHint(model)}");
                return FailureImageResult(model, "Missing Google API key");
            }

            var endpoint = $"{ApiBase}/{model.ModelId}:generateContent";
            var contents = MessagePayloadBuilder.BuildGeminiContents(request?.Messages ?? new List<Message>());

            var body = new Dictionary<string, object> { { "contents", contents } };
            MergeAdditionalBody(body, request?.AdditionalBody);
            EnsureImageGenerationConfig(body);

            var jsonBody = JsonConvert.SerializeObject(body);
            using var req = BuildRequest(endpoint, apiKey, jsonBody);
            await UnityWebRequestUtils.SendAsync(req, ct, request?.TimeoutSeconds ?? -1);

            var rawJson = req.downloadHandler?.text;
            var parsed = TryParse(rawJson);
            var images = ExtractImages(parsed);
            var promptFeedback = parsed?["promptFeedback"]?["blockReason"]?.ToString()
                                 ?? parsed?["promptFeedback"]?["block_reason"]?.ToString();

            return new RawImageResult
            {
                Provider = Provider,
                ModelId = model.ModelId,
                IsSuccess = req.result == UnityWebRequest.Result.Success,
                StatusCode = req.responseCode,
                ErrorMessage = req.result == UnityWebRequest.Result.Success ? null : req.error,
                RawJson = rawJson,
                Images = images,
                PromptFeedback = promptFeedback
            };
        }

        public async Task<RawEmbeddingResult> CreateEmbeddingAsync(
            ModelSpec model,
            IReadOnlyList<string> texts,
            CancellationToken ct)
        {
            var apiKey = ApiKeyResolver.GoogleApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError($"API key not configured. {ApiKeyResolver.GetRequiredEnvHint(model)}");
                return FailureEmbeddingResult(model, "Missing Google API key");
            }

            if (texts == null || texts.Count == 0)
            {
                return FailureEmbeddingResult(model, "No input text provided for embedding.");
            }

            var embeddings = new List<SerializableEmbedding>();
            string lastRaw = string.Empty;
            long lastStatus = 0;
            string lastError = null;

            foreach (var text in texts)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(text)) continue;

                var body = new Dictionary<string, object>
                {
                    { "model", "models/gemini-embedding-001" },
                    { "content", new Dictionary<string, object>
                        {
                            { "parts", new[]{ new Dictionary<string, string>{{ "text", text }} } }
                        }
                    }
                };

                var jsonBody = JsonConvert.SerializeObject(body);
                using var req = BuildRequest(EmbeddingEndpoint, apiKey, jsonBody);
                await UnityWebRequestUtils.SendAsync(req, ct, -1);

                lastStatus = req.responseCode;
                lastRaw = req.downloadHandler?.text;
                lastError = req.result == UnityWebRequest.Result.Success ? null : req.error;

                if (req.result != UnityWebRequest.Result.Success) continue;

                var parsed = TryParse(lastRaw);
                try
                {
                    var dto = parsed?.ToObject<GeminiEmbeddingResponse>();
                    var vec = dto?.embedding?.values;
                    if (vec != null)
                    {
                        var emb = new SerializableEmbedding(model.ModelId);
                        emb.SetFromFloatArray(vec);
                        embeddings.Add(emb);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError("Gemini embedding parse error: " + ex.Message);
                }
            }

            return new RawEmbeddingResult
            {
                Provider = Provider,
                ModelId = model.ModelId,
                IsSuccess = embeddings.Count > 0,
                StatusCode = lastStatus,
                ErrorMessage = embeddings.Count > 0 ? null : lastError,
                RawJson = lastRaw,
                Embeddings = embeddings
            };
        }

        /// <summary>
        /// Gemini 用の UnityWebRequest を生成する。
        /// </summary>
        private static UnityWebRequest BuildRequest(string endpoint, string apiKey, string jsonBody)
        {
            var req = new UnityWebRequest(endpoint, "POST")
            {
                uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonBody)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("x-goog-api-key", apiKey);
            return req;
        }

        /// <summary>
        /// ボディに追加パラメータをマージする。
        /// </summary>
        private static void MergeAdditionalBody(Dictionary<string, object> body, Dictionary<string, object> additional)
        {
            if (body == null || additional == null) return;
            foreach (var kv in additional) body[kv.Key] = kv.Value;
        }

        /// <summary>
        /// Function Calling 用の宣言を Gemini フォーマットで追加する。
        /// </summary>
        private static void AddFunctionDeclarations(Dictionary<string, object> body, IReadOnlyList<IJsonSchema> functions)
        {
            if (functions == null || functions.Count == 0) return;
            var functionDeclarations = new List<Dictionary<string, object>>();
            foreach (var f in functions)
            {
                var schema = f.GenerateJsonSchema();
                var name = schema.ContainsKey("name") ? schema["name"]?.ToString() : f.Name;
                var description = schema.ContainsKey("description") ? schema["description"]?.ToString() : string.Empty;
                object parameters = null;
                if (schema.ContainsKey("parameters")) parameters = schema["parameters"];
                else if (schema.ContainsKey("schema")) parameters = schema["schema"];

                parameters = MessagePayloadBuilder.SanitizeGeminiParameters(parameters);

                functionDeclarations.Add(new Dictionary<string, object>
                {
                    { "name", name },
                    { "description", description ?? string.Empty },
                    { "parameters", parameters ?? new Dictionary<string, object>{{"type","object"}} }
                });
            }

            body["tools"] = new[]
            {
                new Dictionary<string, object> { { "function_declarations", functionDeclarations } }
            };
            body["tool_config"] = new Dictionary<string, object>
            {
                {
                    "function_calling_config",
                    new Dictionary<string, object> { { "mode", "AUTO" } }
                }
            };
        }

        /// <summary>
        /// 画像生成リクエストに必要な generationConfig を補完する。
        /// </summary>
        private static void EnsureImageGenerationConfig(Dictionary<string, object> body)
        {
            if (body == null) return;
            if (body.ContainsKey("generationConfig")) return;
            body["generationConfig"] = new Dictionary<string, object>
            {
                { "responseModalities", new [] { "IMAGE" } }
            };
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

        /// <summary>
        /// UnityWebRequest のレスポンスから RawChatResult を生成する。
        /// </summary>
        private static RawChatResult BuildRawChatResult(ModelSpec model, UnityWebRequest req)
        {
            var rawJson = req.downloadHandler?.text;
            return new RawChatResult
            {
                Provider = AIProvider.Gemini,
                ModelId = model.ModelId,
                IsSuccess = req.result == UnityWebRequest.Result.Success,
                StatusCode = req.responseCode,
                ErrorMessage = req.result == UnityWebRequest.Result.Success ? null : req.error,
                RawJson = rawJson,
                Body = TryParse(rawJson)
            };
        }

        /// <summary>
        /// 即時失敗用の RawChatResult を作るヘルパー。
        /// </summary>
        private static RawChatResult FailureChatResult(ModelSpec model, string message)
        {
            return new RawChatResult
            {
                Provider = AIProvider.Gemini,
                ModelId = model.ModelId,
                IsSuccess = false,
                StatusCode = 0,
                ErrorMessage = message,
                RawJson = string.Empty,
                Body = null
            };
        }

        /// <summary>
        /// 画像生成が未対応の場合の失敗レスポンスを生成する。
        /// </summary>
        private static RawImageResult FailureImageResult(ModelSpec model, string message)
        {
            return new RawImageResult
            {
                Provider = AIProvider.Gemini,
                ModelId = model.ModelId,
                IsSuccess = false,
                StatusCode = 0,
                ErrorMessage = message,
                RawJson = string.Empty,
                Images = new List<GeneratedImage>()
            };
        }

        /// <summary>
        /// 埋め込み生成が未対応の場合の失敗レスポンスを生成する。
        /// </summary>
        private static RawEmbeddingResult FailureEmbeddingResult(ModelSpec model, string message)
        {
            return new RawEmbeddingResult
            {
                Provider = AIProvider.Gemini,
                ModelId = model.ModelId,
                IsSuccess = false,
                StatusCode = 0,
                ErrorMessage = message,
                RawJson = string.Empty,
                Embeddings = new List<SerializableEmbedding>()
            };
        }

        /// <summary>
        /// 画像レスポンスから生成された画像リストを抽出する。
        /// </summary>
        private static List<GeneratedImage> ExtractImages(JObject root)
        {
            var images = new List<GeneratedImage>();
            if (root == null) return images;

            var generated = root?["generatedImages"] as JArray
                            ?? root?["generated_images"] as JArray;
            if (generated != null)
            {
                foreach (var entry in generated)
                {
                    var imageNode = entry?["image"] ?? entry?["inlineData"] ?? entry?["inline_data"];
                    AppendImage(images, imageNode);
                }
                return images;
            }

            var parts = root?["candidates"]?[0]?["content"]?["parts"] as JArray;
            if (parts != null)
            {
                foreach (var part in parts)
                {
                    var inline = part?["inline_data"] ?? part?["inlineData"];
                    AppendImage(images, inline);
                }
            }

            return images;
        }

        /// <summary>
        /// 単一画像トークンをパースし GeneratedImage として追加する。
        /// </summary>
        private static void AppendImage(ICollection<GeneratedImage> images, JToken imageNode)
        {
            if (imageNode == null) return;
            var mime = imageNode["mimeType"]?.ToString() ?? imageNode["mime_type"]?.ToString() ?? "image/png";
            var dataString = imageNode["data"]?.ToString();
            if (string.IsNullOrEmpty(dataString)) return;
            try
            {
                images.Add(new GeneratedImage
                {
                    mimeType = mime,
                    data = System.Convert.FromBase64String(dataString)
                });
            }
            catch
            {
                // ignore bad image data
            }
        }

        /// <summary>
        /// 構造化応答用の JSON Schema を解釈して返す。
        /// </summary>
        private static object ParseSchema(string jsonSchema)
        {
            if (string.IsNullOrEmpty(jsonSchema)) return new Dictionary<string, object> { { "type", "object" } };
            try
            {
                var dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonSchema);
                if (dict != null && dict.ContainsKey("schema"))
                {
                    return dict["schema"];
                }
                return dict;
            }
            catch
            {
                return new Dictionary<string, object> { { "type", "object" } };
            }
        }

        private class GeminiEmbeddingResponse
        {
            public GeminiEmbedding embedding;
        }

        private class GeminiEmbedding
        {
            public float[] values;
        }
    }
}
