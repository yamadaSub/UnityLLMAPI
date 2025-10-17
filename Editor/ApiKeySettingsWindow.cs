// Editor-only window to configure API keys via UnityEditor.EditorPrefs.
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

        private string openAI;
        private string grok;
        private string google;
        private bool showValues = false;

        [MenuItem("Tools/UnityLLMAPI/Configure API Keys")]
        public static void Open()
        {
            var win = GetWindow<ApiKeySettingsWindow>(true, "Unity LLM API Keys", true);
            win.minSize = new Vector2(520, 360);
            win.Show();
        }

        private void OnEnable()
        {
            openAI = EditorPrefs.GetString(PrefOpenAI, string.Empty);
            grok   = EditorPrefs.GetString(PrefGrok, string.Empty);
            google = EditorPrefs.GetString(PrefGoogle, string.Empty);
        }

        private void Save()
        {
            EditorPrefs.SetString(PrefOpenAI, openAI ?? string.Empty);
            EditorPrefs.SetString(PrefGrok,   grok   ?? string.Empty);
            EditorPrefs.SetString(PrefGoogle, google ?? string.Empty);
            ShowNotification(new GUIContent("Saved EditorPrefs keys."));
        }

        private void ClearAll()
        {
            EditorPrefs.DeleteKey(PrefOpenAI);
            EditorPrefs.DeleteKey(PrefGrok);
            EditorPrefs.DeleteKey(PrefGoogle);
            openAI = grok = google = string.Empty;
            ShowNotification(new GUIContent("Cleared EditorPrefs keys."));
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

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Keys are NOT saved in assets. Runtime resolves in order: Environment Variables -> EditorPrefs.",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Show values", GUILayout.Width(90));
                showValues = EditorGUILayout.Toggle(showValues);
            }

            DrawKeySection(
                title: "OpenAI",
                envNames: new[] { "OPENAI_API_KEY" },
                refValue: ref openAI,
                prefKey: PrefOpenAI);

            DrawKeySection(
                title: "Grok (x.ai) — use GROK_API_KEY or XAI_API_KEY",
                envNames: new[] { "GROK_API_KEY", "XAI_API_KEY" },
                refValue: ref grok,
                prefKey: PrefGrok);

            DrawKeySection(
                title: "Google (Gemini)",
                envNames: new[] { "GOOGLE_API_KEY" },
                refValue: ref google,
                prefKey: PrefGoogle);

            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save (EditorPrefs)", GUILayout.Height(26))) Save();
                if (GUILayout.Button("Clear", GUILayout.Height(26))) ClearAll();
            }
        }

        private void DrawKeySection(string title, string[] envNames, ref string refValue, string prefKey)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

            // Environment status
            using (new EditorGUILayout.VerticalScope("box"))
            {
                foreach (var env in envNames)
                {
                    var envVal = ReadEnv(env);
                    EditorGUILayout.LabelField($"Env {env}", string.IsNullOrEmpty(envVal) ? "(not set)" : Mask(envVal));
                }

                var prefVal = EditorPrefs.GetString(prefKey, string.Empty);
                EditorGUILayout.LabelField("EditorPrefs", string.IsNullOrEmpty(prefVal) ? "(not set)" : Mask(prefVal));
            }

            // Input field (EditorPrefs value)
            var label = $"EditorPrefs {prefKey}";
            if (showValues)
            {
                refValue = EditorGUILayout.TextField(label, refValue);
            }
            else
            {
                refValue = EditorGUILayout.PasswordField(label, refValue);
            }
        }
    }
}
#endif
