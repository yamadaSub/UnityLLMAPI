using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Threading.Tasks; 

#region 共通クラス・Enum

/// <summary>
/// サポートするモデル：GPT と Grok のみ
/// </summary>
public enum AIModelType
{
    GPT4o,
    GPT5,
    GPT5Mini,
    GPT5Pro,
    Grok2,
    Grok3,
    Gemini25,
    Gemini25Pro,
    Gemini25Flash,
    Gemini25FlashLite,
    Gemini25FlashImagePreview,
    Gemini3,
    Gemini3ProImagePreview
}

/// <summary>
/// メッセージの役割（role）を示す Enum
/// ※シリアライズ時に小文字（"user", "assistant", "system"）に変換します
/// </summary>
[JsonConverter(typeof(StringEnumConverter), true)]
public enum MessageRole
{
    User,
    Assistant,
    System
}

/// <summary>
/// 役割とメッセージ内容を保持するシリアライズ可能な Message クラス
/// </summary>
[Serializable]
public class Message
{
    public MessageRole role;
    [TextArea(1, 10)]
    public string content;
    public List<MessageContent> parts;

    internal IEnumerable<MessageContent> EnumerateParts()
    {
        if (parts != null && parts.Count > 0)
        {
            foreach (var part in parts)
            {
                if (part == null) continue;
                yield return part;
            }
            yield break;
        }

        if (!string.IsNullOrEmpty(content))
        {
            yield return MessageContent.FromText(content);
        }
    }
}

#endregion

#region Message Parts

public enum MessageContentType
{
    Text,
    ImageUrl,
    ImageData
}

[Serializable]
public class MessageContent
{
    public MessageContentType type = MessageContentType.Text;
    [TextArea(1, 10)]
    public string text;
    public string uri;
    public byte[] data;
    public string mimeType = "image/png";

    public bool HasData => data != null && data.Length > 0;

    public static MessageContent FromText(string value)
        => new MessageContent { type = MessageContentType.Text, text = value };

    public static MessageContent FromImageUrl(string url, string mime = null)
        => new MessageContent
        {
            type = MessageContentType.ImageUrl,
            uri = url,
            mimeType = mime
        };

    public static MessageContent FromImageData(byte[] bytes, string mime = "image/png")
        => new MessageContent
        {
            type = MessageContentType.ImageData,
            data = bytes,
            mimeType = string.IsNullOrEmpty(mime) ? "image/png" : mime
        };

    /// <summary>
    /// Unity の Texture から直接 MessageContent を生成するヘルパー。
    /// 非 readable な Texture2D が渡された場合でも、GPU 読み戻しにより PNG 化を試みる。
    /// </summary>
    public static MessageContent FromImage(Texture texture, string mime = "image/png", bool allowGpuReadback = true, bool logWarnings = true)
    {
        if (texture == null)
        {
            if (logWarnings) Debug.LogWarning("MessageContent.FromImage: texture is null.");
            return null;
        }

        if (!TextureEncodingUtility.TryGetPngBytes(texture, out var png, allowGpuReadback, logWarnings))
        {
            if (logWarnings) Debug.LogWarning("MessageContent.FromImage: Failed to encode texture to PNG.");
            return null;
        }

        return FromImageData(png, mime);
    }
}

#region Image Responses

[Serializable]
public class GeneratedImage
{
    public string mimeType;
    public byte[] data;

    public string ToBase64() => data != null && data.Length > 0 ? Convert.ToBase64String(data) : string.Empty;

    public string ToDataUrl()
    {
        if (data == null || data.Length == 0) return string.Empty;
        var mime = string.IsNullOrEmpty(mimeType) ? "image/png" : mimeType;
        return $"data:{mime};base64,{Convert.ToBase64String(data)}";
    }
}

[Serializable]
public class ImageGenerationResponse
{
    public List<GeneratedImage> images = new List<GeneratedImage>();
    public string promptFeedback;
    public string rawJson;
}

#endregion

#endregion

#region Function 呼び出し関連

public class FunctionSchema<T> : RealTimeJsonSchema<T> where T : SchemaParameter
{
    /// <summary>
    /// 関数の説明文。GenerateJsonSchema で function.description として使用される。
    /// </summary>
    public string Description { get; set; } = string.Empty;
    public FunctionSchema(string schemaName) : base(schemaName)
    {
    }
    public override Dictionary<string, object> GenerateJsonSchema(Func<T, bool> filter = null)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();
        foreach (var param in Parameters)
        {
            properties[param.ParameterName] = param.ToJsonSchemaPiece();
            if (param.Required)
            {
                required.Add(param.ParameterName);
            }
        }

        var schema = new Dictionary<string, object>{
            { "type", "function" },
            { "name",Name },
            { "description", Description },
            { "parameters", new Dictionary<string, object>{
                { "type", "object" },
                { "properties", properties },
                { "required", required }
            }}};
        return schema;
    }
}

#endregion

#region AIManager

/// <summary>
/// AI API 呼び出し用シングルトンマネージャ
/// 会話履歴（Message のリスト）を元にリクエストを構築し、
/// ・通常応答
/// ・構造化出力（自動生成した JSON Schema を利用）
/// ・Function Calling（型安全な IFunction リストを渡して自動実行）
/// を実現します。
/// </summary>
public static class AIManager
{
    private static AIManagerBehaviour cachedBehaviour;

