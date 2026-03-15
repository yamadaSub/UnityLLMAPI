using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityLLMAPI.Schema;

namespace UnityLLMAPI.Chat
{
    internal static class ChatResultParser
    {
        public static string ExtractAssistantMessage(RawChatResult raw)
        {
            if (raw?.Body == null) return null;
            return raw.Provider switch
            {
                AIProvider.OpenAI => ExtractOpenAiContent(raw.Body),
                AIProvider.Grok => ExtractOpenAiContent(raw.Body),
                AIProvider.Gemini => ExtractGeminiContent(raw.Body),
                AIProvider.Anthropic => ExtractAnthropicContent(raw.Body),
                _ => null
            };
        }

        public static string ExtractStructuredContent(RawChatResult raw)
        {
            if (raw?.Body == null) return null;
            return raw.Provider switch
            {
                AIProvider.Anthropic => ExtractAnthropicStructuredContent(raw.Body),
                _ => ExtractAssistantMessage(raw)
            };
        }

        public static Dictionary<string, object> ExtractJsonDictionary(RawChatResult raw)
        {
            var text = ExtractStructuredContent(raw);
            if (string.IsNullOrEmpty(text)) return null;
            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, object>>(text);
            }
            catch
            {
                return null;
            }
        }

        public static IJsonSchema ExtractFunctionCall(RawChatResult raw, IReadOnlyList<IJsonSchema> functions)
        {
            if (raw?.Body == null || functions == null || functions.Count == 0) return null;
            return raw.Provider switch
            {
                AIProvider.OpenAI => ExtractOpenAiFunction(raw.Body, functions),
                AIProvider.Grok => ExtractOpenAiFunction(raw.Body, functions),
                AIProvider.Gemini => ExtractGeminiFunction(raw.Body, functions),
                AIProvider.Anthropic => ExtractAnthropicFunction(raw.Body, functions),
                _ => null
            };
        }

        private static string ExtractOpenAiContent(JObject body)
        {
            return body?["choices"]?[0]?["message"]?["content"]?.ToString();
        }

        private static string ExtractGeminiContent(JObject body)
        {
            return ConcatenateTextParts(body?["candidates"]?[0]?["content"]?["parts"] as JArray);
        }

        private static string ExtractAnthropicContent(JObject body)
        {
            var blocks = body?["content"] as JArray;
            if (blocks == null) return null;

            var texts = blocks
                .OfType<JObject>()
                .Where(block => (block["type"]?.ToString() ?? string.Empty) == "text")
                .Select(block => block["text"]?.ToString())
                .Where(text => !string.IsNullOrEmpty(text))
                .ToList();

            return texts.Count == 0 ? null : string.Join(string.Empty, texts);
        }

        private static string ExtractAnthropicStructuredContent(JObject body)
        {
            var toolBlock = FindAnthropicToolUseBlock(body);
            if (toolBlock != null)
            {
                var input = toolBlock["input"];
                if (input != null)
                {
                    return input.Type == JTokenType.String
                        ? input.ToString()
                        : input.ToString(Formatting.None);
                }
            }

            return ExtractAnthropicContent(body);
        }

        private static IJsonSchema ExtractOpenAiFunction(JObject body, IReadOnlyList<IJsonSchema> functions)
        {
            var message = body?["choices"]?[0]?["message"] as JObject;
            if (message == null) return null;

            var toolCalls = message["tool_calls"] as JArray;
            if (toolCalls != null)
            {
                foreach (var toolCall in toolCalls)
                {
                    var function = toolCall?["function"] as JObject;
                    if (function == null) continue;

                    var funcName = function["name"]?.ToString() ?? string.Empty;
                    var argJson = function["arguments"]?.ToString() ?? "{}";
                    var parsed = ParseFunctionArguments(functions, funcName, argJson);
                    if (parsed != null) return parsed;
                }
            }

            var functionCall = message["function_call"] as JObject;
            if (functionCall == null) return null;

            return ParseFunctionArguments(
                functions,
                functionCall["name"]?.ToString() ?? string.Empty,
                functionCall["arguments"]?.ToString() ?? "{}");
        }

        private static IJsonSchema ParseFunctionArguments(IReadOnlyList<IJsonSchema> functions, string funcName, string argJson)
        {
            if (functions == null || functions.Count == 0) return null;
            if (string.IsNullOrEmpty(funcName)) return null;

            var target = functions.FirstOrDefault(f => f.Name == funcName);
            if (target == null) return null;

            try
            {
                var argDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(argJson)
                              ?? new Dictionary<string, object>();
                target.ParseValueDict(argDict);
                return target;
            }
            catch
            {
                return null;
            }
        }

        private static IJsonSchema ExtractGeminiFunction(JObject body, IReadOnlyList<IJsonSchema> functions)
        {
            var parts = body?["candidates"]?[0]?["content"]?["parts"] as JArray;
            if (parts == null) return null;

            foreach (var part in parts)
            {
                var functionCall = part?["functionCall"] as JObject;
                if (functionCall == null) continue;

                var name = functionCall["name"]?.ToString() ?? string.Empty;
                var args = functionCall["args"] as JObject;
                var target = functions.FirstOrDefault(func => func.Name == name);
                if (target == null) continue;

                var dict = args != null
                    ? JsonConvert.DeserializeObject<Dictionary<string, object>>(args.ToString())
                    : new Dictionary<string, object>();

                target.ParseValueDict(dict);
                return target;
            }

            return null;
        }

        private static IJsonSchema ExtractAnthropicFunction(JObject body, IReadOnlyList<IJsonSchema> functions)
        {
            var toolUse = FindAnthropicToolUseBlock(body);
            if (toolUse == null) return null;

            var name = toolUse["name"]?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(name)) return null;

            var target = functions.FirstOrDefault(func => func.Name == name);
            if (target == null) return null;

            var input = toolUse["input"] as JObject;
            var dict = input != null
                ? JsonConvert.DeserializeObject<Dictionary<string, object>>(input.ToString())
                : new Dictionary<string, object>();

            target.ParseValueDict(dict);
            return target;
        }

        private static JObject FindAnthropicToolUseBlock(JObject body)
        {
            var blocks = body?["content"] as JArray;
            if (blocks == null) return null;

            foreach (var block in blocks.OfType<JObject>())
            {
                if ((block["type"]?.ToString() ?? string.Empty) == "tool_use")
                {
                    return block;
                }
            }

            return null;
        }

        private static string ConcatenateTextParts(JArray parts)
        {
            if (parts == null) return null;

            var texts = parts
                .OfType<JObject>()
                .Select(part => part["text"]?.ToString())
                .Where(text => !string.IsNullOrEmpty(text))
                .ToList();

            return texts.Count == 0 ? null : string.Concat(texts);
        }
    }
}
