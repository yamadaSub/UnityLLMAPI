using System.Collections.Generic;

namespace UnityLLMAPI.Chat
{
    internal static class ProviderRegistry
    {
        private static readonly Dictionary<AIProvider, IProviderClient> Providers = new Dictionary<AIProvider, IProviderClient>
        {
            { AIProvider.OpenAI, new OpenAIClient() },
            { AIProvider.Grok,   new GrokClient() },
            { AIProvider.Gemini, new GeminiClient() },
        };

        public static IProviderClient Get(AIProvider provider) => Providers[provider];
    }
}
