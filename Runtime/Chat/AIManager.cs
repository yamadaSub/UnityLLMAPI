using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Threading.Tasks;
using System.Threading;
using UnityLLMAPI.Common;
using UnityLLMAPI.Schema;

namespace UnityLLMAPI.Chat
{

#region 共通クラス・Enum

/// <summary>
/// サポートするモデル
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
    Gemini3,
    Gemini3ProImage,
    Gemini25FlashImage
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
/// AI API 呼び出しの入口となるクラス。
/// モデル能力の検証 → ProviderClient へのルーティング → 共通パース という流れで処理する。
/// 公開 API は従来のメソッド名を維持しつつ内部実装のみ差し替えている。
/// </summary>
public static class AIManager
{
    public static string OpenAIApiKey => ApiKeyResolver.OpenAIApiKey;
    public static string GrokApiKey => ApiKeyResolver.GrokApiKey;
    public static string GoogleApiKey => ApiKeyResolver.GoogleApiKey;

    internal static void RegisterBehaviour(AIManagerBehaviour behaviour) => ApiKeyResolver.RegisterBehaviour(behaviour);
    internal static void UnregisterBehaviour(AIManagerBehaviour behaviour) => ApiKeyResolver.UnregisterBehaviour(behaviour);

    /// <summary>
    /// モデル種別に対応するメタ情報を取得するラッパー。
    /// </summary>
    public static ModelSpec GetModelSpec(AIModelType modelType) => ModelRegistry.Get(modelType);

    #region Helpers
    /// <summary>
    /// ProviderClient へ渡すチャットオプションを作成する。
    /// </summary>
    private static ChatRequestOptions BuildChatOptions(
        Dictionary<string, object> initBody,
        int timeoutSeconds,
        IReadOnlyList<UnityLLMAPI.Schema.IJsonSchema> functions = null)
    {
        // ProviderClient へ渡すオプションをまとめて生成する
        return new ChatRequestOptions
        {
            AdditionalBody = initBody ?? new Dictionary<string, object>(),
            Functions = functions,
            TimeoutSeconds = timeoutSeconds
        };
    }

    /// <summary>
    /// モデルが必要な機能を持つか検証し、非対応なら例外を投げる。
    /// </summary>
    private static void EnsureCapability(ModelSpec spec, AICapabilities required)
    {
        // モデルが要求された機能をサポートしているか事前チェック
        if (!spec.Capabilities.HasFlag(required))
        {
            throw new NotSupportedException($"{spec.ModelType} does not support {required} (Capabilities: {spec.Capabilities})");
        }
    }

    /// <summary>
    /// チャットレスポンスが失敗していないか判定し、失敗ならログを出す。
    /// </summary>
    private static bool IsFailed(RawChatResult raw, ModelSpec spec)
    {
        if (raw == null)
        {
            UnityEngine.Debug.LogError($"No response received from provider for {spec.ModelType}.");
            return true;
        }
        if (!raw.IsSuccess)
        {
            UnityEngine.Debug.LogError($"Provider call failed for {spec.ModelId}: {raw.ErrorMessage ?? "unknown error"}");
            return true;
        }
        return false;
    }

    /// <summary>
    /// 画像生成レスポンスが失敗していないか判定し、失敗ならログを出す。
    /// </summary>
    private static bool IsFailed(RawImageResult raw, ModelSpec spec)
    {
        if (raw == null)
        {
            UnityEngine.Debug.LogError($"No image response received from provider for {spec.ModelType}.");
            return true;
        }
        if (!raw.IsSuccess)
        {
            UnityEngine.Debug.LogError($"Image generation failed for {spec.ModelId}: {raw.ErrorMessage ?? "unknown error"}");
            return true;
        }
        return false;
    }

    /// <summary>
    /// 埋め込みレスポンスが失敗していないか判定し、失敗ならログを出す。
    /// </summary>
    private static bool IsFailed(RawEmbeddingResult raw, ModelSpec spec)
    {
        if (raw == null)
        {
            UnityEngine.Debug.LogError($"No embedding response received from provider for {spec.ModelType}.");
            return true;
        }
        if (!raw.IsSuccess)
        {
            UnityEngine.Debug.LogError($"Embedding request failed for {spec.ModelId}: {raw.ErrorMessage ?? "unknown error"}");
            return true;
        }
        return false;
    }
    #endregion