    // Note: Do NOT serialize API keys. Keys are resolved at runtime from
    // environment variables or editor-only storage to avoid persisting secrets.
    public static string OpenAIApiKey => ResolveApiKey(b => b.OpenAIApiKey, new[] { "OPENAI_API_KEY" });
    public static string GrokApiKey   => ResolveApiKey(b => b.GrokApiKey,   new[] { "GROK_API_KEY" });
    public static string GoogleApiKey => ResolveApiKey(b => b.GoogleApiKey, new[] { "GOOGLE_API_KEY" });

    private const string openAiEndpoint = "https://api.openai.com/v1/chat/completions";
    private const string grokEndpoint = "https://api.x.ai/v1/chat/completions";
    private const string geminiApiBase = "https://generativelanguage.googleapis.com/v1beta/models";
#if UNITY_EDITOR
    private const string editorIgnoreKeysConfig = "UnityLLMAPI.IGNORE_EDITOR_KEYS";
#endif

    internal static void RegisterBehaviour(AIManagerBehaviour behaviour)
    {
        if (behaviour == null) return;
        cachedBehaviour = behaviour;
    }

    internal static void UnregisterBehaviour(AIManagerBehaviour behaviour)
    {
        if (cachedBehaviour == behaviour)
        {
            cachedBehaviour = null;
        }
    }

    private static AIManagerBehaviour GetBehaviour()
    {
        if (cachedBehaviour != null)
        {
            return cachedBehaviour;
        }

#if UNITY_2023_1_OR_NEWER
        cachedBehaviour = UnityEngine.Object.FindFirstObjectByType<AIManagerBehaviour>();
#else
        cachedBehaviour = UnityEngine.Object.FindObjectOfType<AIManagerBehaviour>();
#endif
        return cachedBehaviour;
    }

    #region Message Serialization Helpers

    private static List<Dictionary<string, object>> BuildOpenAiMessages(List<Message> messages)
    {
        var result = new List<Dictionary<string, object>>();
        if (messages == null) return result;

        foreach (var message in messages)
        {
            if (message == null) continue;
            var payload = new Dictionary<string, object>
            {
                { "role", message.role.ToString().ToLowerInvariant() }
            };

            var parts = BuildOpenAiContentParts(message);
            if (parts.Count > 0)
            {
                payload["content"] = parts;
            }
            else
            {
                payload["content"] = message.content ?? string.Empty;
            }

            result.Add(payload);
        }

        return result;
    }

    private static List<Dictionary<string, object>> BuildOpenAiContentParts(Message message)
    {
        var parts = new List<Dictionary<string, object>>();
        if (message == null) return parts;

        foreach (var part in message.EnumerateParts())
        {
            switch (part.type)
            {
                case MessageContentType.Text:
                    parts.Add(new Dictionary<string, object>
                    {
                        { "type", "text" },
                        { "text", part.text ?? string.Empty }
                    });
                    break;
                case MessageContentType.ImageUrl:
                    {
                        var url = part.uri;
                        if (string.IsNullOrWhiteSpace(url)) break;
                        parts.Add(new Dictionary<string, object>
                        {
                            { "type", "image_url" },
                            { "image_url", new Dictionary<string, object> { { "url", url } } }
                        });
                        break;
                    }
                case MessageContentType.ImageData:
                    {
                        var dataUrl = ConvertImageContentToDataUrl(part);
                        if (string.IsNullOrEmpty(dataUrl)) break;
                        parts.Add(new Dictionary<string, object>
                        {
                            { "type", "image_url" },
                            { "image_url", new Dictionary<string, object> { { "url", dataUrl } } }
                        });
                        break;
                    }
            }
        }

        return parts;
    }

    private static List<Dictionary<string, object>> BuildGeminiContents(List<Message> messages)
    {
        var contents = new List<Dictionary<string, object>>();
        if (messages == null) return contents;

        foreach (var message in messages)
        {
            if (message == null) continue;
            var parts = BuildGeminiParts(message);
            if (parts.Count == 0)
            {
                parts.Add(new Dictionary<string, object> { { "text", message.content ?? string.Empty } });
            }

            var role = message.role == MessageRole.Assistant ? "model" : "user";
            contents.Add(new Dictionary<string, object>
            {
                { "role", role },
                { "parts", parts }
            });
        }

        return contents;
    }

