using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Cysharp.Threading.Tasks; 

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
    Gemini25FlashLite
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
    public string content;
}

#endregion

#region Function 呼び出し関連

public class FunctionSchema<T> : RealTimeJsonSchema<T> where T : SchemaParameter
{
    string Description { get; }
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
public class AIManager : MonoBehaviour
{
    public static AIManager Instance { get; private set; }

    // Note: Do NOT serialize API keys. Keys are resolved at runtime from
    // environment variables or editor-only storage to avoid persisting secrets.
    public string OpenAIApiKey => ResolveApiKey(new[] { "OPENAI_API_KEY" });
    public string GrokApiKey   => ResolveApiKey(new[] { "GROK_API_KEY", "XAI_API_KEY" });
    public string GoogleApiKey => ResolveApiKey(new[] { "GOOGLE_API_KEY" });

    private const string openAiEndpoint = "https://api.openai.com/v1/chat/completions";
    private const string grokEndpoint = "https://api.x.ai/v1/chat/completions";
    private const string geminiApiBase = "https://generativelanguage.googleapis.com/v1beta/models";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private string ResolveApiKey(string[] envKeys)
    {
        // 1) Process/User/Machine environment variables
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

#if UNITY_EDITOR
        // 2) Editor-only fallback: EditorPrefs (not included in builds, not in Assets)
        try
        {
            foreach (var key in envKeys)
            {
                var editorKey = $"UnityLLMAPI.{key}";
                var value = UnityEditor.EditorPrefs.GetString(editorKey, string.Empty);
                if (!string.IsNullOrEmpty(value)) return value;
            }
        }
        catch { /* ignore */ }
#endif

        return string.Empty;
    }

    private bool IsGeminiModel(AIModelType model)
        => model == AIModelType.Gemini25 || model == AIModelType.Gemini25Pro || model == AIModelType.Gemini25Flash || model == AIModelType.Gemini25FlashLite;

