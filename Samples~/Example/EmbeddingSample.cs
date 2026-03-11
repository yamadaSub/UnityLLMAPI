using System.Collections.Generic;
using UnityEngine;
using UnityLLMAPI.Embedding;

/// <summary>
/// Demonstrates word2vec-style nearest-neighbor search and multimodal embedding ranking.
/// Run the sample methods from the Inspector ContextMenu.
/// </summary>
public class EmbeddingSample : MonoBehaviour
{
    [Header("Optional multimodal input for Gemini Embedding 2")]
    public Texture2D multimodalTexture;

    [Header("Embedding Settings")]
    public int outputDimensionality = 768;

    private static readonly List<string> CorpusWords = new List<string>
    {
        "queen",
        "king",
        "woman",
        "man",
        "bird",
        "branch",
        "tree",
        "animal",
        "car",
        "building"
    };

    [ContextMenu("Run All Embedding Samples")]
    public async void RunAllEmbeddingSamplesAsync()
    {
        var corpusContext = await BuildCorpusContextAsync();
        if (!corpusContext.IsValid) return;

        await RunWord2VecAnalogyAsync(corpusContext.Corpus, corpusContext.Dimension);
        await RunSemanticTextQueryAsync(corpusContext.Corpus, corpusContext.Dimension);
        await RunMultimodalQueryAsync(corpusContext.Corpus, corpusContext.Dimension);
    }

    [ContextMenu("Run Word2Vec Analogy")]
    public async void RunWord2VecAnalogyMenuAsync()
    {
        var corpusContext = await BuildCorpusContextAsync();
        if (!corpusContext.IsValid) return;

        await RunWord2VecAnalogyAsync(corpusContext.Corpus, corpusContext.Dimension);
    }

    [ContextMenu("Run Semantic Text Query")]
    public async void RunSemanticTextQueryMenuAsync()
    {
        var corpusContext = await BuildCorpusContextAsync();
        if (!corpusContext.IsValid) return;

        await RunSemanticTextQueryAsync(corpusContext.Corpus, corpusContext.Dimension);
    }

    [ContextMenu("Run Multimodal Query")]
    public async void RunMultimodalQueryMenuAsync()
    {
        var corpusContext = await BuildCorpusContextAsync();
        if (!corpusContext.IsValid) return;

        await RunMultimodalQueryAsync(corpusContext.Corpus, corpusContext.Dimension);
    }

    private async System.Threading.Tasks.Task RunWord2VecAnalogyAsync(
        List<SerializableEmbedding> corpus,
        int dimension)
    {
        var king = await EmbeddingManager.CreateEmbeddingAsync(
            "king",
            EmbeddingModelType.GeminiEmbedding2,
            outputDimensionality: dimension);
        var man = await EmbeddingManager.CreateEmbeddingAsync(
            "male",
            EmbeddingModelType.GeminiEmbedding2,
            outputDimensionality: dimension);
        var woman = await EmbeddingManager.CreateEmbeddingAsync(
            "female",
            EmbeddingModelType.GeminiEmbedding2,
            outputDimensionality: dimension);

        if (king == null || man == null || woman == null)
        {
            Debug.LogWarning("[EmbeddingSample] Failed to create the word2vec analogy embeddings.");
            return;
        }

        var analogyQuery = (king - man + woman).Normalized();
        LogRanking("word2vec analogy: king - male + female", analogyQuery, corpus);
    }

    private async System.Threading.Tasks.Task RunSemanticTextQueryAsync(
        List<SerializableEmbedding> corpus,
        int dimension)
    {
        var textQuery = await EmbeddingManager.CreateEmbeddingAsync(
            "bird sitting on a branch",
            EmbeddingModelType.GeminiEmbedding2,
            outputDimensionality: dimension);

        if (textQuery == null)
        {
            Debug.LogWarning("[EmbeddingSample] Failed to create the semantic text query embedding.");
            return;
        }

        LogRanking("semantic text query: bird sitting on a branch", textQuery, corpus);
    }

    private async System.Threading.Tasks.Task RunMultimodalQueryAsync(
        List<SerializableEmbedding> corpus,
        int dimension)
    {
        if (multimodalTexture == null)
        {
            return;
        }

        var imagePart = EmbeddingPart.FromImage(multimodalTexture);
        if (imagePart == null)
        {
            Debug.LogWarning("[EmbeddingSample] Failed to encode multimodalTexture for Gemini Embedding 2.");
            return;
        }

        var multimodalEmbedding = await EmbeddingManager.CreateEmbeddingAsync(
            EmbeddingInput.FromParts(
                EmbeddingPart.FromText("Find the closest words for the content in this image."),
                imagePart),
            EmbeddingModelType.GeminiEmbedding2,
            outputDimensionality: dimension);

        if (multimodalEmbedding == null)
        {
            Debug.LogWarning("[EmbeddingSample] Failed to create the multimodal embedding.");
            return;
        }

        LogRanking("multimodal query", multimodalEmbedding, corpus);
    }

    private async System.Threading.Tasks.Task<CorpusContext> BuildCorpusContextAsync()
    {
        int dimension = Mathf.Clamp(outputDimensionality, 128, 3072);

        var corpus = await EmbeddingManager.CreateEmbeddingsAsync(
            CorpusWords,
            EmbeddingModelType.GeminiEmbedding2,
            outputDimensionality: dimension);

        if (corpus == null || corpus.Count == 0)
        {
            Debug.LogWarning("[EmbeddingSample] Failed to build the embedding corpus.");
            return CorpusContext.Invalid;
        }

        return new CorpusContext(corpus, dimension);
    }

    private static void LogRanking(
        string label,
        SerializableEmbedding queryEmbedding,
        List<SerializableEmbedding> corpus)
    {
        var ranked = EmbeddingManager.RankByCosine(queryEmbedding, corpus, topK: -1);
        Debug.Log($"[EmbeddingSample] {label}");

        for (int i = 0; i < ranked.Count; i++)
        {
            var result = ranked[i];
            Debug.Log($"{i}: {CorpusWords[result.Index]} (score={result.Score})");
        }
    }

    private readonly struct CorpusContext
    {
        public static CorpusContext Invalid => new CorpusContext(null, 0);

        public CorpusContext(List<SerializableEmbedding> corpus, int dimension)
        {
            Corpus = corpus;
            Dimension = dimension;
        }

        public List<SerializableEmbedding> Corpus { get; }
        public int Dimension { get; }
        public bool IsValid => Corpus != null && Corpus.Count > 0 && Dimension > 0;
    }
}