    private static List<object> BuildGeminiParts(Message message)
    {
        var parts = new List<object>();
        if (message == null) return parts;

        foreach (var part in message.EnumerateParts())
        {
            switch (part.type)
            {
                case MessageContentType.Text:
                    parts.Add(new Dictionary<string, object> { { "text", part.text ?? string.Empty } });
                    break;
                case MessageContentType.ImageUrl:
                    {
                        var url = part.uri;
                        if (string.IsNullOrWhiteSpace(url)) break;

                        if (TryParseDataUrl(url, out var mimeType, out var base64Data))
                        {
                            parts.Add(new Dictionary<string, object>
                            {
                                { "inline_data", new Dictionary<string, object>
                                    {
                                        { "mime_type", !string.IsNullOrEmpty(mimeType) ? mimeType : (part.mimeType ?? "image/png") },
                                        { "data", base64Data }
                                    }
                                }
                            });
                            break;
                        }

                        var fileData = new Dictionary<string, object> { { "file_uri", url } };
                        if (!string.IsNullOrEmpty(part.mimeType))
                        {
                            fileData["mime_type"] = part.mimeType;
                        }
                        parts.Add(new Dictionary<string, object> { { "file_data", fileData } });
                        break;
                    }
                case MessageContentType.ImageData:
                    {
                        if (!part.HasData) break;
                        parts.Add(new Dictionary<string, object>
                        {
                            { "inline_data", new Dictionary<string, object>
                                {
                                    { "mime_type", string.IsNullOrEmpty(part.mimeType) ? "image/png" : part.mimeType },
                                    { "data", Convert.ToBase64String(part.data) }
                                }
                            }
                        });
                        break;
                    }
            }
        }

        return parts;
    }

    private static string ConvertImageContentToDataUrl(MessageContent part)
    {
        if (part == null || !part.HasData) return string.Empty;
        var mime = string.IsNullOrEmpty(part.mimeType) ? "image/png" : part.mimeType;
        return $"data:{mime};base64,{Convert.ToBase64String(part.data)}";
    }

