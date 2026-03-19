using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;
using UnityLLMAPI.Embedding;

/// <summary>
/// Demonstrates word2vec-style nearest-neighbor search, multimodal embedding ranking,
/// and AudioClip / VideoClip retrieval against CorpusWords.
/// Run the sample methods from the Inspector ContextMenu.
/// </summary>
public class EmbeddingSample : MonoBehaviour
{
    [Header("Optional multimodal query inputs")]
    [TextArea(2, 6)]
    [Tooltip("Optional text context combined with the image, audio, or video input. Leave empty to validate media-only retrieval.")]
    public string multimodalText = string.Empty;

    [Tooltip("Assign a Texture2D asset for the image query.")]
    public Texture2D multimodalImageTexture;

    [Tooltip("Assign an AudioClip asset. It is converted to WAV bytes before embedding. The clip importer must use Load Type = Decompress On Load.")]
    public AudioClip multimodalAudioClip;

    [Tooltip("Assign a VideoClip asset. Direct embedding is intended for Unity Editor workflows only. Use FromVideoData or FromFileUri in player builds.")]
    public VideoClip multimodalVideoClip;

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
        var dimension = GetResolvedOutputDimensionality();
        var corpusContext = await BuildCorpusContextAsync(dimension);
        if (!corpusContext.IsValid) return;

