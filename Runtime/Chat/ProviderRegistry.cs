using System.Collections.Generic;

namespace UnityLLMAPI.Chat
{
    // AIProvider -> 具体的な IProviderClient を保持するレジストリ
    internal static class ProviderRegistry
    {
        private static readonly Dictionary<AIProvider, IProviderClient> Providers = new Dictionary<AIProvider, IProviderClient>
        {
            { AIProvider.OpenAI, new OpenAIClient() },
            { AIProvider.Grok,   new GrokClient() },
            { AIProvider.Gemini, new GeminiClient() },
        };

        /// <summary>
        /// Provider に対応する IProviderClient を取得する。
        /// </summary>
        public static IProviderClient Get(AIProvider provider) => Providers[provider];
    }
}
