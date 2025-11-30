using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityLLMAPI.Embedding;
using UnityLLMAPI.Schema;

namespace UnityLLMAPI.Chat
{
public sealed class ChatRequestOptions
{
    public static ChatRequestOptions Default => new ChatRequestOptions();

    public Dictionary<string, object> AdditionalBody { get; set; } = new Dictionary<string, object>();
    public IReadOnlyList<IJsonSchema> Functions { get; set; }
    public int TimeoutSeconds { get; set; } = -1;
}

public sealed class ImageGenerationRequest
{
    public List<Message> Messages { get; set; } = new List<Message>();
    public Dictionary<string, object> AdditionalBody { get; set; } = new Dictionary<string, object>();
    public int TimeoutSeconds { get; set; } = -1;
}

public sealed class RawChatResult
{
    public AIProvider Provider { get; set; }
    public string ModelId { get; set; }
    public bool IsSuccess { get; set; }
    public long StatusCode { get; set; }
    public string ErrorMessage { get; set; }
    public string RawJson { get; set; }
    public JObject Body { get; set; }
}

public sealed class RawImageResult
{
    public AIProvider Provider { get; set; }
    public string ModelId { get; set; }
    public bool IsSuccess { get; set; }
    public long StatusCode { get; set; }
    public string ErrorMessage { get; set; }
    public string RawJson { get; set; }
    public List<GeneratedImage> Images { get; set; } = new List<GeneratedImage>();
    public string PromptFeedback { get; set; }
}

public sealed class RawEmbeddingResult
{
    public AIProvider Provider { get; set; }
    public string ModelId { get; set; }
    public bool IsSuccess { get; set; }
    public long StatusCode { get; set; }
    public string ErrorMessage { get; set; }
    public string RawJson { get; set; }
    public List<SerializableEmbedding> Embeddings { get; set; } = new List<SerializableEmbedding>();
}

    public interface IProviderClient
    {
        AIProvider Provider { get; }

        Task<RawChatResult> SendChatAsync(
            ModelSpec model,
            IReadOnlyList<Message> messages,
            ChatRequestOptions options,
            CancellationToken ct);

        Task<RawChatResult> SendStructuredAsync(
            ModelSpec model,
            IReadOnlyList<Message> messages,
            string jsonSchema,
            ChatRequestOptions options,
            CancellationToken ct);

        Task<RawImageResult> GenerateImageAsync(
            ModelSpec model,
            ImageGenerationRequest request,
            CancellationToken ct);

        Task<RawEmbeddingResult> CreateEmbeddingAsync(
            ModelSpec model,
            IReadOnlyList<string> texts,
            CancellationToken ct);
    }
}
