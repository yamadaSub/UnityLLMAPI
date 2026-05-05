// Editor-only window to configure API keys via project-scoped EditorUserSettings.
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System;
using UnityLLMAPI.Chat;

namespace UnityLLMAPI.Editor
{
    public class ApiKeySettingsWindow : EditorWindow
    {
        private const string PrefOpenAI = "UnityLLMAPI.OPENAI_API_KEY";
        private const string PrefGrok   = "UnityLLMAPI.GROK_API_KEY";
        private const string PrefGoogle = "UnityLLMAPI.GOOGLE_API_KEY";
        private const string PrefAnthropic = "UnityLLMAPI.ANTHROPIC_API_KEY";
        private const string PrefOpenAIEndpointMode = "UnityLLMAPI.OPENAI_ENDPOINT_MODE";
        private const string PrefCodexAppServerBaseUrl = "UnityLLMAPI.CODEX_APP_SERVER_BASE_URL";

        private string openAI;
        private string grok;
        private string google;
        private string anthropic;
        private OpenAIEndpointMode openAIEndpointMode = OpenAIEndpointMode.OpenAI;
        private string codexAppServerBaseUrl;
        private bool showValues = false;
        private bool ignoreEditorKeys = false;

        private const string PrefIgnoreEditorKeys = "UnityLLMAPI.IGNORE_EDITOR_KEYS";

        [MenuItem("Tools/UnityLLMAPI/Configure API Keys")]
        public static void Open()
        {
            var win = GetWindow<ApiKeySettingsWindow>(true, "Unity LLM API Keys", true);
            win.minSize = new Vector2(560, 460);
            win.Show();
        }

        private void OnEnable()
        {
            openAI = LoadKey(PrefOpenAI);
            grok   = LoadKey(PrefGrok);
            google = LoadKey(PrefGoogle);
            anthropic = LoadKey(PrefAnthropic);
            openAIEndpointMode = LoadOpenAIEndpointMode(PrefOpenAIEndpointMode, OpenAIEndpointMode.OpenAI);
            codexAppServerBaseUrl = LoadKey(PrefCodexAppServerBaseUrl);
            ignoreEditorKeys = LoadBool(PrefIgnoreEditorKeys, false);
        }

        private void Save()
        {
            SaveKey(PrefOpenAI, openAI);
            SaveKey(PrefGrok,   grok);
            SaveKey(PrefGoogle, google);
            SaveKey(PrefAnthropic, anthropic);
            SaveKey(PrefOpenAIEndpointMode, openAIEndpointMode.ToString());
            SaveKey(PrefCodexAppServerBaseUrl, codexAppServerBaseUrl);
            ShowNotification(new GUIContent("Saved project keys."));
        }

        private void ClearAll()
        {
            SaveKey(PrefOpenAI, null);
            SaveKey(PrefGrok,   null);
            SaveKey(PrefGoogle, null);
            SaveKey(PrefAnthropic, null);
            SaveKey(PrefOpenAIEndpointMode, null);
            SaveKey(PrefCodexAppServerBaseUrl, null);
            openAI = grok = google = anthropic = string.Empty;
            openAIEndpointMode = OpenAIEndpointMode.OpenAI;
            codexAppServerBaseUrl = string.Empty;
            ShowNotification(new GUIContent("Cleared stored keys."));
        }

        private static string LoadKey(string key)
        {
            var value = EditorUserSettings.GetConfigValue(key);
            return string.IsNullOrEmpty(value) ? string.Empty : value;
        }

        private static OpenAIEndpointMode LoadOpenAIEndpointMode(string key, OpenAIEndpointMode defaultValue)
        {
            var stored = EditorUserSettings.GetConfigValue(key);
            if (string.IsNullOrEmpty(stored)) return defaultValue;
            return Enum.TryParse(stored, true, out OpenAIEndpointMode mode) ? mode : defaultValue;
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
            EditorGUILayout.LabelField($"Env {key}", display);
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Keys and local endpoint settings are NOT saved in assets. Endpoint settings resolve in order: AIManager override -> AIManagerBehaviour -> EditorUserSettings -> Environment Variables.",
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

            DrawOpenAIEndpointSection();

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

            DrawKeySection(
                title: "Anthropic (Claude)",
                envNames: new[] { "ANTHROPIC_API_KEY" },
                refValue: ref anthropic,
                prefKey: PrefAnthropic);

            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save (Project)", GUILayout.Height(26))) Save();
                if (GUILayout.Button("Clear", GUILayout.Height(26))) ClearAll();
            }
        }

        private void DrawOpenAIEndpointSection()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("OpenAI Endpoint Mode", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope("box"))
            {
                DrawEnvStatus("CODEX_APP_SERVER_BASE_URL", mask: false);
                DrawEnvStatus("CODEX_APP_SERVER_URL", mask: false);
                DrawEnvStatus("OPENAI_ENDPOINT_MODE", mask: false);
                DrawEnvStatus("UNITYLLMAPI_OPENAI_ENDPOINT_MODE", mask: false);
            }

            using (new EditorGUI.DisabledScope(ignoreEditorKeys))
            {
                openAIEndpointMode = (OpenAIEndpointMode)EditorGUILayout.EnumPopup(
                    $"Project Mode ({PrefOpenAIEndpointMode})",
                    openAIEndpointMode);

                codexAppServerBaseUrl = EditorGUILayout.TextField(
                    $"Codex App Server URL ({PrefCodexAppServerBaseUrl})",
                    codexAppServerBaseUrl);
            }

            if (openAIEndpointMode == OpenAIEndpointMode.CodexAppServer)
            {
                EditorGUILayout.HelpBox(
                    "Codex App Server mode expects a WebSocket JSON-RPC endpoint, for example ws://127.0.0.1:4500. http:// and https:// values are converted to ws:// and wss:// at runtime. Enable this mode only when routing existing OpenAI/GPT models to App Server; AIModelType.GPT5_5AppServer only needs the URL.",
                    MessageType.Info);
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
                    DrawEnvStatus(env, mask: true);
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
