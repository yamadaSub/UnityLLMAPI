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
                _ => null
            };
        }

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
            var fc = body?["choices"]?[0]?["message"]?["function_call"];
            if (fc == null) return null;

            var funcName = fc["name"]?.ToString() ?? string.Empty;
            var argJson = fc["arguments"]?.ToString() ?? "{}";
            var target = functions.FirstOrDefault(f => f.Name == funcName);
            if (target == null) return null;

            var argDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(argJson)
                          ?? new Dictionary<string, object>();
            target.ParseValueDict(argDict);
            return target;
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
