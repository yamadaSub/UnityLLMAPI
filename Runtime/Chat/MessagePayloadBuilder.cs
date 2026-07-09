using System;
using System.Collections.Generic;
using System.Linq;
using UnityLLMAPI.Schema;

namespace UnityLLMAPI.Chat
{
    internal static class MessagePayloadBuilder
    {
        public static List<Dictionary<string, object>> BuildResponsesInput(List<Message> messages)
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

                var messageParts = message.EnumerateParts().ToList();
                if (messageParts.All(part => part.type == MessageContentType.Text))
                {
                    payload["content"] = string.Concat(messageParts.Select(part => part.text ?? string.Empty));
                }
                else
                {
                    payload["content"] = BuildResponsesContent(messageParts);
                }
                result.Add(payload);
            }

            return result;
        }

        private static List<Dictionary<string, object>> BuildResponsesContent(IEnumerable<MessageContent> messageParts)
        {
            var parts = new List<Dictionary<string, object>>();
            if (messageParts == null) return parts;

            foreach (var part in messageParts)
            {
                if (part == null) continue;
                switch (part.type)
                {
                    case MessageContentType.Text:
                        parts.Add(new Dictionary<string, object>
                        {
                            { "type", "input_text" },
                            { "text", part.text ?? string.Empty }
                        });
                        break;
                    case MessageContentType.ImageUrl:
                        {
                            var url = part.uri;
                            if (string.IsNullOrWhiteSpace(url)) break;
                            parts.Add(new Dictionary<string, object>
                            {
                                { "type", "input_image" },
                                { "image_url", url }
                            });
                            break;
                        }
                    case MessageContentType.ImageData:
                        {
                            var dataUrl = ConvertImageContentToDataUrl(part);
                            if (string.IsNullOrEmpty(dataUrl)) break;
                            parts.Add(new Dictionary<string, object>
                            {
                                { "type", "input_image" },
                                { "image_url", dataUrl }
                            });
                            break;
                        }
                }
            }

            return parts;
        }

        public static void AddResponsesFunctions(
            Dictionary<string, object> body,
            IReadOnlyList<IJsonSchema> functions)
        {
            if (body == null || functions == null || functions.Count == 0) return;
            body["tools"] = functions.Select(BuildResponsesFunctionTool).ToList();
            body["tool_choice"] = "auto";
        }

        public static Dictionary<string, object> BuildResponsesJsonSchemaFormat(
            Dictionary<string, object> schemaDefinition)
        {
            var definition = schemaDefinition ?? new Dictionary<string, object>();
            var name = definition.TryGetValue("name", out var nameValue)
                ? nameValue?.ToString()
                : "schema";
            var schema = definition.TryGetValue("schema", out var schemaValue)
                ? schemaValue
                : definition;

            return new Dictionary<string, object>
            {
                { "type", "json_schema" },
                { "name", string.IsNullOrEmpty(name) ? "schema" : name },
                { "strict", false },
                { "schema", schema ?? new Dictionary<string, object> { { "type", "object" } } }
            };
        }

        private static Dictionary<string, object> BuildResponsesFunctionTool(IJsonSchema functionSchema)
        {
            var schema = functionSchema?.GenerateJsonSchema() ?? new Dictionary<string, object>();
            var name = schema.TryGetValue("name", out var nameValue)
                ? nameValue?.ToString()
                : functionSchema?.Name;
            var description = schema.TryGetValue("description", out var descriptionValue)
                ? descriptionValue?.ToString()
                : string.Empty;

            object parameters = null;
            if (schema.TryGetValue("parameters", out var parametersValue)) parameters = parametersValue;
            else if (schema.TryGetValue("schema", out var schemaValue)) parameters = schemaValue;

            var tool = new Dictionary<string, object>
            {
                { "type", "function" },
                { "name", name ?? string.Empty },
                { "parameters", parameters ?? new Dictionary<string, object> { { "type", "object" } } },
                { "strict", false }
            };
            if (!string.IsNullOrEmpty(description)) tool["description"] = description;
            return tool;
        }

        public static string BuildAnthropicSystem(List<Message> messages)
            => BuildSystemInstructionText(messages);

        public static string BuildSystemInstructionText(List<Message> messages)
        {
            if (messages == null || messages.Count == 0) return string.Empty;

            var systemTexts = new List<string>();
            foreach (var message in messages)
            {
                if (message == null || message.role != MessageRole.System) continue;

                foreach (var part in message.EnumerateParts())
                {
                    if (part?.type != MessageContentType.Text) continue;
                    if (string.IsNullOrWhiteSpace(part.text)) continue;
                    systemTexts.Add(part.text);
                }
            }

            return string.Join("\n\n", systemTexts);
        }

        public static List<Dictionary<string, object>> BuildAnthropicMessages(List<Message> messages)
        {
            var result = new List<Dictionary<string, object>>();
            if (messages == null) return result;

            foreach (var message in messages)
            {
                if (message == null || message.role == MessageRole.System) continue;

                var blocks = BuildAnthropicContentBlocks(message);
                if (blocks.Count == 0)
                {
                    blocks.Add(new Dictionary<string, object>
                    {
                        { "type", "text" },
                        { "text", message.content ?? string.Empty }
                    });
                }

                result.Add(new Dictionary<string, object>
                {
                    { "role", message.role.ToString().ToLowerInvariant() },
                    { "content", blocks }
                });
            }

            return result;
        }

        public static List<Dictionary<string, object>> BuildAnthropicContentBlocks(Message message)
        {
            var blocks = new List<Dictionary<string, object>>();
            if (message == null) return blocks;

            foreach (var part in message.EnumerateParts())
            {
                switch (part.type)
                {
                    case MessageContentType.Text:
                        blocks.Add(new Dictionary<string, object>
                        {
                            { "type", "text" },
                            { "text", part.text ?? string.Empty }
                        });
                        break;
                    case MessageContentType.ImageUrl:
                        {
                            var url = part.uri;
                            if (string.IsNullOrWhiteSpace(url)) break;

                            if (TryParseDataUrl(url, out var mimeType, out var base64Data))
                            {
                                blocks.Add(new Dictionary<string, object>
                                {
                                    { "type", "image" },
                                    { "source", new Dictionary<string, object>
                                        {
                                            { "type", "base64" },
                                            { "media_type", string.IsNullOrEmpty(mimeType) ? (part.mimeType ?? "image/png") : mimeType },
                                            { "data", base64Data }
                                        }
                                    }
                                });
                                break;
                            }

                            blocks.Add(new Dictionary<string, object>
                            {
                                { "type", "image" },
                                { "source", new Dictionary<string, object>
                                    {
                                        { "type", "url" },
                                        { "url", url }
                                    }
                                }
                            });
                            break;
                        }
                    case MessageContentType.ImageData:
                        {
                            if (!part.HasData) break;
                            blocks.Add(new Dictionary<string, object>
                            {
                                { "type", "image" },
                                { "source", new Dictionary<string, object>
                                    {
                                        { "type", "base64" },
                                        { "media_type", string.IsNullOrEmpty(part.mimeType) ? "image/png" : part.mimeType },
                                        { "data", Convert.ToBase64String(part.data) }
                                    }
                                }
                            });
                            break;
                        }
                }
            }

            return blocks;
        }

        public static List<Dictionary<string, object>> BuildGeminiInteractionInput(List<Message> messages)
        {
            var steps = new List<Dictionary<string, object>>();
            if (messages == null) return steps;

            foreach (var message in messages)
            {
                if (message == null || message.role == MessageRole.System) continue;
                var content = BuildGeminiInteractionContent(message);
                if (content.Count == 0)
                {
                    content.Add(new Dictionary<string, object>
                    {
                        { "type", "text" },
                        { "text", message.content ?? string.Empty }
                    });
                }

                steps.Add(new Dictionary<string, object>
                {
                    { "type", message.role == MessageRole.Assistant ? "model_output" : "user_input" },
                    { "content", content }
                });
            }

            return steps;
        }

        private static List<Dictionary<string, object>> BuildGeminiInteractionContent(Message message)
        {
            var content = new List<Dictionary<string, object>>();
            if (message == null) return content;

            foreach (var part in message.EnumerateParts())
            {
                switch (part.type)
                {
                    case MessageContentType.Text:
                        content.Add(new Dictionary<string, object>
                        {
                            { "type", "text" },
                            { "text", part.text ?? string.Empty }
                        });
                        break;
                    case MessageContentType.ImageUrl:
                        {
                            var url = part.uri;
                            if (string.IsNullOrWhiteSpace(url)) break;

                            if (TryParseDataUrl(url, out var mimeType, out var base64Data))
                            {
                                content.Add(new Dictionary<string, object>
                                {
                                    { "type", "image" },
                                    { "data", base64Data },
                                    { "mime_type", !string.IsNullOrEmpty(mimeType) ? mimeType : (part.mimeType ?? "image/png") }
                                });
                                break;
                            }

                            var image = new Dictionary<string, object>
                            {
                                { "type", "image" },
                                { "uri", url }
                            };
                            if (!string.IsNullOrEmpty(part.mimeType)) image["mime_type"] = part.mimeType;
                            content.Add(image);
                            break;
                        }
                    case MessageContentType.ImageData:
                        {
                            if (!part.HasData) break;
                            content.Add(new Dictionary<string, object>
                            {
                                { "type", "image" },
                                { "data", Convert.ToBase64String(part.data) },
                                { "mime_type", string.IsNullOrEmpty(part.mimeType) ? "image/png" : part.mimeType }
                            });
                            break;
                        }
                }
            }

            return content;
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
                                if (props[key] is Dictionary<string, object> property)
                                {
                                    var typeStr = property.TryGetValue("type", out var propertyType) ? propertyType?.ToString() : null;
                                    if (!string.Equals(typeStr, "string", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (property.ContainsKey("enum")) property.Remove("enum");
                                    }
                                    props[key] = property;
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
