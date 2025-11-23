using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
using UnityLLMAPI.Chat;
using UnityLLMAPI.Common;

namespace UnityLLMAPI.Embedding
{
    /// <summary>
    /// 利用可能な埋め込みモデル種別。
    /// </summary>
    public enum EmbeddingModelType
    {
        OpenAISmall,
        OpenAILarge,
        Gemini01,
        Gemini01_1536,
        Gemini01_768
    }

    /// <summary>
    /// OpenAI / Gemini の埋め込みベクトルを取得するためのヘルパークラス。
    /// API キーの解決は <see cref="AIManager"/> と同様。
    /// </summary>
    public static class EmbeddingManager
    {
        // Embedding はチャットとは別の OpenAI 専用エンドポイントを使用する。
        private const string openAiEmbeddingsEndpoint = "https://api.openai.com/v1/embeddings";
        private const string geminiEmbeddingsEndpoint = "https://generativelanguage.googleapis.com/v1beta/models/gemini-embedding-001:embedContent";

        /// <summary>
        /// 単一テキストに対する埋め込みベクトルを取得する。
        /// </summary>
        /// <param name="text">埋め込み対象テキスト。</param>
        /// <param name="model">使用するバックエンド。</param>
        /// <param name="cancellationToken">キャンセル要求を監視。</param>
        /// <param name="timeoutSeconds">UnityWebRequest.timeout に設定する秒数。</param>
        public static async Task<SerializableEmbedding> CreateEmbeddingAsync(
            string text,
            EmbeddingModelType model = EmbeddingModelType.Gemini01,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = -1)
        {
            switch (model)
            {
                case EmbeddingModelType.Gemini01:
                    return await CreateGeminiEmbeddingAsync(text, null, cancellationToken, timeoutSeconds);
                case EmbeddingModelType.Gemini01_1536:
                    return await CreateGeminiEmbeddingAsync(text, 1536, cancellationToken, timeoutSeconds);
                case EmbeddingModelType.Gemini01_768:
                    return await CreateGeminiEmbeddingAsync(text, 768, cancellationToken, timeoutSeconds);
                case EmbeddingModelType.OpenAILarge:
                    return await CreateEmbeddingAsyncOpenAI(text, "text-embedding-3-large", cancellationToken, timeoutSeconds);
                case EmbeddingModelType.OpenAISmall:
                default:
                    return await CreateEmbeddingAsyncOpenAI(text, "text-embedding-3-small", cancellationToken, timeoutSeconds);
            }
        }

        /// <summary>
        /// 複数テキストをまとめて埋め込み生成する。
        /// OpenAI は一括 API を利用し、Gemini は単一 API を順次呼び出す。
        /// </summary>
        public static async Task<List<SerializableEmbedding>> CreateEmbeddingsAsync(
            IEnumerable<string> texts,
            EmbeddingModelType model = EmbeddingModelType.Gemini01,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = -1)
        {
            if (texts == null) throw new ArgumentNullException(nameof(texts));
            var inputs = texts.Where(t => !string.IsNullOrEmpty(t)).ToList();
            if (inputs.Count == 0) throw new ArgumentException("texts is empty.", nameof(texts));

            switch (model)
            {
                case EmbeddingModelType.Gemini01:
                    return await CreateGeminiEmbeddingsAsync(inputs, null, cancellationToken, timeoutSeconds);
                case EmbeddingModelType.Gemini01_1536:
                    return await CreateGeminiEmbeddingsAsync(inputs, 1536, cancellationToken, timeoutSeconds);
                case EmbeddingModelType.Gemini01_768:
                    return await CreateGeminiEmbeddingsAsync(inputs, 768, cancellationToken, timeoutSeconds);
                case EmbeddingModelType.OpenAILarge:
                    return await CreateOpenAiEmbeddingsAsync(inputs, "text-embedding-3-large", cancellationToken, timeoutSeconds);
                case EmbeddingModelType.OpenAISmall:
                default:
                    return await CreateOpenAiEmbeddingsAsync(inputs, "text-embedding-3-small", cancellationToken, timeoutSeconds);
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
            string modelName,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = -1)
        {
            if (string.IsNullOrEmpty(text))
                throw new ArgumentException("text が null または空です。");

            // API キーは AIManagerBehaviour と EditorUserSettings と 環境変数 の順に解決
            var openAiKey = AIManager.OpenAIApiKey;
            if (string.IsNullOrEmpty(openAiKey))
                throw new InvalidOperationException("OpenAIApiKey が AIManagerBehaviour / EditorUserSettings / 環境変数のいずれにも設定されていません。");

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

                await UnityWebRequestUtils.SendAsync(req, cancellationToken, timeoutSeconds);

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"Embedding エラー: {req.error}");
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
                        Debug.LogError("Embedding レスポンスが空、または形式不正です。");
                        return null;
                    }

