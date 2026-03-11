using System;
using System.Collections.Generic;
using UnityEngine;
using UnityLLMAPI.Common;

namespace UnityLLMAPI.Embedding
{
    public enum EmbeddingPartType
    {
        Text,
        FileUri,
        InlineData
    }

    [Serializable]
    public sealed class EmbeddingPart
    {
        public EmbeddingPartType type = EmbeddingPartType.Text;
        [TextArea(1, 10)]
        public string text;
        public string uri;
        public byte[] data;
        public string mimeType = "application/octet-stream";

        public bool HasData => data != null && data.Length > 0;

        public static EmbeddingPart FromText(string value)
            => new EmbeddingPart { type = EmbeddingPartType.Text, text = value };

        public static EmbeddingPart FromFileUri(string value, string mime = null)
            => new EmbeddingPart
            {
                type = EmbeddingPartType.FileUri,
                uri = value,
                mimeType = mime
            };

        public static EmbeddingPart FromInlineData(byte[] bytes, string mime = "application/octet-stream")
            => new EmbeddingPart
            {
                type = EmbeddingPartType.InlineData,
                data = bytes,
                mimeType = string.IsNullOrEmpty(mime) ? "application/octet-stream" : mime
            };

        public static EmbeddingPart FromImage(Texture texture, string mime = "image/png", bool allowGpuReadback = true, bool logWarnings = true)
        {
            if (texture == null)
            {
                if (logWarnings) Debug.LogWarning("EmbeddingPart.FromImage: texture is null.");
                return null;
            }

            if (!TextureEncodingUtility.TryGetPngBytes(texture, out var png, allowGpuReadback, logWarnings))
            {
                if (logWarnings) Debug.LogWarning("EmbeddingPart.FromImage: Failed to encode texture to PNG.");
                return null;
            }

            return FromInlineData(png, mime);
        }
    }

    [Serializable]
    public sealed class EmbeddingInput
    {
        public List<EmbeddingPart> parts = new List<EmbeddingPart>();

        internal IEnumerable<EmbeddingPart> EnumerateParts()
        {
            if (parts == null) yield break;

            foreach (var part in parts)
            {
                if (part == null) continue;
                yield return part;
            }
        }

        public static EmbeddingInput FromText(string value)
            => FromParts(EmbeddingPart.FromText(value));

        public static EmbeddingInput FromParts(params EmbeddingPart[] valueParts)
        {
            var input = new EmbeddingInput();
            if (valueParts == null) return input;

            foreach (var part in valueParts)
            {
                if (part == null) continue;
                input.parts.Add(part);
            }

            return input;
        }
    }

    internal static class GeminiEmbeddingPayloadBuilder
    {
        private const string ApiBase = "https://generativelanguage.googleapis.com/v1beta/models";

        public static string BuildEndpoint(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                throw new ArgumentException("modelId is null or empty.", nameof(modelId));
            }

            return $"{ApiBase}/{modelId}:embedContent";
        }

        public static Dictionary<string, object> BuildRequestBody(string modelId, EmbeddingInput input, int? outputDimensionality = null)
        {
            var parts = BuildParts(input);
            if (parts.Count == 0)
            {
                throw new ArgumentException("Embedding input does not contain any valid parts.", nameof(input));
            }

            var body = new Dictionary<string, object>
            {
                { "model", $"models/{modelId}" },
                {
                    "content",
                    new Dictionary<string, object>
                    {
                        { "parts", parts }
                    }
                }
            };

            if (outputDimensionality.HasValue)
            {
                body["outputDimensionality"] = outputDimensionality.Value;
            }

            return body;
        }

        public static bool SupportsMultimodal(string modelId)
            => !string.IsNullOrEmpty(modelId)
               && modelId.StartsWith("gemini-embedding-2", StringComparison.OrdinalIgnoreCase);

        public static bool TryConvertToText(EmbeddingInput input, out string text)
        {
            text = string.Empty;
            if (input == null) return false;

            var segments = new List<string>();
            foreach (var part in input.EnumerateParts())
            {
                if (part.type != EmbeddingPartType.Text)
                {
                    text = string.Empty;
                    return false;
                }

                if (!string.IsNullOrEmpty(part.text))
                {
                    segments.Add(part.text);
                }
            }

            text = string.Join("\n", segments);
            return !string.IsNullOrEmpty(text);
        }

        public static bool HasNonTextParts(EmbeddingInput input)
        {
            if (input == null) return false;

            foreach (var part in input.EnumerateParts())
            {
                if (part.type != EmbeddingPartType.Text)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<object> BuildParts(EmbeddingInput input)
        {
            var parts = new List<object>();
            if (input == null) return parts;

            foreach (var part in input.EnumerateParts())
            {
                switch (part.type)
                {
                    case EmbeddingPartType.Text:
                        if (!string.IsNullOrEmpty(part.text))
                        {
                            parts.Add(new Dictionary<string, object> { { "text", part.text } });
                        }
                        break;
                    case EmbeddingPartType.FileUri:
                        if (!string.IsNullOrWhiteSpace(part.uri))
                        {
                            var fileData = new Dictionary<string, object> { { "file_uri", part.uri } };
                            if (!string.IsNullOrEmpty(part.mimeType))
                            {
                                fileData["mime_type"] = part.mimeType;
                            }

                            parts.Add(new Dictionary<string, object> { { "file_data", fileData } });
                        }
                        break;
                    case EmbeddingPartType.InlineData:
                        if (part.HasData)
                        {
                            parts.Add(new Dictionary<string, object>
                            {
                                {
                                    "inline_data",
                                    new Dictionary<string, object>
                                    {
                                        { "mime_type", string.IsNullOrEmpty(part.mimeType) ? "application/octet-stream" : part.mimeType },
                                        { "data", Convert.ToBase64String(part.data) }
                                    }
                                }
                            });
                        }
                        break;
                }
            }

            return parts;
        }
    }
}