    /// <summary>
    /// 通常チャットを送信し、アシスタントからのテキスト応答を返す。
    /// </summary>
    public static async System.Threading.Tasks.Task<string> SendMessageAsync(
        List<Message> messages,
        AIModelType model,
        Dictionary<string, object> initBody = null,
        System.Threading.CancellationToken cancellationToken = default,
        int timeoutSeconds = -1)
    {
        var spec = ModelRegistry.Get(model);
        EnsureCapability(spec, AICapabilities.TextChat);

        // ModelSpec -> ProviderClient にルーティングして実行
        var provider = ProviderRegistry.Get(spec.Provider);
        var options = BuildChatOptions(initBody, timeoutSeconds);
        var raw = await provider.SendChatAsync(spec, messages, options, cancellationToken);
        if (IsFailed(raw, spec)) return null;

        return ChatResultParser.ExtractAssistantMessage(raw);
    }

    /// <summary>
    /// JSON Schema ベースの構造化応答を型 T にデシリアライズして返す。
    /// </summary>
    public static async System.Threading.Tasks.Task<T> SendStructuredMessageAsync<T>(
        List<Message> messages,
        AIModelType model,
        Dictionary<string, object> initBody = null,
        System.Threading.CancellationToken cancellationToken = default,
        int timeoutSeconds = -1)
    {
        var content = await SendStructuredMessageAsyncCore<T>(messages, model, initBody, cancellationToken, timeoutSeconds);
        if (string.IsNullOrEmpty(content))
        {
            return default;
        }
        try
        {
            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(content);
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogError("Structured response parse error: " + ex.Message);
            return default;
        }
    }

    /// <summary>
    /// 既存インスタンスに対し構造化応答の内容を上書きする。
    /// </summary>
    public static async System.Threading.Tasks.Task SendStructuredMessageAsync<T>(
        T targetInstance,
        List<Message> messages,
        AIModelType model,
        Dictionary<string, object> initBody = null,
        System.Threading.CancellationToken cancellationToken = default,
        int timeoutSeconds = -1)
    {
        if (targetInstance == null) throw new ArgumentNullException(nameof(targetInstance));

        var content = await SendStructuredMessageAsyncCore<T>(messages, model, initBody, cancellationToken, timeoutSeconds);
        if (!string.IsNullOrEmpty(content))
        {
            Newtonsoft.Json.JsonConvert.PopulateObject(content, targetInstance);
        }
    }

    /// <summary>
    /// RealTimeJsonSchema を用いた構造化応答を取得し、値をパースして返す。
    /// </summary>
    public static async System.Threading.Tasks.Task<UnityLLMAPI.Schema.IJsonSchema> SendStructuredMessageWithRealTimeSchemaAsync(
        List<Message> messages,
        UnityLLMAPI.Schema.IJsonSchema schema,
        AIModelType model,
        Dictionary<string, object> initBody = null,
        System.Threading.CancellationToken cancellationToken = default,
        int timeoutSeconds = -1)
    {
        var jsonSchema = schema.GenerateJsonSchema();
        var valuesDict = await SendStructuredMessageWithSchemaAsync(messages, jsonSchema, model, initBody, cancellationToken, timeoutSeconds);
        if (valuesDict != null)
        {
            var result = schema.Clone() as UnityLLMAPI.Schema.IJsonSchema;
            result?.ParseValueDict(valuesDict);
            return result;
        }
        return null;
    }

    /// <summary>
    /// 任意の JSON Schema を直接指定して構造化応答を Dictionary として受け取る。
    /// </summary>
    public static async System.Threading.Tasks.Task<Dictionary<string, object>> SendStructuredMessageWithSchemaAsync(
        List<Message> messages,
        Dictionary<string, object> jsonSchema,
        AIModelType model,
        Dictionary<string, object> initBody = null,
        System.Threading.CancellationToken cancellationToken = default,
        int timeoutSeconds = -1)
    {
        var spec = ModelRegistry.Get(model);
        EnsureCapability(spec, AICapabilities.JsonSchema);

        // 構造化出力に対応した ProviderClient に委譲
        var schemaJson = Newtonsoft.Json.JsonConvert.SerializeObject(jsonSchema ?? new Dictionary<string, object>());
        var raw = await SendStructuredRawAsync(messages, spec, schemaJson, initBody, cancellationToken, timeoutSeconds);
        if (IsFailed(raw, spec)) return null;

        return ChatResultParser.ExtractJsonDictionary(raw);
    }

