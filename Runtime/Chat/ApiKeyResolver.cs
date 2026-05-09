using System;
using UnityEngine;

namespace UnityLLMAPI.Chat
{
    /// <summary>
    /// Resolves provider secrets and local endpoint settings from runtime overrides,
    /// scene components, EditorUserSettings, and environment variables.
    /// </summary>
    internal static class ApiKeyResolver
    {
        private const string EditorIgnoreKeysConfig = "UnityLLMAPI.IGNORE_EDITOR_KEYS";
        private const string EditorCodexAppServerBaseUrlConfig = "UnityLLMAPI.CODEX_APP_SERVER_BASE_URL";

        private static AIManagerBehaviour cachedBehaviour;
        private static bool hasCodexAppServerBaseUrlOverride;
        private static string codexAppServerBaseUrlOverride;

        public static string OpenAIApiKey => ResolveString(b => b.OpenAIApiKey, new[] { "OPENAI_API_KEY" });
        public static string GrokApiKey => ResolveString(b => b.GrokApiKey, new[] { "GROK_API_KEY" });
        public static string GoogleApiKey => ResolveString(b => b.GoogleApiKey, new[] { "GOOGLE_API_KEY" });
        public static string AnthropicApiKey => ResolveString(b => b.AnthropicApiKey, new[] { "ANTHROPIC_API_KEY" });

        public static OpenAIEndpointMode OpenAIEndpointMode => OpenAIEndpointMode.OpenAI;
        public static string CodexAppServerBaseUrl => ResolveCodexAppServerBaseUrl();

        public static void RegisterBehaviour(AIManagerBehaviour behaviour)
        {
            if (behaviour == null) return;
            cachedBehaviour = behaviour;
        }

        public static void UnregisterBehaviour(AIManagerBehaviour behaviour)
        {
            if (cachedBehaviour == behaviour)
            {
                cachedBehaviour = null;
            }
        }

        public static void ConfigureOpenAIEndpoint(OpenAIEndpointMode mode, string codexAppServerBaseUrl = null)
        {
            ConfigureCodexAppServerBaseUrl(codexAppServerBaseUrl);
        }

        public static void ConfigureCodexAppServerBaseUrl(string codexAppServerBaseUrl)
        {
            hasCodexAppServerBaseUrlOverride = !string.IsNullOrWhiteSpace(codexAppServerBaseUrl);
            codexAppServerBaseUrlOverride = codexAppServerBaseUrl;
        }

        public static void ClearOpenAIEndpointOverride()
        {
            hasCodexAppServerBaseUrlOverride = false;
            codexAppServerBaseUrlOverride = null;
        }

        public static string GetRequiredEnvHint(ModelSpec spec)
        {
            switch (spec.Provider)
            {
                case AIProvider.OpenAI:
                    return "Provide an OpenAI API key via AIManagerBehaviour, UnityLLMAPI.OPENAI_API_KEY (EditorUserSettings), or the OPENAI_API_KEY environment variable.";
                case AIProvider.Grok:
                    return "Provide a Grok API key via AIManagerBehaviour, UnityLLMAPI.GROK_API_KEY (EditorUserSettings), or the GROK_API_KEY environment variable.";
                case AIProvider.Gemini:
                    return "Provide a Google API key via AIManagerBehaviour, UnityLLMAPI.GOOGLE_API_KEY (EditorUserSettings), or the GOOGLE_API_KEY environment variable.";
                case AIProvider.Anthropic:
                    return "Provide an Anthropic API key via AIManagerBehaviour, UnityLLMAPI.ANTHROPIC_API_KEY (EditorUserSettings), or the ANTHROPIC_API_KEY environment variable.";
                case AIProvider.CodexAppServer:
                    return GetRequiredCodexAppServerUrlHint();
                default:
                    return "Configure the matching API key on AIManagerBehaviour, in EditorUserSettings (UnityLLMAPI.*), or via environment variables.";
            }
        }

