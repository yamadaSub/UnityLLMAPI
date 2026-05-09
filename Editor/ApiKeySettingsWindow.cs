// Editor-only window to configure API keys via project-scoped EditorUserSettings.
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System;

namespace UnityLLMAPI.Editor
{
    public class ApiKeySettingsWindow : EditorWindow
    {
        private const string PrefOpenAI = "UnityLLMAPI.OPENAI_API_KEY";
        private const string PrefGrok   = "UnityLLMAPI.GROK_API_KEY";
        private const string PrefGoogle = "UnityLLMAPI.GOOGLE_API_KEY";
        private const string PrefAnthropic = "UnityLLMAPI.ANTHROPIC_API_KEY";
        private const string LegacyPrefOpenAIEndpointMode = "UnityLLMAPI.OPENAI_ENDPOINT_MODE";

        private string openAI;
        private string grok;
        private string google;
        private string anthropic;
        private bool showValues = false;
        private bool ignoreEditorKeys = false;
        private Vector2 scroll;

        private const string PrefIgnoreEditorKeys = "UnityLLMAPI.IGNORE_EDITOR_KEYS";

        [MenuItem("Tools/UnityLLMAPI/Configure API Keys")]
        public static void Open()
        {
            var win = GetWindow<ApiKeySettingsWindow>(true, "Unity LLM API Keys", true);
            win.minSize = new Vector2(620, 520);
            win.Show();
        }

        private void OnEnable()
        {
            openAI = LoadKey(PrefOpenAI);
            grok   = LoadKey(PrefGrok);
            google = LoadKey(PrefGoogle);
            anthropic = LoadKey(PrefAnthropic);
            ignoreEditorKeys = LoadBool(PrefIgnoreEditorKeys, false);
            SaveKey(LegacyPrefOpenAIEndpointMode, null);
        }

        private void Save()
        {
            SaveKey(PrefOpenAI, openAI);
            SaveKey(PrefGrok,   grok);
            SaveKey(PrefGoogle, google);
            SaveKey(PrefAnthropic, anthropic);
            SaveKey(LegacyPrefOpenAIEndpointMode, null);
            ShowNotification(new GUIContent("Saved project keys."));
        }

        private void ClearAll()
        {
            if (!EditorUtility.DisplayDialog(
                    "Clear stored API keys?",
                    "This will remove UnityLLMAPI EditorUserSettings API keys. Environment variables and Codex App Server settings are not changed.",
                    "Clear Stored Keys",
                    "Cancel"))
            {
                return;
            }

            SaveKey(PrefOpenAI, null);
            SaveKey(PrefGrok,   null);
            SaveKey(PrefGoogle, null);
            SaveKey(PrefAnthropic, null);
            SaveKey(LegacyPrefOpenAIEndpointMode, null);
            openAI = grok = google = anthropic = string.Empty;
            ShowNotification(new GUIContent("Cleared stored keys."));
        }

        private static string LoadKey(string key)
        {
            var value = EditorUserSettings.GetConfigValue(key);
            return string.IsNullOrEmpty(value) ? string.Empty : value;
        }

        private static void SaveKey(string key, string value)
        {
            EditorUserSettings.SetConfigValue(key, string.IsNullOrEmpty(value) ? null : value);
        }

        private static bool LoadBool(string key, bool defaultValue)
        {
            var stored = EditorUserSettings.GetConfigValue(key);
            if (string.IsNullOrEmpty(stored))
            {
                return defaultValue;
            }

            if (stored == "1") return true;
            if (stored == "0") return false;
            if (bool.TryParse(stored, out var parsed)) return parsed;
            return defaultValue;
        }

        private static void SaveBool(string key, bool value)
        {
            EditorUserSettings.SetConfigValue(key, value ? "1" : "0");
        }

