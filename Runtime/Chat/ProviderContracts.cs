using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityLLMAPI.Embedding;
using UnityLLMAPI.Schema;

namespace UnityLLMAPI.Chat
{
    // チャットリクエストに共通で使うオプション集
    public sealed class ChatRequestOptions
    {
        public static ChatRequestOptions Default => new ChatRequestOptions();

        public Dictionary<string, object> AdditionalBody { get; set; } = new Dictionary<string, object>();
        public IReadOnlyList<IJsonSchema> Functions { get; set; }
        public int TimeoutSeconds { get; set; } = -1;
    }

    // 画像生成に必要な入力パッケージ
    public sealed class ImageGenerationRequest
    {
        public List<Message> Messages { get; set; } = new List<Message>();
        public Dictionary<string, object> AdditionalBody { get; set; } = new Dictionary<string, object>();
        public int TimeoutSeconds { get; set; } = -1;
    }

    // Provider から受け取ったチャットレスポンスの生データ
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

    // Provider から受け取ったチャットストリーミングレスポンスの生データ
    public sealed class RawChatStreamResult
    {
        public AIProvider Provider { get; set; }
        public string ModelId { get; set; }
        public bool IsSuccess { get; set; }
        public long StatusCode { get; set; }
        public string ErrorMessage { get; set; }

        /// <summary>
        /// ストリームとして受信した生テキスト（SSE 等）の全量
        /// </summary>
        public string RawText { get; set; }

        /// <summary>
        /// アシスタントの通常出力（content）の全量
        /// </summary>
        public string Content { get; set; }
    }

    // Provider から受け取った画像生成レスポンスの生データ
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

    // Provider から受け取った埋め込みベクトルの生データ
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

    // Provider ごとの実装差分を吸収するインターフェース
    public interface IProviderClient
    {
        AIProvider Provider { get; }

        /// <summary>
        /// 通常チャットを送信し生レスポンスを返す。
        /// </summary>
        Task<RawChatResult> SendChatAsync(
            ModelSpec model,
            IReadOnlyList<Message> messages,
            ChatRequestOptions options,
            CancellationToken ct);

        /// <summary>
        /// JSON Schema などの構造化出力を要求するチャットを送信する。
        /// </summary>
        Task<RawChatResult> SendStructuredAsync(
            ModelSpec model,
            IReadOnlyList<Message> messages,
            string jsonSchema,
            ChatRequestOptions options,
            CancellationToken ct);

        /// <summary>
        /// 画像生成を要求し、生のレスポンスを返す。
        /// </summary>
        Task<RawImageResult> GenerateImageAsync(
            ModelSpec model,
            ImageGenerationRequest request,
            CancellationToken ct);

        /// <summary>
        /// 埋め込みベクトルを生成し生レスポンスを返す。
        /// </summary>
        Task<RawEmbeddingResult> CreateEmbeddingAsync(
            ModelSpec model,
            IReadOnlyList<string> texts,
            CancellationToken ct);

        /// <summary>
        /// ストリーミングでチャットを送信し、デルタを逐次通知しつつ結果を返す。
        /// </summary>
        Task<RawChatStreamResult> SendChatStreamAsync(
            ModelSpec model,
            IReadOnlyList<Message> messages,
            ChatRequestOptions options,
            System.Action<string> onContentDelta,
            CancellationToken ct);
    }
}