                    var emb = new SerializableEmbedding(modelName);
                    emb.SetFromFloatArray(list[0].embedding); // Copy raw vector values.
                    return emb;
                }
                catch (Exception ex)
                {
                    Debug.LogError("Embedding JSON パースエラー: " + ex.Message);
                    return null;
                }
            }
        }

        private static async Task<List<SerializableEmbedding>> CreateOpenAiEmbeddingsAsync(
            IReadOnlyList<string> texts,
            string modelName,
            CancellationToken cancellationToken,
            int timeoutSeconds)
        {
            if (texts == null) throw new ArgumentNullException(nameof(texts));
            if (texts.Count == 0) throw new ArgumentException("texts が空です。", nameof(texts));

            var openAiKey = AIManager.OpenAIApiKey;
            if (string.IsNullOrEmpty(openAiKey))
                throw new InvalidOperationException("OpenAIApiKey が AIManagerBehaviour / EditorUserSettings / 環境変数のいずれにも設定されていません。");

            var body = new Dictionary<string, object>
            {
                { "model", modelName},
                { "input", texts }
            };

            var jsonBody = JsonConvert.SerializeObject(body);

            using var req = new UnityWebRequest(openAiEmbeddingsEndpoint, "POST")
            {
                uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonBody)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + openAiKey);

            await UnityWebRequestUtils.SendAsync(req, cancellationToken, timeoutSeconds);

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Embedding エラー: {req.error}");
                return null;
            }

            try
            {
                var json = req.downloadHandler.text;
                var dto = JsonConvert.DeserializeObject<OpenAIEmbeddingResponse>(json);

                var list = dto?.data;
                if (list == null || list.Count == 0)
                {
                    Debug.LogError("Embedding レスポンスが空、または形式不正です。");
                    return null;
                }

                var embeddings = new List<SerializableEmbedding>(list.Count);
                foreach (var item in list)
                {
                    if (item?.embedding == null) continue;
                    var emb = new SerializableEmbedding(modelName);
                    emb.SetFromFloatArray(item.embedding);
                    embeddings.Add(emb);
                }
                return embeddings;
            }
            catch (Exception ex)
            {
                Debug.LogError("Embedding JSON パースエラー: " + ex.Message);
                return null;
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
        private static async Task<SerializableEmbedding> CreateGeminiEmbeddingAsync(string text, int? outputDimensionality, CancellationToken cancellationToken, int timeoutSeconds)
        {
            if (string.IsNullOrEmpty(text))
                throw new ArgumentException("text が null または空です。");

            var googleKey = AIManager.GoogleApiKey;
            if (string.IsNullOrEmpty(googleKey))
                throw new InvalidOperationException("GoogleApiKey が AIManagerBehaviour / EditorUserSettings / 環境変数のいずれにも設定されていません。");

            const string modelName = "models/gemini-embedding-001";
            var body = new Dictionary<string, object>
            {
                { "model", modelName },
                { "content", new Dictionary<string, object>{
                    { "parts", new[]{ new Dictionary<string, string>{{ "text", text }} } }
                }}
            };
            if (outputDimensionality.HasValue)
            {
                body["outputDimensionality"] = outputDimensionality.Value;
            }

            string jsonBody = JsonConvert.SerializeObject(body);
            using var req = new UnityWebRequest(geminiEmbeddingsEndpoint, "POST")
            {
                uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonBody)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            req.SetRequestHeader("Content-Type", "application/json");

            req.SetRequestHeader("x-goog-api-key", googleKey);
            await UnityWebRequestUtils.SendAsync(req, cancellationToken, timeoutSeconds);

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Gemini Embedding エラー: {req.error}\n{req.downloadHandler?.text}");
                return null;
            }

            try
            {
                var json = req.downloadHandler.text;
                var dto = JsonConvert.DeserializeObject<GeminiEmbeddingResponse>(json);
                var vec = dto?.embedding?.values;
                if (vec == null)
                {
                    Debug.LogError("Gemini レスポンスに embedding.values が含まれていません。");
                    return null;
                }

                var emb = new SerializableEmbedding(modelName);
                emb.SetFromFloatArray(vec);
                return emb;
            }
            catch (Exception ex)
            {
                Debug.LogError("Gemini Embedding のパースエラー: " + ex.Message);
                return null;
            }
        }

        private static async Task<List<SerializableEmbedding>> CreateGeminiEmbeddingsAsync(
            IReadOnlyList<string> texts,
            int? outputDimensionality,
            CancellationToken cancellationToken,
            int timeoutSeconds)
        {
            var result = new List<SerializableEmbedding>(texts.Count);
            foreach (var text in texts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var embedding = await CreateGeminiEmbeddingAsync(text, outputDimensionality, cancellationToken, timeoutSeconds);
                if (embedding != null)
                {
                    result.Add(embedding);
                }
            }
            return result;
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
        /// クエリベクトルとコーパス間のコサイン類似度を計算して降順ソートする。
        /// </summary>
        public static List<SimilarityResult> RankByCosine(
            SerializableEmbedding query,
            IList<SerializableEmbedding> corpus,
            bool assumeNormalized = false,
            int topK = -1,
            bool logModelMismatchWarning = true)
        {
            var results = new List<SimilarityResult>(corpus.Count);
            bool modelMismatchLogged = false;
            for (int i = 0; i < corpus.Count; i++)
            {
                var target = corpus[i];
                if (!string.Equals(query.Model, target.Model) && logModelMismatchWarning && !modelMismatchLogged)
                {
                    Debug.LogWarning("Embedding のモデルが異なります。類似度は比較できない可能性があります。");
                    modelMismatchLogged = true;
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
}
