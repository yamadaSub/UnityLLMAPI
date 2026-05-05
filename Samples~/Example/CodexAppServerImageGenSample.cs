using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityLLMAPI.Chat;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Generates an image through Codex App Server by invoking the $imagegen skill,
/// saves it under Assets, then optionally places it in the scene as a SpriteRenderer.
/// </summary>
public class CodexAppServerImageGenSample : MonoBehaviour
{
    private const AIModelType LlmapiRouteModel = AIModelType.GPT5_5AppServer;

    [Header("Codex App Server")]
    [Tooltip("Optional Codex App Server turn model override. Leave as AppServerDefault to use the app-server configured model.")]
    public CodexAppServerModelType appServerModel = CodexAppServerModelType.AppServerDefault;

    [Tooltip("Timeout for the whole image generation turn. Use -1 to disable.")]
    public int timeoutSeconds = 600;

    [Header("Output")]
    [Tooltip("Unity-project-relative output path. Keep this under Assets so Unity can import it.")]
    public string relativeOutputPath = "Assets/Generated/codex_coin_icon.png";

    [Tooltip("Create or update a child SpriteRenderer using the generated PNG.")]
    public bool placeInScene = false;

    [Tooltip("Pixels per unit for the generated sprite.")]
    public float pixelsPerUnit = 100f;

    [Header("Prompt")]
    [TextArea(3, 10)]
    public string prompt =
        "Unity用の2Dアイコンを1枚生成してください。透明背景のPNG。シンプルなフラットデザインで、金色のコイン。";

    [ContextMenu("Generate Image Asset")]
    public async void GenerateImageAssetAsync()
    {
        var serverUrl = AIManager.CodexAppServerBaseUrl;
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            Debug.Log("[CodexAppServerImageGenSample] Codex App Server URL が未設定です。CODEX_APP_SERVER_BASE_URL、AIManagerBehaviour、または Tools > UnityLLMAPI > Configure API Keys で設定してください。");
            return;
        }

        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var outputPath = Path.GetFullPath(Path.Combine(projectRoot, relativeOutputPath));

        var messages = new List<Message>
        {
            new Message
            {
                role = MessageRole.User,
                content = string.IsNullOrWhiteSpace(prompt)
                    ? "$imagegen Generate one simple Unity icon."
                    : prompt
            }
        };

        var initBody = new Dictionary<string, object>
        {
            { "cwd", projectRoot },
            { "outputPath", relativeOutputPath },
            { "approvalPolicy", "never" },
            {
                "sandboxPolicy",
                new Dictionary<string, object>
                {
                    { "type", "workspaceWrite" },
                    { "writableRoots", new List<string> { projectRoot } },
                    { "networkAccess", true }
                }
            }
        };
        CodexAppServerModelOptions.ApplyTo(initBody, appServerModel);

        Debug.Log($"[CodexAppServerImageGenSample] Requesting $imagegen via {serverUrl}; LLMAPI route={LlmapiRouteModel}; Codex model={CodexAppServerModelOptions.ToDisplayName(appServerModel)}; output={relativeOutputPath}");
        var response = await AIManager.GenerateImagesAsync(
            messages,
            LlmapiRouteModel,
            initBody,
            timeoutSeconds: timeoutSeconds);

        if (response == null || response.images == null || response.images.Count == 0)
        {
            Debug.Log("[CodexAppServerImageGenSample] 生成画像が返りませんでした。Server URL、skill の利用可否、codex app-server のログを確認してください。");
            return;
        }

#if UNITY_EDITOR
        AssetDatabase.Refresh();
#endif

        if (!File.Exists(outputPath))
        {
            Debug.Log("[CodexAppServerImageGenSample] 生成画像の bytes は返りましたが、出力ファイルが見つかりません: " + outputPath);
            return;
        }

        Debug.Log("[CodexAppServerImageGenSample] Generated image saved: " + outputPath);
        if (placeInScene)
        {
            PlaceGeneratedSprite(outputPath);
        }
    }

    private void PlaceGeneratedSprite(string absolutePath)
    {
        var bytes = File.ReadAllBytes(absolutePath);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(bytes))
        {
            Debug.Log("[CodexAppServerImageGenSample] 生成 PNG を Texture2D として読み込めませんでした。");
            return;
        }

        texture.name = Path.GetFileNameWithoutExtension(absolutePath);
        var spritePixelsPerUnit = pixelsPerUnit < 1f ? 1f : pixelsPerUnit;
        var sprite = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            spritePixelsPerUnit);

        const string childName = "Codex Generated Image";
        var child = transform.Find(childName);
        var target = child == null ? new GameObject(childName) : child.gameObject;
        target.transform.SetParent(transform, false);

        var spriteRenderer = target.GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = target.AddComponent<SpriteRenderer>();
        }

        spriteRenderer.sprite = sprite;
    }
}