    private static bool TryParseDataUrl(string uri, out string mimeType, out string base64Data)
    {
        mimeType = null;
        base64Data = null;

        if (string.IsNullOrEmpty(uri) || !uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var commaIndex = uri.IndexOf(',');
        if (commaIndex < 0 || commaIndex + 1 >= uri.Length)
        {
            return false;
        }

        var metadata = uri.Substring(5, commaIndex - 5);
        base64Data = uri.Substring(commaIndex + 1);

        if (string.IsNullOrEmpty(base64Data))
        {
            return false;
        }

        var semicolonIndex = metadata.IndexOf(';');
        if (semicolonIndex >= 0)
        {
            mimeType = metadata.Substring(0, semicolonIndex);
            var suffix = metadata.Substring(semicolonIndex + 1);
            if (!suffix.Contains("base64", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        else
        {
            mimeType = metadata;
        }

        return true;
    }

    #endregion

    private static string ResolveApiKey(Func<AIManagerBehaviour, string> behaviourSelector, string[] envKeys)
    {
        var behaviour = GetBehaviour();
        if (behaviour != null)
        {
            var behaviourValue = behaviourSelector?.Invoke(behaviour);
            if (!string.IsNullOrEmpty(behaviourValue))
            {
                return behaviourValue;
            }
        }

#if UNITY_EDITOR
        foreach (var key in envKeys)
        {
            var stored = GetEditorStoredKey(key);
            if (!string.IsNullOrEmpty(stored))
            {
                return stored;
            }
        }
#endif

        foreach (var key in envKeys)
        {
            try
            {
                var v = Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.Process);
                if (!string.IsNullOrEmpty(v)) return v;
                v = Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.User);
                if (!string.IsNullOrEmpty(v)) return v;
                v = Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.Machine);
                if (!string.IsNullOrEmpty(v)) return v;
            }
            catch { /* ignore */ }
        }

        return string.Empty;
    }

#if UNITY_EDITOR
    private static string GetEditorStoredKey(string envKey)
    {
        var editorKey = $"UnityLLMAPI.{envKey}";
        try
        {
            if (!ShouldUseEditorStoredKeys())
            {
                return string.Empty;
            }

            var value = UnityEditor.EditorUserSettings.GetConfigValue(editorKey);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }
        catch
        {
            // ignored
        }

        return string.Empty;
    }
#endif

#if UNITY_EDITOR
    private static bool ShouldUseEditorStoredKeys()
    {
        try
        {
            var flag = UnityEditor.EditorUserSettings.GetConfigValue(editorIgnoreKeysConfig);
            if (!string.IsNullOrEmpty(flag))
            {
                if (flag == "1") return false;
                if (flag == "0") return true;
                if (bool.TryParse(flag, out var parsed)) return !parsed;
            }
        }
        catch
        {
            // ignored
        }
        return true;
    }

#endif

    private static (string endpoint, string apiKey) GetEndpointAndApiKey(AIModelType model)
    {
        switch (model)
        {
            case AIModelType.GPT4o:
            case AIModelType.GPT5:
            case AIModelType.GPT5Mini:
            case AIModelType.GPT5Pro:
                return (openAiEndpoint, OpenAIApiKey);
            case AIModelType.Grok2:
            case AIModelType.Grok3:
                return (grokEndpoint, GrokApiKey);
            case AIModelType.Gemini25:
            case AIModelType.Gemini25Pro:
            case AIModelType.Gemini25Flash:
            case AIModelType.Gemini25FlashLite:
            case AIModelType.Gemini25FlashImagePreview:
            case AIModelType.Gemini3:
            case AIModelType.Gemini3ProImagePreview:
                return (geminiApiBase, GoogleApiKey);
            default:
                return (openAiEndpoint, OpenAIApiKey);
        }
    }

    private static string GetRequiredEnvHint(AIModelType model)
    {
        switch (model)
        {
            case AIModelType.GPT4o:
                return "Provide an OpenAI API key via AIManagerBehaviour, UnityLLMAPI.OPENAI_API_KEY (EditorUserSettings), or the OPENAI_API_KEY environment variable.";
            case AIModelType.Grok2:
            case AIModelType.Grok3:
                return "Provide a Grok API key via AIManagerBehaviour, UnityLLMAPI.GROK_API_KEY (EditorUserSettings), or the GROK_API_KEY environment variable.";
            case AIModelType.Gemini25:
            case AIModelType.Gemini25Pro:
            case AIModelType.Gemini25Flash:
            case AIModelType.Gemini25FlashLite:
            case AIModelType.Gemini25FlashImagePreview:
            case AIModelType.Gemini3:
            case AIModelType.Gemini3ProImagePreview:
                return "Provide a Google API key via AIManagerBehaviour, UnityLLMAPI.GOOGLE_API_KEY (EditorUserSettings), or the GOOGLE_API_KEY environment variable.";
            default:
                return "Configure the matching API key on AIManagerBehaviour, in EditorUserSettings (UnityLLMAPI.*), or via environment variables.";
        }
    }

    private static string GetModelName(AIModelType model)
    {
        switch (model)
        {
            case AIModelType.GPT4o:
                return "gpt-4o";
            case AIModelType.GPT5:
                return "gpt-5";
            case AIModelType.GPT5Mini:
                return "gpt-5-mini";
            case AIModelType.GPT5Pro:
                return "gpt-5-pro";
            case AIModelType.Grok2:
                return "grok-2-latest";
            case AIModelType.Grok3:
                return "grok-3-latest";
            case AIModelType.Gemini25:
            case AIModelType.Gemini25Pro:
                return "gemini-2.5-pro";
            case AIModelType.Gemini25Flash:
                return "gemini-2.5-flash";
            case AIModelType.Gemini25FlashLite:
                return "gemini-2.5-flash-lite";
            case AIModelType.Gemini25FlashImagePreview:
                return "gemini-2.5-flash-image-preview";
            case AIModelType.Gemini3:
                return "gemini-3.0-pro-exp";
            case AIModelType.Gemini3ProImagePreview:
                return "gemini-3-pro-image-preview";
            default:
                return "gpt-4o";
        }
    }

    private static bool IsGeminiModel(AIModelType model)
    {
        return model == AIModelType.Gemini25 ||
               model == AIModelType.Gemini25Pro ||
               model == AIModelType.Gemini25Flash ||
               model == AIModelType.Gemini25FlashLite ||
               model == AIModelType.Gemini25FlashImagePreview ||
               model == AIModelType.Gemini3 ||
               model == AIModelType.Gemini3ProImagePreview;
    }

    #region Task 化した各メソッド

    /// <summary>
    /// 会話履歴（Message のリスト）を送信し、テキスト応答を非同期に取得します。
    /// エラー時は null を返します。
    /// </summary>
    public static async Task<string> SendMessageAsync(List<Message> messages, AIModelType model, Dictionary<string, object> initBody=null)
    {
        if (model == AIModelType.Gemini25FlashImagePreview || model == AIModelType.Gemini3ProImagePreview)
        {
            Debug.LogError($"{model} is image-generation focused. Use AIManager.GenerateImageAsync or GenerateImagesAsync for this model.");
            return null;
        }

        if (IsGeminiModel(model))
        {
            var modelNameGemini = GetModelName(model);
            return await SendMessageAsyncGemini(messages, modelNameGemini, initBody);
        }
        var (endpoint, apiKey) = GetEndpointAndApiKey(model);
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError($"API key not configured. {GetRequiredEnvHint(model)}");
            return null;
        }
        string modelName = GetModelName(model);

        var body = new Dictionary<string, object>
        {
            { "model", modelName },
            { "messages", BuildOpenAiMessages(messages) }
        };
        if(initBody != null)
        {
            foreach (var key in initBody.Keys)
            {
                body.Add(key, initBody[key]);
            }
        }

        string jsonBody = JsonConvert.SerializeObject(body);

        using (UnityWebRequest request = new UnityWebRequest(endpoint, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            await UnityWebRequestUtils.SendAsync(request);

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Error: " + request.error);
                return null;
            }
            else
            {
                string resultJson = request.downloadHandler.text;
                try
                {
                    var resultDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(resultJson);
                    if (resultDict != null && resultDict.ContainsKey("choices"))
                    {
                        var choices = resultDict["choices"] as Newtonsoft.Json.Linq.JArray;
                        if (choices != null && choices.Count > 0)
                        {
                            var firstChoice = choices[0] as Newtonsoft.Json.Linq.JObject;
                            if (firstChoice?["message"]?["content"] != null)
                            {
                                string content = firstChoice["message"]["content"].ToString();
                                return content;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError("JSON Parsing error: " + ex.Message);
                }
                return null;
            }
        }
    }

    private static async Task<string> SendMessageAsyncGemini(List<Message> messages, string modelName, Dictionary<string, object> initBody)
    {
        var (_, apiKey) = GetEndpointAndApiKey(AIModelType.Gemini25);
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError($"API key not configured. {GetRequiredEnvHint(AIModelType.Gemini25)}");
            return null;
        }
        var endpoint = $"{geminiApiBase}/{modelName}:generateContent";

        var contents = BuildGeminiContents(messages);

        var body = new Dictionary<string, object>{{"contents", contents}};
        if (initBody != null)
        {
            foreach (var kv in initBody) body[kv.Key] = kv.Value;
        }
        var jsonBody = JsonConvert.SerializeObject(body);

        using (var req = new UnityWebRequest(endpoint, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonBody));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("x-goog-api-key", apiKey);

            await UnityWebRequestUtils.SendAsync(req);
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Gemini Error: {req.error} \n{req.downloadHandler?.text}");
                return null;
            }
            try
            {
                var text = req.downloadHandler.text;
                var root = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(text);
                var first = root?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();
                return first ?? string.Empty;
            }
            catch (Exception ex)
            {
                Debug.LogError("Gemini JSON parse error: " + ex.Message);
                return null;
            }
        }
    }

