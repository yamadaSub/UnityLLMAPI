using System;
using UnityEngine;

namespace UnityLLMAPI.Chat
{
    internal static class ApiKeyResolver
    {
        private static AIManagerBehaviour cachedBehaviour;

        public static string OpenAIApiKey => ResolveApiKey(b => b.OpenAIApiKey, new[] { "OPENAI_API_KEY" });
        public static string GrokApiKey => ResolveApiKey(b => b.GrokApiKey, new[] { "GROK_API_KEY" });
        public static string GoogleApiKey => ResolveApiKey(b => b.GoogleApiKey, new[] { "GOOGLE_API_KEY" });

#if UNITY_EDITOR
        private const string EditorIgnoreKeysConfig = "UnityLLMAPI.IGNORE_EDITOR_KEYS";
#endif

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
                default:
                    return "Configure the matching API key on AIManagerBehaviour, in EditorUserSettings (UnityLLMAPI.*), or via environment variables.";
            }
        }

        private static string ResolveApiKey(Func<AIManagerBehaviour, string> behaviourSelector, string[] envKeys)
        {
            var behaviour = GetBehaviour();
            if (behaviour != null)
            {
                var behaviourValue = behaviourSelector?.Invoke(behaviour);
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
            try
            {
                if (!ShouldUseEditorStoredKeys())
                {
                    return string.Empty;
                }

                var value = UnityEditor.EditorUserSettings.GetConfigValue(editorKey);
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
            catch
            {
                // ignored
            }

            return string.Empty;
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
