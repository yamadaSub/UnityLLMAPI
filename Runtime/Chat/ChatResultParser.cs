using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityLLMAPI.Schema;

namespace UnityLLMAPI.Chat
{
    internal static class ChatResultParser
    {
        /// <summary>
        /// Provider に応じてアシスタントのテキスト部分を抽出する。
        /// </summary>
        public static string ExtractAssistantMessage(RawChatResult raw)
        {
            if (raw?.Body == null) return null;
            return raw.Provider switch
            {
                AIProvider.OpenAI => ExtractOpenAiContent(raw.Body),
                AIProvider.Grok => ExtractOpenAiContent(raw.Body),
                AIProvider.Gemini => ExtractGeminiContent(raw.Body),
                _ => null
            };
        }

        /// <summary>
        /// アシスタント応答を Dictionary としてパースする。
        /// </summary>
        public static Dictionary<string, object> ExtractJsonDictionary(RawChatResult raw)
        {
            var text = ExtractAssistantMessage(raw);
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

        /// <summary>
        /// Function Calling の結果をパースし、対応する IJsonSchema に値を入れて返す。
        /// </summary>
        public static IJsonSchema ExtractFunctionCall(RawChatResult raw, IReadOnlyList<IJsonSchema> functions)
        {
            if (raw?.Body == null || functions == null || functions.Count == 0) return null;
            return raw.Provider switch
            {
                AIProvider.OpenAI => ExtractOpenAiFunction(raw.Body, functions),
                AIProvider.Grok => ExtractOpenAiFunction(raw.Body, functions),
                AIProvider.Gemini => ExtractGeminiFunction(raw.Body, functions),
                _ => null
            };
        }

        private static string ExtractOpenAiContent(JObject body)
        {
            return body?["choices"]?[0]?["message"]?["content"]?.ToString();
        }

        private static string ExtractGeminiContent(JObject body)
        {
            return body?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();
        }

        private static IJsonSchema ExtractOpenAiFunction(JObject body, IReadOnlyList<IJsonSchema> functions)
        {
            var message = body?["choices"]?[0]?["message"] as JObject;
            if (message == null) return null;

            // New format: tool_calls (tools)
            var toolCalls = message["tool_calls"] as JArray;
            if (toolCalls != null)
            {
                foreach (var toolCall in toolCalls)
                {
                    var func = toolCall?["function"] as JObject;
                    if (func == null) continue;

                    var funcName = func["name"]?.ToString() ?? string.Empty;
                    var argJson = func["arguments"]?.ToString() ?? "{}";
                    var parsed = ParseFunctionArguments(functions, funcName, argJson);
                    if (parsed != null) return parsed;
                }
            }

            // Legacy format: function_call (functions)
            var fc = message["function_call"] as JObject;
            if (fc == null) return null;

            return ParseFunctionArguments(
                functions,
                fc["name"]?.ToString() ?? string.Empty,
                fc["arguments"]?.ToString() ?? "{}");
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
                var fc = part?["functionCall"] as JObject;
                if (fc == null) continue;

                var fname = fc["name"]?.ToString() ?? string.Empty;
                var fargs = fc["args"] as JObject;
                var target = functions.FirstOrDefault(func => func.Name == fname);
                if (target == null) continue;

                var dict = fargs != null
                    ? JsonConvert.DeserializeObject<Dictionary<string, object>>(fargs.ToString())
                    : new Dictionary<string, object>();

                target.ParseValueDict(dict);
                return target;
            }

            return null;
        }
    }
}