    /// <summary>
    /// Function Calling 対応の会話を送り、実行すべき関数パラメータをパースして返す。
    /// </summary>
    public static async System.Threading.Tasks.Task<UnityLLMAPI.Schema.IJsonSchema> SendFunctionCallMessageAsync(
        List<Message> messages,
        List<UnityLLMAPI.Schema.IJsonSchema> functions,
        AIModelType model,
        Dictionary<string, object> initBody = null,
        System.Threading.CancellationToken cancellationToken = default,
        int timeoutSeconds = -1)
    {
        if (functions == null || functions.Count == 0)
        {
            throw new ArgumentException("functions is null or empty.", nameof(functions));
        }

        var spec = ModelRegistry.Get(model);
        EnsureCapability(spec, AICapabilities.FunctionCalling);

        // Function Calling は追加オプションとして functions を渡す
        var provider = ProviderRegistry.Get(spec.Provider);
        var options = BuildChatOptions(initBody, timeoutSeconds, functions);
        var raw = await provider.SendChatAsync(spec, messages, options, cancellationToken);
        if (IsFailed(raw, spec)) return null;

        return ChatResultParser.ExtractFunctionCall(raw, functions);
    }

    /// <summary>
    /// 画像生成に対応したモデルへメッセージを送り、生成画像セットを取得する。
    /// </summary>
    public static async System.Threading.Tasks.Task<ImageGenerationResponse> GenerateImagesAsync(
        List<Message> messages,
        AIModelType model = AIModelType.Gemini25FlashImage,
        Dictionary<string, object> initBody = null,
        System.Threading.CancellationToken cancellationToken = default,
        int timeoutSeconds = -1)
    {
        var spec = ModelRegistry.Get(model);
        EnsureCapability(spec, AICapabilities.ImageGeneration);

        var provider = ProviderRegistry.Get(spec.Provider);
        var request = new ImageGenerationRequest
        {
            Messages = messages ?? new List<Message>(),
            AdditionalBody = initBody ?? new Dictionary<string, object>(),
            TimeoutSeconds = timeoutSeconds
        };

        var raw = await provider.GenerateImageAsync(spec, request, cancellationToken);
        if (IsFailed(raw, spec)) return null;

        return new ImageGenerationResponse
        {
            images = raw.Images ?? new List<GeneratedImage>(),
            promptFeedback = raw.PromptFeedback,
            rawJson = raw.RawJson
        };
    }

    /// <summary>
    /// 画像生成のうち最初の画像のみを簡便に取得するラッパー。
    /// </summary>
    public static async System.Threading.Tasks.Task<GeneratedImage> GenerateImageAsync(
        List<Message> messages,
        AIModelType model = AIModelType.Gemini25FlashImage,
        Dictionary<string, object> initBody = null,
        System.Threading.CancellationToken cancellationToken = default,
        int timeoutSeconds = -1)
    {
        var response = await GenerateImagesAsync(messages, model, initBody, cancellationToken, timeoutSeconds);
        return response?.images?.FirstOrDefault();
    }

    #region Internal helpers
    /// <summary>
    /// 構造化応答を要求するチャットを送り、生テキストとして取得する。
    /// </summary>
    private static async System.Threading.Tasks.Task<string> SendStructuredMessageAsyncCore<T>(
        List<Message> messages,
        AIModelType model,
        Dictionary<string, object> initBody,
        System.Threading.CancellationToken cancellationToken,
        int timeoutSeconds)
    {
        var spec = ModelRegistry.Get(model);
        EnsureCapability(spec, AICapabilities.JsonSchema);

        var schema = UnityLLMAPI.Schema.JsonSchemaGenerator.GenerateSchema<T>();
        var schemaJson = Newtonsoft.Json.JsonConvert.SerializeObject(schema);
        var raw = await SendStructuredRawAsync(messages, spec, schemaJson, initBody, cancellationToken, timeoutSeconds);
        if (IsFailed(raw, spec)) return null;

        return ChatResultParser.ExtractAssistantMessage(raw);
    }

    /// <summary>
    /// 構造化チャットを送信し RawChatResult を受け取る共通ルート。
    /// </summary>
    private static async System.Threading.Tasks.Task<RawChatResult> SendStructuredRawAsync(
        List<Message> messages,
        ModelSpec spec,
        string schemaJson,
        Dictionary<string, object> initBody,
        System.Threading.CancellationToken cancellationToken,
        int timeoutSeconds)
    {
        var provider = ProviderRegistry.Get(spec.Provider);
        var options = BuildChatOptions(initBody, timeoutSeconds);
        return await provider.SendStructuredAsync(spec, messages, schemaJson, options, cancellationToken);
    }
    #endregion
}

#endregion

}