        public static string GetRequiredCodexAppServerUrlHint()
        {
            return "Codex App Server WebSocket URL is not configured. Set a URL such as ws://127.0.0.1:4500 via AIManagerBehaviour, AIManager.SetCodexAppServerBaseUrl(url), Tools > UnityLLMAPI > Codex App Server, UnityLLMAPI.CODEX_APP_SERVER_BASE_URL, or the CODEX_APP_SERVER_BASE_URL / CODEX_APP_SERVER_URL environment variable.";
        }

        private static string ResolveCodexAppServerBaseUrl()
        {
            if (hasCodexAppServerBaseUrlOverride && !string.IsNullOrWhiteSpace(codexAppServerBaseUrlOverride))
            {
                return codexAppServerBaseUrlOverride;
            }

            var behaviour = GetBehaviour();
            if (behaviour != null && !string.IsNullOrWhiteSpace(behaviour.CodexAppServerBaseUrl))
            {
                return behaviour.CodexAppServerBaseUrl;
            }

#if UNITY_EDITOR
            var stored = GetEditorConfigValue(EditorCodexAppServerBaseUrlConfig);
            if (!string.IsNullOrWhiteSpace(stored))
            {
                return stored;
            }
#endif

            return ResolveEnvironmentValue(new[] { "CODEX_APP_SERVER_BASE_URL", "CODEX_APP_SERVER_URL" });
        }

        private static string ResolveString(Func<AIManagerBehaviour, string> behaviourSelector, string[] envKeys)
        {
            var behaviour = GetBehaviour();
            if (behaviour != null && behaviourSelector != null)
            {
                var behaviourValue = behaviourSelector.Invoke(behaviour);
                if (!string.IsNullOrEmpty(behaviourValue))
                {
                    return behaviourValue;
                }
            }

#if UNITY_EDITOR
            foreach (var key in envKeys)
            {
                var stored = GetEditorStoredKey(key);
                if (!string.IsNullOrEmpty(stored))
                {
                    return stored;
                }
            }
#endif

            return ResolveEnvironmentValue(envKeys);
        }

        private static string ResolveEnvironmentValue(string[] envKeys)
        {
            if (envKeys == null) return string.Empty;

            foreach (var key in envKeys)
            {
                try
                {
                    var v = Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.Process);
                    if (!string.IsNullOrEmpty(v)) return v;
                    v = Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.User);
                    if (!string.IsNullOrEmpty(v)) return v;
                    v = Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.Machine);
                    if (!string.IsNullOrEmpty(v)) return v;
                }
                catch
                {
                    // ignored
                }
            }

            return string.Empty;
        }

        private static AIManagerBehaviour GetBehaviour()
        {
            if (cachedBehaviour != null)
            {
                return cachedBehaviour;
            }

#if UNITY_2023_1_OR_NEWER
            cachedBehaviour = UnityEngine.Object.FindFirstObjectByType<AIManagerBehaviour>();
#else
            cachedBehaviour = UnityEngine.Object.FindObjectOfType<AIManagerBehaviour>();
#endif
            return cachedBehaviour;
        }

#if UNITY_EDITOR
        private static string GetEditorStoredKey(string envKey)
        {
            var editorKey = $"UnityLLMAPI.{envKey}";
            return GetEditorConfigValue(editorKey);
        }

        private static string GetEditorConfigValue(string key)
        {
            try
            {
                if (!ShouldUseEditorStoredKeys())
                {
                    return string.Empty;
                }

                return UnityEditor.EditorUserSettings.GetConfigValue(key) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool ShouldUseEditorStoredKeys()
        {
            try
            {
                var flag = UnityEditor.EditorUserSettings.GetConfigValue(EditorIgnoreKeysConfig);
                if (!string.IsNullOrEmpty(flag))
                {
                    if (flag == "1") return false;
                    if (flag == "0") return true;
                    if (bool.TryParse(flag, out var parsed)) return !parsed;
                }
            }
            catch
            {
                // ignored
            }
            return true;
        }
#endif
    }
}
