using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityLLMAPI.Chat
{
    internal static class MessagePayloadBuilder
    {
        public static List<Dictionary<string, object>> BuildOpenAiMessages(List<Message> messages)
        {
            var result = new List<Dictionary<string, object>>();
            if (messages == null) return result;

            foreach (var message in messages)
            {
                if (message == null) continue;
                var payload = new Dictionary<string, object>
                {
                    { "role", message.role.ToString().ToLowerInvariant() }
                };

                var parts = BuildOpenAiContentParts(message);
                if (parts.Count > 0)
                {
                    payload["content"] = parts;
                }
                else
                {
                    payload["content"] = message.content ?? string.Empty;
                }

                result.Add(payload);
            }

            return result;
        }

        public static List<Dictionary<string, object>> BuildOpenAiContentParts(Message message)
        {
            var parts = new List<Dictionary<string, object>>();
            if (message == null) return parts;

            foreach (var part in message.EnumerateParts())
            {
                switch (part.type)
                {
                    case MessageContentType.Text:
                        parts.Add(new Dictionary<string, object>
                        {
                            { "type", "text" },
                            { "text", part.text ?? string.Empty }
                        });
                        break;
                    case MessageContentType.ImageUrl:
                        {
                            var url = part.uri;
                            if (string.IsNullOrWhiteSpace(url)) break;
                            parts.Add(new Dictionary<string, object>
                            {
                                { "type", "image_url" },
                                { "image_url", new Dictionary<string, object> { { "url", url } } }
                            });
                            break;
                        }
                    case MessageContentType.ImageData:
                        {
                            var dataUrl = ConvertImageContentToDataUrl(part);
                            if (string.IsNullOrEmpty(dataUrl)) break;
                            parts.Add(new Dictionary<string, object>
                            {
                                { "type", "image_url" },
                                { "image_url", new Dictionary<string, object> { { "url", dataUrl } } }
                            });
                            break;
                        }
                }
            }

            return parts;
        }

        public static List<Dictionary<string, object>> BuildGeminiContents(List<Message> messages)
        {
            var contents = new List<Dictionary<string, object>>();
            if (messages == null) return contents;

            foreach (var message in messages)
            {
                if (message == null) continue;
                var parts = BuildGeminiParts(message);
                if (parts.Count == 0)
                {
                    parts.Add(new Dictionary<string, object> { { "text", message.content ?? string.Empty } });
                }

                var role = message.role == MessageRole.Assistant ? "model" : "user";
                contents.Add(new Dictionary<string, object>
                {
                    { "role", role },
                    { "parts", parts }
                });
            }

            return contents;
        }

        public static List<object> BuildGeminiParts(Message message)
        {
            var parts = new List<object>();
            if (message == null) return parts;

            foreach (var part in message.EnumerateParts())
            {
                switch (part.type)
                {
                    case MessageContentType.Text:
                        parts.Add(new Dictionary<string, object> { { "text", part.text ?? string.Empty } });
                        break;
                    case MessageContentType.ImageUrl:
                        {
                            var url = part.uri;
                            if (string.IsNullOrWhiteSpace(url)) break;

                            if (TryParseDataUrl(url, out var mimeType, out var base64Data))
                            {
                                parts.Add(new Dictionary<string, object>
                                {
                                    {
                                        "inline_data", new Dictionary<string, object>
                                        {
                                            { "mime_type", !string.IsNullOrEmpty(mimeType) ? mimeType : (part.mimeType ?? "image/png") },
                                            { "data", base64Data }
                                        }
                                    }
                                });
                                break;
                            }

                            var fileData = new Dictionary<string, object> { { "file_uri", url } };
                            if (!string.IsNullOrEmpty(part.mimeType))
                            {
                                fileData["mime_type"] = part.mimeType;
                            }
                            parts.Add(new Dictionary<string, object> { { "file_data", fileData } });
                            break;
                        }
                    case MessageContentType.ImageData:
                        {
                            if (!part.HasData) break;
                            parts.Add(new Dictionary<string, object>
                            {
                                {
                                    "inline_data", new Dictionary<string, object>
                                    {
                                        { "mime_type", string.IsNullOrEmpty(part.mimeType) ? "image/png" : part.mimeType },
                                        { "data", Convert.ToBase64String(part.data) }
                                    }
                                }
                            });
                            break;
                        }
                }
            }

            return parts;
        }

        public static object SanitizeGeminiParameters(object parameters)
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
            catch
            {
                // ignored
            }
            return parameters;
        }

        private static string ConvertImageContentToDataUrl(MessageContent part)
        {
            if (part == null || !part.HasData) return string.Empty;
            var mime = string.IsNullOrEmpty(part.mimeType) ? "image/png" : part.mimeType;
            return $"data:{mime};base64,{Convert.ToBase64String(part.data)}";
        }

        private static bool TryParseDataUrl(string uri, out string mimeType, out string base64Data)
        {
            mimeType = null;
            base64Data = null;

            if (string.IsNullOrEmpty(uri) || !uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var commaIndex = uri.IndexOf(',');
            if (commaIndex < 0 || commaIndex + 1 >= uri.Length)
            {
                return false;
            }

            var metadata = uri.Substring(5, commaIndex - 5);
            base64Data = uri.Substring(commaIndex + 1);

            if (string.IsNullOrEmpty(base64Data))
            {
                return false;
            }

            var semicolonIndex = metadata.IndexOf(';');
            if (semicolonIndex >= 0)
            {
                mimeType = metadata.Substring(0, semicolonIndex);
                var suffix = metadata.Substring(semicolonIndex + 1);
                if (!suffix.Contains("base64", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            else
            {
                mimeType = metadata;
            }

            return true;
        }
    }
}
