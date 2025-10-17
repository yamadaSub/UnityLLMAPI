using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Cysharp.Threading.Tasks;

#region 共通クラス・Enum

/// <summary>
/// サポートするモデル：GPT と Grok のみ
/// </summary>
public enum EmmbeddingModelType
{
    OpenAISmall,
    OpenAILarge,
    Gemini01
}
#endregion
/// <summary>
/// OpenAI Embeddings を非同期取得して SerializableEmbedding で返す。
/// 既存 AIManager と同階層に置き、APIキーは AIManager のものを流用。
/// </summary>
public static class EmbeddingManager
{
    // OpenAI 固有エンドポイント（Embeddings は ChatCompletions と別URL）
    private const string openAiEmbeddingsEndpoint = "https://api.openai.com/v1/embeddings";
    private const string geminiEmbeddingsEndpoint = "https://generativelanguage.googleapis.com/v1beta/models/gemini-embedding-001:embedContent";

    /// <summary>
    /// 指定テキストの埋め込みを取得（OpenAI）。戻り値は軽量シリアライズ型。
    /// </summary>
    /// <param name="text">入力テキスト（単一）</param>
    /// <param name="openAiModelName">例: "text-embedding-3-small" / "text-embedding-3-large"</param>
    public static async UniTask<SerializableEmbedding> CreateEmbeddingAsync(
        string text,
        EmmbeddingModelType model = EmmbeddingModelType.Gemini01)
    {
        switch (model)
        {
            case EmmbeddingModelType.Gemini01:
                return await CreateGeminiEmbeddingAsync(text);
            case EmmbeddingModelType.OpenAILarge:
                return await CreateEmbeddingAsyncOpenAI(text, "text-embedding-3-large");
            case EmmbeddingModelType.OpenAISmall:
            default:
                return await CreateEmbeddingAsyncOpenAI(text, "text-embedding-3-small");
        }
    }



    #region Emmbeddingモデル実行用(OpenAI)
    /// <summary>
    /// 指定テキストの埋め込みを取得（OpenAI）。戻り値は軽量シリアライズ型。
    /// </summary>
    /// <param name="text">入力テキスト（単一）</param>
    /// <param name="openAiModelName">例: "text-embedding-3-small" / "text-embedding-3-large"</param>
    public static async UniTask<SerializableEmbedding> CreateEmbeddingAsyncOpenAI(
        string text,
        string modelName)
    {
        if (string.IsNullOrEmpty(text))
            throw new ArgumentException("text is null or empty.");

        // APIキーは AIManager の OpenAIApiKey を利用
        var ai = AIManager.Instance;
        if (ai == null || string.IsNullOrEmpty(ai.OpenAIApiKey))
            throw new InvalidOperationException("AIManager.Instance or OpenAIApiKey is not configured.");

        // OpenAI Embeddings API ボディ
        var body = new Dictionary<string, object>
        {
            { "model", modelName},
            { "input", text }
        };

        var jsonBody = JsonConvert.SerializeObject(body);

        using (var req = new UnityWebRequest(openAiEmbeddingsEndpoint, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + ai.OpenAIApiKey);

            await req.SendWebRequest().ToUniTask();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Embeddings Error: {req.error}");
                return null;
            }

            try
            {
                var json = req.downloadHandler.text;
                var dto = JsonConvert.DeserializeObject<OpenAIEmbeddingResponse>(json);

                // 想定: data[0].embedding が float[]
                var list = dto?.data;
                if (list == null || list.Count == 0 || list[0].embedding == null)
                {
                    Debug.LogError("Embeddings response is empty or invalid.");
                    return null;
                }

                var emb = new SerializableEmbedding(modelName);
                emb.SetFromFloatArray(list[0].embedding); // 量子化して内部格納
                return emb;
            }
            catch (Exception ex)
            {
                Debug.LogError("Embeddings JSON parse error: " + ex.Message);
                return null;
            }
        }
    }

    #region DTO
    [Serializable]
    private class OpenAIEmbeddingResponse
    {
        public string @object;
        public List<OpenAIEmbeddingData> data;
        public OpenAIEmbeddingUsage usage;
        public string model;
    }

    [Serializable]
    private class OpenAIEmbeddingData
    {
        public string @object;
        public float[] embedding;
        public int index;
    }

    [Serializable]
    private class OpenAIEmbeddingUsage
    {
        public int prompt_tokens;
        public int total_tokens;
    }
    #endregion
    #endregion

    #region Emmbeddingモデル実行用(Gemini)

    private static async UniTask<SerializableEmbedding> CreateGeminiEmbeddingAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
            throw new ArgumentException("text is null or empty.");

        var ai = AIManager.Instance;
        if (ai == null || string.IsNullOrEmpty(ai.GoogleApiKey))
            throw new InvalidOperationException("AIManager.GoogleApiKey not configured.");

        var modelName = "models/gemini-embedding-001"; // 固定
        var body = new Dictionary<string, object>
        {
            { "model", modelName },
            { "content", new Dictionary<string, object>{
                { "parts", new[]{ new Dictionary<string, string>{{ "text", text }} } }
            }}
        };

        string jsonBody = JsonConvert.SerializeObject(body);
        using var req = new UnityWebRequest(geminiEmbeddingsEndpoint, "POST")
        {
            uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonBody)),
            downloadHandler = new DownloadHandlerBuffer()
        };
        req.SetRequestHeader("Content-Type", "application/json");

        req.SetRequestHeader("x-goog-api-key", ai.GoogleApiKey);
        await req.SendWebRequest().ToUniTask();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"Gemini Embedding Error: {req.error}\n{req.downloadHandler?.text}");
            return null;
        }

        try
        {
            var json = req.downloadHandler.text;
            var dto = JsonConvert.DeserializeObject<GeminiEmbeddingResponse>(json);
            var vec = dto?.embedding?.values;
            if (vec == null) { Debug.LogError("Gemini response missing embedding.values"); return null; }

            var emb = new SerializableEmbedding(modelName);
            emb.SetFromFloatArray(vec);
            return emb;
        }
        catch (Exception ex)
        {
            Debug.LogError("Gemini Embedding parse error: " + ex.Message);
            return null;
        }
    }

    [Serializable]
    private class GeminiEmbeddingResponse
    {
        public GeminiEmbedding embedding;
    }

    [Serializable]
    private class GeminiEmbedding
    {
        public float[] values;
    }

    #endregion

    #region コサイン類似度計算用

    /// <summary>
    /// クエリとコーパスのコサイン類似度を一括計算し、(index,score)で降順ソートして返す。
    /// topK &gt; 0 なら上位K件のみ返す。
    /// </summary>
    public static List<SimilarityResult> RankByCosine(
        SerializableEmbedding query,
        IList<SerializableEmbedding> corpus,
        bool assumeNormalized = false,
        int topK = -1)
    {
        var results = new List<SimilarityResult>(corpus.Count);
        for (int i = 0; i < corpus.Count; i++)
        {
            var target = corpus[i];
            if (!string.Equals(query.Model, target.Model))
            {
                Debug.LogWarning("Emmbeddingで使用したモデルが異なります。正しい類似度比較にならない可能性がある点に注意してください。");
            }

            float score = assumeNormalized? query.Dot(target) : query.CosineSimilarity(target);
            results.Add(new SimilarityResult(i, score));
        }
        results.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (topK > 0 && topK < results.Count)
            results.RemoveRange(topK, results.Count - topK);

        return results;
    }

    public struct SimilarityResult
    {
        public int Index;
        public float Score;

        public SimilarityResult(int index, float score)
        {
            Index = index;
            Score = score;
        }
    }
    #endregion
}