    private (string endpoint, string apiKey) GetEndpointAndApiKey(AIModelType model)
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
                return (geminiApiBase, GoogleApiKey);
            default:
                return (openAiEndpoint, OpenAIApiKey);
        }
    }

    private string GetRequiredEnvHint(AIModelType model)
    {
        switch (model)
        {
            case AIModelType.GPT4o:
                return "Set environment variable OPENAI_API_KEY.";
            case AIModelType.Grok2:
            case AIModelType.Grok3:
                return "Set environment variable GROK_API_KEY (or XAI_API_KEY).";
            case AIModelType.Gemini25:
            case AIModelType.Gemini25Pro:
            case AIModelType.Gemini25Flash:
            case AIModelType.Gemini25FlashLite:
                return "Set environment variable GOOGLE_API_KEY.";
            default:
                return "Set the appropriate API key environment variable.";
        }
    }

    private string GetModelName(AIModelType model)
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
            default:
                return "gpt-4o";
        }
    }

    #region UniTask 化した各メソッド

    /// <summary>
    /// 会話履歴（Message のリスト）を送信し、テキスト応答を非同期に取得します。
    /// エラー時は null を返します。
    /// </summary>
    public async UniTask<string> SendMessageAsync(List<Message> messages, AIModelType model, Dictionary<string, object> initBody=null)
    {
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
            { "messages", messages.Select(m => new { role = m.role.ToString().ToLower(), content = m.content }).ToList() }
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

            await request.SendWebRequest().ToUniTask();

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

    private async UniTask<string> SendMessageAsyncGemini(List<Message> messages, string modelName, Dictionary<string, object> initBody)
    {
        var (_, apiKey) = GetEndpointAndApiKey(AIModelType.Gemini25);
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError($"API key not configured. {GetRequiredEnvHint(AIModelType.Gemini25)}");
            return null;
        }
        var endpoint = $"{geminiApiBase}/{modelName}:generateContent";

        var contents = new List<Dictionary<string, object>>();
        foreach (var m in messages)
        {
            var role = m.role == MessageRole.Assistant ? "model" : "user";
            var parts = new List<Dictionary<string, string>> { new Dictionary<string, string>{{"text", m.content ?? string.Empty}} };
            contents.Add(new Dictionary<string, object>{{"role", role }, {"parts", parts}});
        }

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

            await req.SendWebRequest().ToUniTask();
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
    /// 会話履歴を送信し、構造化出力モードで型パラメータ T に基づく応答を非同期に取得します。
    /// エラー時は default(T) を返します。
    /// </summary>
    public async UniTask<T> SendStructuredMessageAsync<T>(List<Message> messages, AIModelType model, Dictionary<string, object> initBody = null)
    {
        string content = await SendStructuredMessageAsyncCore<T>(messages, model, initBody);
        if (string.IsNullOrEmpty(content))
        {
            return default;
        }
        T structuredResult = JsonConvert.DeserializeObject<T>(content);
        return structuredResult;
    }

    public async UniTask SendStructuredMessageAsync<T>(T targetInstance, List<Message> messages, AIModelType model, Dictionary<string, object> initBody = null)
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
    private async UniTask<string> SendStructuredMessageAsyncCore<T>(List<Message> messages, AIModelType model, Dictionary<string, object> initBody = null)
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
            { "messages", messages.Select(m => new { role = m.role.ToString().ToLower(), content = m.content }).ToList() },
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

            await request.SendWebRequest().ToUniTask();

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

    private async UniTask<string> SendStructuredMessageAsyncCoreGemini<T>(List<Message> messages, string modelName, Dictionary<string, object> initBody)
    {
        var (_, apiKey) = GetEndpointAndApiKey(AIModelType.Gemini25);
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError($"API key not configured. {GetRequiredEnvHint(AIModelType.Gemini25)}");
            return null;
        }
        var endpoint = $"{geminiApiBase}/{modelName}:generateContent";

        var contents = new List<Dictionary<string, object>>();
        foreach (var m in messages)
        {
            var role = m.role == MessageRole.Assistant ? "model" : "user";
            var parts = new List<Dictionary<string, string>> { new Dictionary<string, string>{{"text", m.content ?? string.Empty}} };
            contents.Add(new Dictionary<string, object>{{"role", role }, {"parts", parts}});
        }

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

            await req.SendWebRequest().ToUniTask();
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
    public async UniTask<IJsonSchema> SendStructuredMessageWithRealTimeSchemaAsync(List<Message> messages, IJsonSchema schema, AIModelType model, Dictionary<string, object> initBody = null)
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
    public async UniTask<Dictionary<string, object>> SendStructuredMessageWithSchemaAsync(List<Message> messages, Dictionary<string, object> jsonSchema, AIModelType model, Dictionary<string, object> initBody = null)
    {
        var (endpoint, apiKey) = GetEndpointAndApiKey(model);
        string modelName = GetModelName(model);

        var body = new Dictionary<string, object>
        {
            { "model", modelName },
            { "messages", messages.Select(m => new { role = m.role.ToString().ToLower(), content = m.content }).ToList() },
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

            await request.SendWebRequest().ToUniTask();

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
    public async UniTask<IJsonSchema> SendFunctionCallMessageAsync(
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
            { "messages",      messages.Select(m => new { role = m.role.ToString().ToLower(), content = m.content }).ToList() },
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

        await request.SendWebRequest().ToUniTask();

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

    private async UniTask<IJsonSchema> SendFunctionCallMessageAsyncGemini(
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

        var contents = new List<Dictionary<string, object>>();
        foreach (var m in messages)
        {
            var role = m.role == MessageRole.Assistant ? "model" : "user";
            var parts = new List<object> { new Dictionary<string, string> { { "text", m.content ?? string.Empty } } };
            contents.Add(new Dictionary<string, object> { { "role", role }, { "parts", parts } });
        }

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

            await req.SendWebRequest().ToUniTask();
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
    private object SanitizeGeminiParameters(object parameters)
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