    /// <summary>
    /// Gemini 2.5 Flash Image Preview / Gemini 3 Pro Image Preview を用いて画像生成（および画像編集）を行う。
    /// プロンプトにはテキスト・画像パートのいずれも含めることができる。
    /// </summary>
    /// <param name="messages">ユーザー／システムメッセージのリスト。画像編集時は画像パートを含める。</param>
    /// <param name="model">利用するモデル。現在は Gemini 2.5 Flash Image Preview / Gemini 3 Pro Image Preview に対応。</param>
    /// <param name="initBody">generationConfig 等の追加パラメーターを付与したい場合に使用。</param>
    /// <returns>生成された画像群と元レスポンス JSON を格納した <see cref="ImageGenerationResponse"/>。</returns>
    public static async Task<ImageGenerationResponse> GenerateImagesAsync(
        List<Message> messages,
        AIModelType model = AIModelType.Gemini25FlashImagePreview,
        Dictionary<string, object> initBody = null)
    {
        if (model != AIModelType.Gemini25FlashImagePreview && model != AIModelType.Gemini3ProImagePreview)
        {
            Debug.LogError($"GenerateImagesAsync currently supports only Gemini25FlashImagePreview and Gemini3ProImagePreview. Requested model '{model}' is not yet implemented.");
            return null;
        }

        var (baseEndpoint, apiKey) = GetEndpointAndApiKey(model);
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError($"API key not configured. {GetRequiredEnvHint(model)}");
            return null;
        }

        var modelName = GetModelName(model);
        var endpoint = $"{baseEndpoint}/{modelName}:generateContent";

        // Gemini リクエスト形式に合わせて Role/Parts を構築
        var contents = BuildGeminiContents(messages);
        if (contents.Count == 0)
        {
            Debug.LogWarning("GenerateImagesAsync called without any prompt content. Provide at least one message with text or image parts.");
        }

        var body = new Dictionary<string, object>
        {
            { "contents", contents }
        };

        if (initBody != null)
        {
            foreach (var kv in initBody)
            {
                body[kv.Key] = kv.Value;
            }
        }

        if (!body.ContainsKey("generationConfig"))
        {
            body["generationConfig"] = new Dictionary<string, object>
            {
                { "responseModalities", new [] { "IMAGE" } }
            };
        }