        private static string ReadEnv(string key)
        {
            try
            {
                var v = Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.Process);
                if (!string.IsNullOrEmpty(v)) return v;
                v = Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.User);
                if (!string.IsNullOrEmpty(v)) return v;
                v = Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.Machine);
                return v ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        private static string Mask(string v)
        {
            if (string.IsNullOrEmpty(v)) return "(not set)";
            var visible = Mathf.Min(4, v.Length);
            return new string('*', Math.Max(0, v.Length - visible)) + v.Substring(v.Length - visible, visible);
        }

        private static void DrawEnvStatus(string key, bool mask)
        {
            var envVal = ReadEnv(key);
            var display = string.IsNullOrEmpty(envVal)
                ? "(not set)"
                : mask ? Mask(envVal) : envVal;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Env", GUILayout.Width(72));
                EditorGUILayout.LabelField(key, EditorStyles.miniLabel, GUILayout.MinWidth(150));
                EditorGUILayout.SelectableLabel(display, EditorStyles.label, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }

        private void OnGUI()
        {
            DrawHeader();

            DrawOptions();

            scroll = EditorGUILayout.BeginScrollView(scroll);

            DrawKeySection("OpenAI", new[] { "OPENAI_API_KEY" }, ref openAI, PrefOpenAI);

            DrawKeySection("Grok (x.ai)", new[] { "GROK_API_KEY" }, ref grok, PrefGrok);
            DrawKeySection("Google (Gemini)", new[] { "GOOGLE_API_KEY" }, ref google, PrefGoogle);
            DrawKeySection("Anthropic (Claude)", new[] { "ANTHROPIC_API_KEY" }, ref anthropic, PrefAnthropic);

            EditorGUILayout.EndScrollView();

            DrawFooterActions();
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Unity LLM API Keys", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "Stored in EditorUserSettings, not assets. Runtime resolves values from AIManager overrides, scene settings, EditorUserSettings, then environment variables.",
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawOptions()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                showValues = EditorGUILayout.ToggleLeft("Show key values", showValues);

                EditorGUI.BeginChangeCheck();
                ignoreEditorKeys = EditorGUILayout.ToggleLeft("Ignore EditorUserSettings for package behavior tests", ignoreEditorKeys);
                if (EditorGUI.EndChangeCheck())
                {
                    SaveBool(PrefIgnoreEditorKeys, ignoreEditorKeys);
                }

                if (ignoreEditorKeys)
                {
                    EditorGUILayout.LabelField("Editor-stored keys are ignored by API calls while this is enabled.", EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        private void DrawFooterActions()
        {
            using (new EditorGUILayout.HorizontalScope("box"))
            {
                if (GUILayout.Button("Save", GUILayout.Width(100), GUILayout.Height(28)))
                {
                    Save();
                }

                if (GUILayout.Button("Close", GUILayout.Width(90), GUILayout.Height(28)))
                {
                    Close();
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Clear Stored Keys...", GUILayout.Width(150), GUILayout.Height(28)))
                {
                    ClearAll();
                }
            }
        }

        private void DrawKeySection(string title, string[] envNames, ref string refValue, string prefKey)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope("box"))
            {
                foreach (var env in envNames)
                {
                    DrawEnvStatus(env, mask: true);
                }

                using (new EditorGUI.DisabledScope(ignoreEditorKeys))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Project Key", GUILayout.Width(96));
                        var displayedValue = ignoreEditorKeys ? string.Empty : refValue;
                        if (showValues)
                        {
                            var updatedValue = EditorGUILayout.TextField(displayedValue);
                            if (!ignoreEditorKeys)
                            {
                                refValue = updatedValue;
                            }
                        }
                        else
                        {
                            var updatedValue = EditorGUILayout.PasswordField(displayedValue);
                            if (!ignoreEditorKeys)
                            {
                                refValue = updatedValue;
                            }
                        }
                    }
                }

                EditorGUILayout.LabelField("Stored in " + prefKey, EditorStyles.miniLabel);
            }
        }

    }
}
#endif
