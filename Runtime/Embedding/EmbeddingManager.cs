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
    public enum EmbeddingModelType
    {
        OpenAISmall,
        OpenAILarge,
        GeminiEmbedding2
    }

    public static class EmbeddingManager
    {
        private const string OpenAiEmbeddingsEndpoint = "https://api.openai.com/v1/embeddings";

        private enum EmbeddingProviderType
        {
            OpenAI,
            Gemini
        }

        private readonly struct EmbeddingModelSpec
        {
            public EmbeddingModelSpec(EmbeddingProviderType provider, string modelName, int? outputDimensionality = null)
            {
                Provider = provider;
                ModelName = modelName;
                OutputDimensionality = outputDimensionality;
            }

            public EmbeddingProviderType Provider { get; }
            public string ModelName { get; }
            public int? OutputDimensionality { get; }
            public bool SupportsMultimodal => Provider == EmbeddingProviderType.Gemini && GeminiEmbeddingPayloadBuilder.SupportsMultimodal(ModelName);
        }

        public static async Task<SerializableEmbedding> CreateEmbeddingAsync(
            string text,
            EmbeddingModelType model = EmbeddingModelType.GeminiEmbedding2,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = -1,
            int? outputDimensionality = null)
        {
            if (string.IsNullOrEmpty(text))
                throw new ArgumentException("text is null or empty.", nameof(text));

            return await CreateEmbeddingAsync(
                EmbeddingInput.FromText(text),
                model,
                cancellationToken,
                timeoutSeconds,
                outputDimensionality);
        }

        public static async Task<SerializableEmbedding> CreateEmbeddingAsync(
            EmbeddingInput input,
            EmbeddingModelType model = EmbeddingModelType.GeminiEmbedding2,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = -1,
            int? outputDimensionality = null)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var embeddings = await CreateEmbeddingsAsync(
                new[] { input },
                model,
                cancellationToken,
                timeoutSeconds,
                outputDimensionality);

            return embeddings?.FirstOrDefault();
        }

        public static async Task<List<SerializableEmbedding>> CreateEmbeddingsAsync(
            IEnumerable<string> texts,
            EmbeddingModelType model = EmbeddingModelType.GeminiEmbedding2,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = -1,
            int? outputDimensionality = null)
        {
            if (texts == null) throw new ArgumentNullException(nameof(texts));

            var inputs = texts
                .Where(t => !string.IsNullOrEmpty(t))
                .Select(EmbeddingInput.FromText)
                .ToList();

            if (inputs.Count == 0)
                throw new ArgumentException("texts is empty.", nameof(texts));

            return await CreateEmbeddingsAsync(
                inputs,
                model,
                cancellationToken,
                timeoutSeconds,
                outputDimensionality);
        }

        public static async Task<List<SerializableEmbedding>> CreateEmbeddingsAsync(
            IEnumerable<EmbeddingInput> inputs,
            EmbeddingModelType model = EmbeddingModelType.GeminiEmbedding2,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = -1,
            int? outputDimensionality = null)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));

            var normalizedInputs = inputs.Where(i => i != null).ToList();
            if (normalizedInputs.Count == 0)
                throw new ArgumentException("inputs is empty.", nameof(inputs));

            var spec = GetModelSpec(model, outputDimensionality);
            switch (spec.Provider)
            {
                case EmbeddingProviderType.OpenAI:
                    {
                        var texts = ConvertToTextInputs(normalizedInputs, spec.ModelName);
                        return await CreateOpenAiEmbeddingsAsync(
                            texts,
                            spec.ModelName,
                            cancellationToken,
                            timeoutSeconds,
                            spec.OutputDimensionality);
                    }
                case EmbeddingProviderType.Gemini:
                    ValidateGeminiInputs(normalizedInputs, spec);
                    return await CreateGeminiEmbeddingsAsync(
                        normalizedInputs,
                        spec,
                        cancellationToken,
                        timeoutSeconds);
                default:
                    throw new NotSupportedException($"Unsupported embedding provider for {model}.");
            }
        }

        public static async Task<SerializableEmbedding> CreateEmbeddingAsyncOpenAI(
            string text,
            string modelName,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = -1,
            int? outputDimensionality = null)
        {
            if (string.IsNullOrEmpty(text))
                throw new ArgumentException("text is null or empty.", nameof(text));

            var embeddings = await CreateOpenAiEmbeddingsAsync(
                new[] { text },
                modelName,
                cancellationToken,
                timeoutSeconds,
                outputDimensionality);

            return embeddings?.FirstOrDefault();
        }

        private static async Task<List<SerializableEmbedding>> CreateOpenAiEmbeddingsAsync(
            IReadOnlyList<string> texts,
            string modelName,
            CancellationToken cancellationToken,
            int timeoutSeconds,
            int? outputDimensionality)
        {
            if (texts == null) throw new ArgumentNullException(nameof(texts));
            if (texts.Count == 0) throw new ArgumentException("texts is empty.", nameof(texts));

            var openAiKey = AIManager.OpenAIApiKey;
            if (string.IsNullOrEmpty(openAiKey))
                throw new InvalidOperationException("OpenAIApiKey is not configured.");

            var body = new Dictionary<string, object>
            {
                { "model", modelName },
                { "input", texts }
            };

            if (outputDimensionality.HasValue)
            {
                body["dimensions"] = outputDimensionality.Value;
            }

            var jsonBody = JsonConvert.SerializeObject(body);

            using var req = new UnityWebRequest(OpenAiEmbeddingsEndpoint, "POST")
            {
                uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonBody)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + openAiKey);

            await UnityWebRequestUtils.SendAsync(req, cancellationToken, timeoutSeconds);

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Embedding error: {req.error}");
                return null;
            }

            try
            {
                var json = req.downloadHandler.text;
                var dto = JsonConvert.DeserializeObject<OpenAIEmbeddingResponse>(json);
                var list = dto?.data;
                if (list == null || list.Count == 0)
                {
                    Debug.LogError("Embedding response is empty or malformed.");
                    return null;
                }

                var embeddings = new List<SerializableEmbedding>(list.Count);
                foreach (var item in list)
                {
                    if (item?.embedding == null) continue;
                    var embedding = new SerializableEmbedding(modelName);
                    embedding.SetFromFloatArray(item.embedding);
                    embeddings.Add(embedding);
                }

                return embeddings;
            }
            catch (Exception ex)
            {
                Debug.LogError("Embedding JSON parse error: " + ex.Message);
                return null;
            }
        }

        private static async Task<List<SerializableEmbedding>> CreateGeminiEmbeddingsAsync(
            IReadOnlyList<EmbeddingInput> inputs,
            EmbeddingModelSpec spec,
            CancellationToken cancellationToken,
            int timeoutSeconds)
        {
            var result = new List<SerializableEmbedding>(inputs.Count);
            foreach (var input in inputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var embedding = await CreateGeminiEmbeddingAsync(input, spec, cancellationToken, timeoutSeconds);
                if (embedding != null)
                {
                    result.Add(embedding);
                }
            }

            return result;
        }

        private static async Task<SerializableEmbedding> CreateGeminiEmbeddingAsync(
            EmbeddingInput input,
            EmbeddingModelSpec spec,
            CancellationToken cancellationToken,
            int timeoutSeconds)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var googleKey = AIManager.GoogleApiKey;
            if (string.IsNullOrEmpty(googleKey))
                throw new InvalidOperationException("GoogleApiKey is not configured.");

            var body = GeminiEmbeddingPayloadBuilder.BuildRequestBody(
                spec.ModelName,
                input,
                spec.OutputDimensionality);

            var jsonBody = JsonConvert.SerializeObject(body);
            using var req = new UnityWebRequest(GeminiEmbeddingPayloadBuilder.BuildEndpoint(spec.ModelName), "POST")
            {
                uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonBody)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("x-goog-api-key", googleKey);

            await UnityWebRequestUtils.SendAsync(req, cancellationToken, timeoutSeconds);

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Gemini embedding error: {req.error}\n{req.downloadHandler?.text}");
                return null;
            }

            try
            {
                var json = req.downloadHandler.text;
                var dto = JsonConvert.DeserializeObject<GeminiEmbeddingResponse>(json);
                var values = dto?.embedding?.values;
                if (values == null)
                {
                    Debug.LogError("Gemini response does not contain embedding.values.");
                    return null;
                }

                var embedding = new SerializableEmbedding(spec.ModelName);
                embedding.SetFromFloatArray(values);
                return embedding;
            }
            catch (Exception ex)
            {
                Debug.LogError("Gemini embedding parse error: " + ex.Message);
                return null;
            }
        }

        private static EmbeddingModelSpec GetModelSpec(EmbeddingModelType model, int? outputDimensionality)
        {
            return model switch
            {
                EmbeddingModelType.OpenAISmall => new EmbeddingModelSpec(EmbeddingProviderType.OpenAI, "text-embedding-3-small", outputDimensionality),
                EmbeddingModelType.OpenAILarge => new EmbeddingModelSpec(EmbeddingProviderType.OpenAI, "text-embedding-3-large", outputDimensionality),
                EmbeddingModelType.GeminiEmbedding2 => new EmbeddingModelSpec(EmbeddingProviderType.Gemini, "gemini-embedding-2", outputDimensionality),
                _ => throw new NotSupportedException($"Unsupported embedding model: {model}")
            };
        }

        private static List<string> ConvertToTextInputs(IReadOnlyList<EmbeddingInput> inputs, string modelName)
        {
            var texts = new List<string>(inputs.Count);
            foreach (var input in inputs)
            {
                if (!GeminiEmbeddingPayloadBuilder.TryConvertToText(input, out var text))
                {
                    throw new NotSupportedException($"{modelName} only supports text embedding inputs in this library.");
                }

                texts.Add(text);
            }

            return texts;
        }

        private static void ValidateGeminiInputs(IReadOnlyList<EmbeddingInput> inputs, EmbeddingModelSpec spec)
        {
            if (spec.OutputDimensionality.HasValue && spec.OutputDimensionality.Value <= 0)
            {
                throw new ArgumentOutOfRangeException("outputDimensionality", "outputDimensionality must be greater than zero.");
            }

            if (spec.SupportsMultimodal && spec.OutputDimensionality.HasValue)
            {
                var dims = spec.OutputDimensionality.Value;
                if (dims < 128 || dims > 3072)
                {
                    throw new ArgumentOutOfRangeException("outputDimensionality", "gemini-embedding-2 outputDimensionality must be between 128 and 3072.");
                }
            }

            if (spec.SupportsMultimodal) return;

            foreach (var input in inputs)
            {
                if (GeminiEmbeddingPayloadBuilder.HasNonTextParts(input))
                {
                    throw new NotSupportedException($"{spec.ModelName} only supports text embedding inputs.");
                }
            }
        }

        [Serializable]
        private class OpenAIEmbeddingResponse
        {
            public List<OpenAIEmbeddingData> data;
        }

        [Serializable]
        private class OpenAIEmbeddingData
        {
            public float[] embedding;
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
                    Debug.LogWarning("Embedding model mismatch detected. Similarity may not be comparable.");
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
    }
}