        await RunWord2VecAnalogyAsync(corpusContext.Corpus, corpusContext.Dimension);
        await RunSemanticTextQueryAsync(corpusContext.Corpus, corpusContext.Dimension);
        await RunMultimodalQueryAsync(corpusContext.Corpus, corpusContext.Dimension);
        await RunInlineAudioEmbeddingAsync(corpusContext.Corpus, corpusContext.Dimension);
        await RunInlineVideoEmbeddingAsync(corpusContext.Corpus, corpusContext.Dimension);
    }

    [ContextMenu("Run Word2Vec Analogy")]
    public async void RunWord2VecAnalogyMenuAsync()
    {
        var corpusContext = await BuildCorpusContextAsync(GetResolvedOutputDimensionality());
        if (!corpusContext.IsValid) return;

        await RunWord2VecAnalogyAsync(corpusContext.Corpus, corpusContext.Dimension);
    }

    [ContextMenu("Run Semantic Text Query")]
    public async void RunSemanticTextQueryMenuAsync()
    {
        var corpusContext = await BuildCorpusContextAsync(GetResolvedOutputDimensionality());
        if (!corpusContext.IsValid) return;

        await RunSemanticTextQueryAsync(corpusContext.Corpus, corpusContext.Dimension);
    }

    [ContextMenu("Run Multimodal Query / Image")]
    public async void RunMultimodalQueryMenuAsync()
    {
        var corpusContext = await BuildCorpusContextAsync(GetResolvedOutputDimensionality());
        if (!corpusContext.IsValid) return;

        await RunMultimodalQueryAsync(corpusContext.Corpus, corpusContext.Dimension);
    }

    [ContextMenu("Run Multimodal Query / Audio")]
    public async void RunInlineAudioEmbeddingMenuAsync()
    {
        var corpusContext = await BuildCorpusContextAsync(GetResolvedOutputDimensionality());
        if (!corpusContext.IsValid) return;

        await RunInlineAudioEmbeddingAsync(corpusContext.Corpus, corpusContext.Dimension);
    }

    [ContextMenu("Run Multimodal Query / Video")]
    public async void RunInlineVideoEmbeddingMenuAsync()
    {
        var corpusContext = await BuildCorpusContextAsync(GetResolvedOutputDimensionality());
        if (!corpusContext.IsValid) return;

        await RunInlineVideoEmbeddingAsync(corpusContext.Corpus, corpusContext.Dimension);
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
        if (multimodalImageTexture == null)
        {
            return;
        }

        var imagePart = EmbeddingPart.FromImage(multimodalImageTexture);
        if (imagePart == null)
        {
            Debug.LogWarning("[EmbeddingSample] Failed to encode multimodalImageTexture for Gemini Embedding 2.");
            return;
        }

        var multimodalEmbedding = await EmbeddingManager.CreateEmbeddingAsync(
            BuildMultimodalInput(imagePart),
            EmbeddingModelType.GeminiEmbedding2,
            outputDimensionality: dimension);

        if (multimodalEmbedding == null)
        {
            Debug.LogWarning("[EmbeddingSample] Failed to create the multimodal embedding.");
            return;
        }

        LogRanking("multimodal query", multimodalEmbedding, corpus);
    }

    private async System.Threading.Tasks.Task RunInlineAudioEmbeddingAsync(
        List<SerializableEmbedding> corpus,
        int dimension)
    {
        if (!await EnsureAudioClipLoadedAsync(multimodalAudioClip))
        {
            return;
        }

        var audioPart = EmbeddingPart.FromAudioClip(multimodalAudioClip);
        if (audioPart == null)
        {
            Debug.LogWarning("[EmbeddingSample] Failed to convert the AudioClip to WAV bytes for embedding.");
            return;
        }

        var embedding = await EmbeddingManager.CreateEmbeddingAsync(
            BuildMultimodalInput(audioPart),
            EmbeddingModelType.GeminiEmbedding2,
            outputDimensionality: dimension);

        if (embedding == null)
        {
            Debug.LogWarning("[EmbeddingSample] Failed to create the inline audio embedding.");
            return;
        }

        LogBestMatch("audio clip query", embedding, corpus);
    }

    private async System.Threading.Tasks.Task RunInlineVideoEmbeddingAsync(
        List<SerializableEmbedding> corpus,
        int dimension)
    {
        if (multimodalVideoClip == null)
        {
            Debug.Log("[EmbeddingSample] Skip sample. Assign multimodalVideoClip first.");
            return;
        }

        var videoPart = EmbeddingPart.FromVideoClip(multimodalVideoClip);
        if (videoPart == null)
        {
            Debug.LogWarning("[EmbeddingSample] Failed to read the VideoClip source bytes for embedding.");
            return;
        }

        var embedding = await EmbeddingManager.CreateEmbeddingAsync(
            BuildMultimodalInput(videoPart),
            EmbeddingModelType.GeminiEmbedding2,
            outputDimensionality: dimension);

        if (embedding == null)
        {
            Debug.LogWarning("[EmbeddingSample] Failed to create the inline video embedding.");
            return;
        }

        LogBestMatch("video clip query", embedding, corpus);
    }

    private async System.Threading.Tasks.Task<CorpusContext> BuildCorpusContextAsync(int dimension)
    {
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

    private static void LogBestMatch(
        string label,
        SerializableEmbedding queryEmbedding,
        List<SerializableEmbedding> corpus)
    {
        var ranked = EmbeddingManager.RankByCosine(queryEmbedding, corpus, topK: -1);
        if (ranked == null || ranked.Count == 0)
        {
            Debug.LogWarning($"[EmbeddingSample] {label}: no ranking results were produced.");
            return;
        }

        var best = ranked[0];
        Debug.Log($"[EmbeddingSample] {label} best match: {CorpusWords[best.Index]} (score={best.Score})");

        for (int i = 0; i < ranked.Count; i++)
        {
            var result = ranked[i];
            Debug.Log($"{i}: {CorpusWords[result.Index]} (score={result.Score})");
        }
    }

    private EmbeddingInput BuildMultimodalInput(EmbeddingPart mediaPart)
    {
        if (mediaPart == null)
        {
            return EmbeddingInput.FromParts();
        }

        if (string.IsNullOrWhiteSpace(multimodalText))
        {
            return EmbeddingInput.FromParts(mediaPart);
        }

        return EmbeddingInput.FromParts(
            EmbeddingPart.FromText(multimodalText),
            mediaPart);
    }

    private int GetResolvedOutputDimensionality()
    {
        return Mathf.Clamp(outputDimensionality, 128, 3072);
    }

    private static async System.Threading.Tasks.Task<bool> EnsureAudioClipLoadedAsync(AudioClip clip)
    {
        if (clip == null)
        {
            Debug.Log("[EmbeddingSample] Skip sample. Assign multimodalAudioClip first.");
            return false;
        }

        if (clip.loadState == AudioDataLoadState.Loaded)
        {
            return true;
        }

        clip.LoadAudioData();
        while (clip.loadState == AudioDataLoadState.Loading)
        {
            await System.Threading.Tasks.Task.Yield();
        }

        if (clip.loadState != AudioDataLoadState.Loaded)
        {
            Debug.LogWarning($"[EmbeddingSample] AudioClip failed to load sample data. loadState={clip.loadState}");
            return false;
        }

        return true;
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
