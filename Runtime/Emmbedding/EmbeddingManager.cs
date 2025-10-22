using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Threading.Tasks;

#region Common Types

/// <summary>
/// 利用可能な埋め込みモデル種別。
/// </summary>
public enum EmmbeddingModelType
{
    OpenAISmall,
    OpenAILarge,
    Gemini01
}
#endregion

/// <summary>
/// OpenAI / Gemini の埋め込みベクトルを取得するためのヘルパークラス。
/// API キーの解決は <see cref="AIManager"/> と共通である。
/// </summary>
public static class EmbeddingManager
{
    // Embeddings use a dedicated OpenAI endpoint (different from chat completions).
    private const string openAiEmbeddingsEndpoint = "https://api.openai.com/v1/embeddings";
    private const string geminiEmbeddingsEndpoint = "https://generativelanguage.googleapis.com/v1beta/models/gemini-embedding-001:embedContent";

    /// <summary>
    /// 指定テキストに対する埋め込みベクトルを取得する。
    /// </summary>
    /// <param name="text">埋め込み対象テキスト。</param>
    /// <param name="model">使用するバックエンド。</param>
    public static async Task<SerializableEmbedding> CreateEmbeddingAsync(
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

    #region OpenAI Embeddings

    /// <summary>
    /// OpenAI の埋め込みエンドポイントを呼び出す。
    /// </summary>
    /// <param name="text">埋め込み対象テキスト。</param>
    /// <param name="modelName">OpenAI モデル名。</param>
    public static async Task<SerializableEmbedding> CreateEmbeddingAsyncOpenAI(
        string text,
        string modelName)
    {
        if (string.IsNullOrEmpty(text))
            throw new ArgumentException("text is null or empty.");

        // API キーは AIManagerBehaviour → EditorUserSettings → 環境変数 の順に解決
        var openAiKey = AIManager.OpenAIApiKey;
        if (string.IsNullOrEmpty(openAiKey))
            throw new InvalidOperationException("OpenAIApiKey is not configured on AIManagerBehaviour, EditorUserSettings, or environment variables.");

        // OpenAI Embeddings API のリクエストボディ
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
            req.SetRequestHeader("Authorization", "Bearer " + openAiKey);

            await UnityWebRequestUtils.SendAsync(req);

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Embeddings Error: {req.error}");
                return null;
            }

            try
            {
                var json = req.downloadHandler.text;
                var dto = JsonConvert.DeserializeObject<OpenAIEmbeddingResponse>(json);

                // Expected shape: data[0].embedding => float[]
                var list = dto?.data;
                if (list == null || list.Count == 0 || list[0].embedding == null)
                {
                    Debug.LogError("Embeddings response is empty or invalid.");
                    return null;
                }

                var emb = new SerializableEmbedding(modelName);
                emb.SetFromFloatArray(list[0].embedding); // Copy raw vector values.
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

    #region Gemini Embeddings

    /// <summary>
    /// Gemini Embeddings API を呼び出す。
    /// </summary>
    private static async Task<SerializableEmbedding> CreateGeminiEmbeddingAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
            throw new ArgumentException("text is null or empty.");

        var googleKey = AIManager.GoogleApiKey;
        if (string.IsNullOrEmpty(googleKey))
            throw new InvalidOperationException("GoogleApiKey is not configured on AIManagerBehaviour, EditorUserSettings, or environment variables.");

        const string modelName = "models/gemini-embedding-001";
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

        req.SetRequestHeader("x-goog-api-key", googleKey);
        await UnityWebRequestUtils.SendAsync(req);

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
            if (vec == null)
            {
                Debug.LogError("Gemini response missing embedding.values");
                return null;
            }

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

    #region Cosine Similarity Helpers

    /// <summary>
    /// Calculates cosine similarity between a query vector and a corpus.
    /// Results are sorted by descending similarity.
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
                Debug.LogWarning("Embedding models differ. Similarity scores may not be comparable.");
            }

            float score = assumeNormalized ? query.Dot(target) : query.CosineSimilarity(target);
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