        var jsonBody = JsonConvert.SerializeObject(body);
        using var req = new UnityWebRequest(endpoint, "POST")
        {
            uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonBody)),
            downloadHandler = new DownloadHandlerBuffer()
        };
        req.SetRequestHeader("Content-Type", "application/json");
        req.SetRequestHeader("x-goog-api-key", apiKey);

        await UnityWebRequestUtils.SendAsync(req);
        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"Gemini image generation error: {req.error}\n{req.downloadHandler?.text}");
            return null;
        }

        try
        {
            var text = req.downloadHandler.text;
            var root = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(text);
            var response = new ImageGenerationResponse
            {
                rawJson = text
            };

            // 新しいプレビュー API は generatedImages / generated_images のどちらかに結果を返す
            var generated = root?["generatedImages"] as Newtonsoft.Json.Linq.JArray
                            ?? root?["generated_images"] as Newtonsoft.Json.Linq.JArray;
            if (generated != null)
            {
                foreach (var entry in generated)
                {
                    var imageNode = entry?["image"] ?? entry?["inlineData"] ?? entry?["inline_data"];
                    if (imageNode == null) continue;
                    var mime = imageNode["mimeType"]?.ToString() ?? imageNode["mime_type"]?.ToString() ?? "image/png";
                    var dataString = imageNode["data"]?.ToString();
                    if (string.IsNullOrEmpty(dataString)) continue;
                    try
                    {
                        response.images.Add(new GeneratedImage
                        {
                            mimeType = mime,
                            data = Convert.FromBase64String(dataString)
                        });
                    }
                    catch (FormatException)
                    {
                        Debug.LogWarning("Failed to decode image data from Gemini response.");
                    }
                }
            }
            else
            {
                // 上記フィールドが無い場合は candidates[].content.parts[].inline_data として返却される
                var parts = root?["candidates"]?[0]?["content"]?["parts"] as Newtonsoft.Json.Linq.JArray;
                if (parts != null)
                {
                    foreach (var part in parts)
                    {
                        var inline = part?["inline_data"] ?? part?["inlineData"];
                        if (inline == null) continue;
                        var mime = inline["mime_type"]?.ToString() ?? inline["mimeType"]?.ToString() ?? "image/png";
                        var dataString = inline["data"]?.ToString();
                        if (string.IsNullOrEmpty(dataString)) continue;
                        try
                        {
                            response.images.Add(new GeneratedImage
                            {
                                mimeType = mime,
                                data = Convert.FromBase64String(dataString)
                            });
                        }
                        catch (FormatException)
                        {
                            Debug.LogWarning("Failed to decode inline image data from Gemini response.");
                        }
                    }
                }
            }

            var promptFeedback = root?["promptFeedback"]?["blockReason"]?.ToString()
                                  ?? root?["promptFeedback"]?["block_reason"]?.ToString();
            response.promptFeedback = promptFeedback;

            return response;
        }
        catch (Exception ex)
        {
            Debug.LogError("Gemini image parse error: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Convenience wrapper that returns the first generated image (if any).
    /// </summary>
    public static async Task<GeneratedImage> GenerateImageAsync(
        List<Message> messages,
        AIModelType model = AIModelType.Gemini25FlashImagePreview,
        Dictionary<string, object> initBody = null)
    {
        var response = await GenerateImagesAsync(messages, model, initBody);
        return response?.images?.FirstOrDefault();
    }

    /// <summary>
    /// 会話履歴を送信し、構造化出力モードで型パラメータ T に基づく応答を非同期に取得します。
    /// エラー時は default(T) を返します。
    /// </summary>
    public static async Task<T> SendStructuredMessageAsync<T>(List<Message> messages, AIModelType model, Dictionary<string, object> initBody = null)
    {
        string content = await SendStructuredMessageAsyncCore<T>(messages, model, initBody);
        if (string.IsNullOrEmpty(content))
        {
            return default;
        }
        T structuredResult = JsonConvert.DeserializeObject<T>(content);
        return structuredResult;
    }

    public static async Task SendStructuredMessageAsync<T>(T targetInstance, List<Message> messages, AIModelType model, Dictionary<string, object> initBody = null)
    {
        if (targetInstance == null)
        {
            throw new ArgumentNullException(nameof(targetInstance), "targetInstance cannot be null.");
        }

        string content = await SendStructuredMessageAsyncCore<T>(messages, model, initBody);
        if (!string.IsNullOrEmpty(content))
        {
            JsonConvert.PopulateObject(content, targetInstance);
        }
    }

    /// <summary>
    /// 会話履歴を送信し、構造化出力モードで型パラメータ T に基づく応答を非同期に取得します。
    /// </summary>
    private static async Task<string> SendStructuredMessageAsyncCore<T>(List<Message> messages, AIModelType model, Dictionary<string, object> initBody = null)
    {
        if (IsGeminiModel(model))
        {
            var modelNameGemini = GetModelName(model);
            return await SendStructuredMessageAsyncCoreGemini<T>(messages, modelNameGemini, initBody);
        }
        var (endpoint, apiKey) = GetEndpointAndApiKey(model);
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError($"API key not configured. {GetRequiredEnvHint(model)}");
            return null;
        }
        string modelName = GetModelName(model);

        // 型 T から JSON Schema を自動生成
        var jsonSchema = JsonSchemaGenerator.GenerateSchema<T>();

        var body = new Dictionary<string, object>
        {
            { "model", modelName },
            { "messages", BuildOpenAiMessages(messages) },
            { "response_format", new Dictionary<string, object>
                {
                    { "type", "json_schema" },
                    { "json_schema", jsonSchema }
                }
            }
        };
        if (initBody != null)
        {
            foreach (var key in initBody.Keys)
            {
                body.Add(key, initBody[key]);
            }
        }
        string jsonBody = JsonConvert.SerializeObject(body);

        using (UnityWebRequest request = new UnityWebRequest(endpoint, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            await UnityWebRequestUtils.SendAsync(request);

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Error: " + request.error);
                return null;
            }
            else
            {
                string resultJson = request.downloadHandler.text;
                try
                {
                    var resultDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(resultJson);
                    if (resultDict != null && resultDict.ContainsKey("choices"))
                    {
                        var choices = resultDict["choices"] as Newtonsoft.Json.Linq.JArray;
                        if (choices != null && choices.Count > 0)
                        {
                            var firstChoice = choices[0] as Newtonsoft.Json.Linq.JObject;
                            if (firstChoice?["message"]?["content"] != null)
                            {
                                string content = firstChoice["message"]["content"].ToString();
                                return content;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError("JSON Parsing error: " + ex.Message);
                }
                return null;
            }
        }
    }

    private static async Task<string> SendStructuredMessageAsyncCoreGemini<T>(List<Message> messages, string modelName, Dictionary<string, object> initBody)
    {
        var (_, apiKey) = GetEndpointAndApiKey(AIModelType.Gemini25);
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError($"API key not configured. {GetRequiredEnvHint(AIModelType.Gemini25)}");
            return null;
        }
        var endpoint = $"{geminiApiBase}/{modelName}:generateContent";

        var contents = BuildGeminiContents(messages);

        var schemaWrap = JsonSchemaGenerator.GenerateSchema<T>();
        object responseSchema = schemaWrap != null && schemaWrap.ContainsKey("schema") ? schemaWrap["schema"] : null;

        var body = new Dictionary<string, object>
        {
            { "contents", contents },
            { "generationConfig", new Dictionary<string, object>
                {
                    { "responseMimeType", "application/json" },
                    { "responseSchema", responseSchema ?? new Dictionary<string, object>{{"type","object"}} }
                }
            }
        };
        if (initBody != null)
        {
            foreach (var kv in initBody) body[kv.Key] = kv.Value;
        }

        var jsonBody = JsonConvert.SerializeObject(body);
        using (var req = new UnityWebRequest(endpoint, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonBody));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("x-goog-api-key", apiKey);

            await UnityWebRequestUtils.SendAsync(req);
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Gemini (structured) Error: {req.error} \n{req.downloadHandler?.text}");
                return null;
            }
            try
            {
                var text = req.downloadHandler.text;
                var root = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(text);
                var part = root?["candidates"]?[0]?["content"]?["parts"]?[0];
                var asText = part?["text"]?.ToString();
                if (!string.IsNullOrEmpty(asText)) return asText;
                return root?.ToString();
            }
            catch (Exception ex)
            {
                Debug.LogError("Gemini (structured) JSON parse error: " + ex.Message);
                return null;
            }
        }
    }

    /// <summary>
    /// 会話履歴を送信し、RealTimeJsonSchema の JSON Schema に基づいた構造化出力のレスポンスから
    /// 各パラメータの値を schema.Parameters の Value に設定して返します。
    /// エラー時は null を返します。
    /// </summary>
    public static async Task<IJsonSchema> SendStructuredMessageWithRealTimeSchemaAsync(List<Message> messages, IJsonSchema schema, AIModelType model, Dictionary<string, object> initBody = null)
    {
        // RealTimeJsonSchema から JSON Schema を生成
        var jsonSchema = schema.GenerateJsonSchema();
        var valuesDict = await SendStructuredMessageWithSchemaAsync(messages,jsonSchema,model, initBody);
        if(valuesDict != null)
        {
            var result = schema.Clone() as IJsonSchema;
            result.PerseValueDict(valuesDict);
            return result;
        }
        return null;
    }

    /// <summary>
    /// JsonSchemaを直接介して構造化出力
    /// </summary>
    public static async Task<Dictionary<string, object>> SendStructuredMessageWithSchemaAsync(List<Message> messages, Dictionary<string, object> jsonSchema, AIModelType model, Dictionary<string, object> initBody = null)
    {
        var (endpoint, apiKey) = GetEndpointAndApiKey(model);
        string modelName = GetModelName(model);

        var body = new Dictionary<string, object>
        {
            { "model", modelName },
            { "messages", BuildOpenAiMessages(messages) },
            { "response_format", new Dictionary<string, object>
                {
                    { "type", "json_schema" },
                    { "json_schema", jsonSchema }
                }
            }
        };
        if (initBody != null)
        {
            foreach (var key in initBody.Keys)
            {
                body.Add(key, initBody[key]);
            }
        }
        string jsonBody = JsonConvert.SerializeObject(body);

        using (UnityWebRequest request = new UnityWebRequest(endpoint, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            await UnityWebRequestUtils.SendAsync(request);

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Error: " + request.error);
                return null;
            }
            else
            {
                string resultJson = request.downloadHandler.text;
                try
                {
                    var resultDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(resultJson);
                    if (resultDict != null && resultDict.ContainsKey("choices"))
                    {
                        var choices = resultDict["choices"] as Newtonsoft.Json.Linq.JArray;
                        if (choices != null && choices.Count > 0)
                        {
                            var firstChoice = choices[0] as Newtonsoft.Json.Linq.JObject;
                            if (firstChoice?["message"]?["content"] != null)
                            {
                                string content = firstChoice["message"]["content"].ToString();
                                // レスポンスの内容を Dictionary としてパース
                                var valuesDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(content);
                                return valuesDict;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError("JSON Parsing error: " + ex.Message);
                }
                return null;
            }
        }
    }
    /// <summary>
    /// 会話履歴と IFunction のリストを送信し、Function Calling 対応の応答を非同期に取得します。
    /// エラー時は null を返します。
    /// </summary>
    public static async Task<IJsonSchema> SendFunctionCallMessageAsync(
            List<Message> messages,
            List<IJsonSchema> functions,
            AIModelType model,
            Dictionary<string, object> initBody = null)
    {
        if (IsGeminiModel(model))
        {
            var modelNameGemini = GetModelName(model);
            return await SendFunctionCallMessageAsyncGemini(messages, functions, modelNameGemini, initBody);
        }
        var (endpoint, apiKey) = GetEndpointAndApiKey(model);
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError($"API key not configured. {GetRequiredEnvHint(model)}");
            return null;
        }
        string modelName = GetModelName(model);

        var functionList = functions.Select(func => func.GenerateJsonSchema()).ToList();

        var body = new Dictionary<string, object>
        {
            { "model",         modelName },
            { "messages",      BuildOpenAiMessages(messages) },
            { "functions",     functionList },
            { "function_call", "auto" }
        };
        if (initBody != null)
            foreach (var kv in initBody) body[kv.Key] = kv.Value;

        string jsonBody = JsonConvert.SerializeObject(body);

        using var request = new UnityWebRequest(endpoint, "POST")
        {
            uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonBody)),
            downloadHandler = new DownloadHandlerBuffer()
        };
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + apiKey);

        await UnityWebRequestUtils.SendAsync(request);

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"OpenAI Error: {request.error}");
            return null;
        }

        var root = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(request.downloadHandler.text);
        var fc = root?["choices"]?[0]?["message"]?["function_call"];
        if (fc == null) return null;

        string funcName = fc["name"]?.ToString() ?? "";
        string argJson = fc["arguments"]?.ToString() ?? "{}";

        // 対象関数を取得
        var target = functions.FirstOrDefault(f => f.Name == funcName);
        if (target == null) return null;

        // JSON → Dictionary<string, object>
        var argDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(argJson)
                      ?? new Dictionary<string, object>();

        // RealTimeJsonSchema で型を補正
        target.PerseValueDict(argDict);

        // Execute & そのまま返す
        return target;
    }
    #endregion

    private static async Task<IJsonSchema> SendFunctionCallMessageAsyncGemini(
        List<Message> messages,
        List<IJsonSchema> functions,
        string modelName,
        Dictionary<string, object> initBody)
    {
        var (_, apiKey) = GetEndpointAndApiKey(AIModelType.Gemini25);
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError($"API key not configured. {GetRequiredEnvHint(AIModelType.Gemini25)}");
            return null;
        }
        var endpoint = $"{geminiApiBase}/{modelName}:generateContent";

        var contents = BuildGeminiContents(messages);

        var functionDeclarations = new List<Dictionary<string, object>>();
        foreach (var f in functions)
        {
            var schema = f.GenerateJsonSchema();
            var name = schema.ContainsKey("name") ? schema["name"]?.ToString() : f.Name;
            var description = schema.ContainsKey("description") ? schema["description"]?.ToString() : string.Empty;
            object parameters = null;
            if (schema.ContainsKey("parameters")) parameters = schema["parameters"];
            else if (schema.ContainsKey("schema")) parameters = schema["schema"]; // fallback

            // Gemini 制約に合わせて parameters を調整（非 string 型には enum を付けない）
            parameters = SanitizeGeminiParameters(parameters);

            functionDeclarations.Add(new Dictionary<string, object>
            {
                { "name", name },
                { "description", description ?? string.Empty },
                { "parameters", parameters ?? new Dictionary<string, object>{{"type","object"}} }
            });
        }

        var body = new Dictionary<string, object>
        {
            { "contents", contents },
            { "tools", new[]{ new Dictionary<string, object> { { "function_declarations", functionDeclarations } } } },
            { "tool_config", new Dictionary<string, object>{ { "function_calling_config", new Dictionary<string, object>{ { "mode", "AUTO" } } } } }
        };
        if (initBody != null)
            foreach (var kv in initBody) body[kv.Key] = kv.Value;

        var jsonBody = JsonConvert.SerializeObject(body);
        using (var req = new UnityWebRequest(endpoint, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonBody));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("x-goog-api-key", apiKey);

            await UnityWebRequestUtils.SendAsync(req);
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Gemini (function) Error: {req.error} \n{req.downloadHandler?.text}");
                return null;
            }

            try
            {
                var text = req.downloadHandler.text;
                var root = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(text);
                var parts = root?["candidates"]?[0]?["content"]?["parts"] as Newtonsoft.Json.Linq.JArray;
                if (parts != null)
                {
                    foreach (var p in parts)
                    {
                        var fc = p?["functionCall"] as Newtonsoft.Json.Linq.JObject;
                        if (fc == null) continue;
                        var fname = fc["name"]?.ToString() ?? string.Empty;
                        var fargs = fc["args"] as Newtonsoft.Json.Linq.JObject;

                        var target = functions.FirstOrDefault(func => func.Name == fname);
                        if (target == null) continue;

                        var dict = fargs != null
                            ? JsonConvert.DeserializeObject<Dictionary<string, object>>(fargs.ToString())
                            : new Dictionary<string, object>();

                        target.PerseValueDict(dict);
                        return target;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("Gemini (function) parse error: " + ex.Message);
            }
            return null;
        }
    }

    // Gemini function_declarations.parameters の仕様に合わせ、
    // 非 string 型に含まれる enum を除去する簡易サニタイズ。
    private static object SanitizeGeminiParameters(object parameters)
    {
        try
        {
            if (parameters is Dictionary<string, object> dict)
            {
                if (dict.TryGetValue("type", out var tObj) && (tObj?.ToString() ?? "") == "object")
                {
                    if (dict.TryGetValue("properties", out var propsObj) && propsObj is Dictionary<string, object> props)
                    {
                        foreach (var key in props.Keys.ToList())
                        {
                            if (props[key] is Dictionary<string, object> p)
                            {
                                var typeStr = p.TryGetValue("type", out var pType) ? pType?.ToString() : null;
                                if (!string.Equals(typeStr, "string", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (p.ContainsKey("enum")) p.Remove("enum");
                                }
                                props[key] = p;
                            }
                        }
                        dict["properties"] = props;
                    }
                }
                return dict;
            }
        }
        catch { }
        return parameters;
    }
}

#endregion

