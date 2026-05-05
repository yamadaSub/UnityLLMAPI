using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityLLMAPI.Chat;

/// <summary>
/// Minimal runnable sample for Codex App Server mode.
/// Configure the Codex App Server URL in UnityLLMAPI settings or AIManagerBehaviour,
/// then add this component to a GameObject and run from the Inspector context menu.
/// </summary>
public class CodexAppServerSample : MonoBehaviour
{
    private const AIModelType LlmapiRouteModel = AIModelType.GPT5_5AppServer;

    [Header("Codex App Server")]
    [Tooltip("Optional Codex App Server turn model override. Leave as AppServerDefault to use the app-server configured model.")]
    public CodexAppServerModelType appServerModel = CodexAppServerModelType.AppServerDefault;

    [Tooltip("When enabled, this sample passes the Unity project root as turn cwd.")]
    public bool useProjectRootAsCwd = true;

    [Tooltip("Timeout for the whole Codex App Server turn. Use -1 to disable.")]
    public int timeoutSeconds = 180;

    [Header("Prompt")]
    [TextArea(3, 8)]
    public string prompt = "Reply in one short sentence: what is the purpose of this Unity package?";

    [Header("Vision")]
    [Tooltip("Optional image for a multimodal Codex App Server turn.")]
    public Texture2D imageToDescribe;

    [TextArea(2, 6)]
    public string visionPrompt = "Describe the image in one concise paragraph.";

    [ContextMenu("Run Codex App Server Chat")]
    public async void RunCodexAppServerChatAsync()
    {
        var serverUrl = AIManager.CodexAppServerBaseUrl;
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            Debug.Log("[CodexAppServerSample] Codex App Server URL が未設定です。CODEX_APP_SERVER_BASE_URL、AIManagerBehaviour、または Tools > UnityLLMAPI > Configure API Keys で設定してください。");
            return;
        }

        var messages = new List<Message>
        {
            new Message
            {
                role = MessageRole.System,
                content = "You are a concise assistant responding to a Unity sample request."
            },
            new Message
            {
                role = MessageRole.User,
                content = string.IsNullOrWhiteSpace(prompt) ? "Say hello from Codex App Server." : prompt
            }
        };

        try
        {
            var initBody = BuildCodexTurnOptions();
            Debug.Log($"[CodexAppServerSample] Sending turn to {serverUrl}. LLMAPI route={LlmapiRouteModel}; Codex model={CodexAppServerModelOptions.ToDisplayName(appServerModel)}.");

            var result = await AIManager.SendMessageStreamAsync(
                messages,
                LlmapiRouteModel,
                initBody,
                timeoutSeconds: timeoutSeconds,
                onContentDelta: delta => Debug.Log($"[CodexAppServerSample delta] {delta}"));

            if (result == null)
            {
                Debug.Log("[CodexAppServerSample] 応答が返りませんでした。Server URL と codex app-server のログを確認してください。");
                return;
            }

            Debug.Log($"[CodexAppServerSample complete]\n{result.Content}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[CodexAppServerSample] Request failed: {ex}");
        }
    }

    [ContextMenu("Describe Image With Codex App Server")]
    public async void DescribeImageWithCodexAppServerAsync()
    {
        if (imageToDescribe == null)
        {
            Debug.Log("[CodexAppServerSample] imageToDescribe が未設定です。");
            return;
        }

        var imagePart = MessageContent.FromImage(imageToDescribe, logWarnings: true);
        if (imagePart == null)
        {
            Debug.Log("[CodexAppServerSample] imageToDescribe のエンコードに失敗しました。");
            return;
        }

        var serverUrl = AIManager.CodexAppServerBaseUrl;
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            Debug.Log("[CodexAppServerSample] Codex App Server URL が未設定です。CODEX_APP_SERVER_BASE_URL、AIManagerBehaviour、または Tools > UnityLLMAPI > Configure API Keys で設定してください。");
            return;
        }

        var messages = new List<Message>
        {
            new Message
            {
                role = MessageRole.User,
                parts = new List<MessageContent>
                {
                    MessageContent.FromText(string.IsNullOrWhiteSpace(visionPrompt)
                        ? "Describe this image."
                        : visionPrompt),
                    imagePart
                }
            }
        };

        try
        {
            var initBody = BuildCodexTurnOptions();
            Debug.Log($"[CodexAppServerSample] Sending multimodal turn to {serverUrl}. LLMAPI route={LlmapiRouteModel}; Codex model={CodexAppServerModelOptions.ToDisplayName(appServerModel)}.");

            var result = await AIManager.SendMessageStreamAsync(
                messages,
                LlmapiRouteModel,
                initBody,
                timeoutSeconds: timeoutSeconds,
                onContentDelta: delta => Debug.Log($"[CodexAppServerSample vision delta] {delta}"));

            if (result == null)
            {
                Debug.Log("[CodexAppServerSample] 画像入力への応答が返りませんでした。Server URL と codex app-server のログを確認してください。");
                return;
            }

            Debug.Log($"[CodexAppServerSample vision complete]\n{result.Content}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[CodexAppServerSample] Vision request failed: {ex}");
        }
    }

    private Dictionary<string, object> BuildCodexTurnOptions()
    {
        var body = new Dictionary<string, object>();
        if (useProjectRootAsCwd)
        {
            body["cwd"] = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        CodexAppServerModelOptions.ApplyTo(body, appServerModel);
        return body;
    }
}
