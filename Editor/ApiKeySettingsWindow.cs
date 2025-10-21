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

        private string openAI;
        private string grok;
        private string google;
        private bool showValues = false;
        private bool ignoreEditorKeys = false;

        private const string PrefIgnoreEditorKeys = "UnityLLMAPI.IGNORE_EDITOR_KEYS";

        [MenuItem("Tools/UnityLLMAPI/Configure API Keys")]
        public static void Open()
        {
            var win = GetWindow<ApiKeySettingsWindow>(true, "Unity LLM API Keys", true);
            win.minSize = new Vector2(520, 360);
            win.Show();
        }

        private void OnEnable()
        {
            openAI = LoadKey(PrefOpenAI);
            grok   = LoadKey(PrefGrok);
            google = LoadKey(PrefGoogle);
            ignoreEditorKeys = LoadBool(PrefIgnoreEditorKeys, false);
        }

        private void Save()
        {
            SaveKey(PrefOpenAI, openAI);
            SaveKey(PrefGrok,   grok);
            SaveKey(PrefGoogle, google);
            ShowNotification(new GUIContent("Saved project keys."));
        }

        private void ClearAll()
        {
            SaveKey(PrefOpenAI, null);
            SaveKey(PrefGrok,   null);
            SaveKey(PrefGoogle, null);
            openAI = grok = google = string.Empty;
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

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Keys are NOT saved in assets. Runtime resolves in order: Environment Variables -> EditorUserSettings (per project).",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Show values", GUILayout.Width(90));
                showValues = EditorGUILayout.Toggle(showValues);
            }

            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            ignoreEditorKeys = EditorGUILayout.ToggleLeft("パッケージ動作をテストする（Editor設定を無視）", ignoreEditorKeys);
            if (EditorGUI.EndChangeCheck())
            {
                SaveBool(PrefIgnoreEditorKeys, ignoreEditorKeys);
            }

            if (ignoreEditorKeys)
            {
                EditorGUILayout.HelpBox("パッケージ動作テスト中: EditorUserSettings に保存された API キーは API 呼び出しで使用されません。入力欄は参照のみで編集できません。", MessageType.Info);
            }

            DrawKeySection(
                title: "OpenAI",
                envNames: new[] { "OPENAI_API_KEY" },
                refValue: ref openAI,
                prefKey: PrefOpenAI);

            DrawKeySection(
                title: "Grok (x.ai)",
                envNames: new[] { "GROK_API_KEY" },
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
                if (GUILayout.Button("Save (Project)", GUILayout.Height(26))) Save();
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
            }

            // Input field (project-scoped value)
            var label = $"Project Key ({prefKey})";
            if (ignoreEditorKeys)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    if (showValues)
                    {
                        EditorGUILayout.TextField(label, string.Empty);
                    }
                    else
                    {
                        EditorGUILayout.PasswordField(label, string.Empty);
                    }
                }

                return;
            }

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
