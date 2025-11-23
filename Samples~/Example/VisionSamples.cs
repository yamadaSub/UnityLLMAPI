using System.Collections.Generic;
using UnityEngine;
using UnityLLMAPI.Chat;

/// <summary>
/// 画像編集・画像認識ワークフローのサンプル。
/// Gemini 2.5 Flash Image Preview を利用した編集、および Vision モデルへの画像解析を実演する。
/// </summary>
public class VisionSamples : MonoBehaviour
{
    [Header("Image Edit (Gemini 2.5 Flash Image Preview)")]
    [Tooltip("編集の元となるテクスチャ。非 readable でも GPU 読み戻しで処理します。")]
    public Texture2D sourceImage;

    [TextArea]
    [Tooltip("画像編集時にモデルへ伝える指示。空の場合は『水彩画風にしてください。』を使用します。")]
    public string editInstruction = "水彩画風にしてください。";

    [Tooltip("画像生成・編集に使用するモデル。現在は Gemini 2.5 Flash Image Preview / Gemini 3 Pro Image Preview のみがサポートされています。")]
    public AIModelType imageGenerationModel = AIModelType.Gemini25FlashImagePreview;

    [Header("Output Settings")]
    [Tooltip("画像の保存先フォルダパス (Assets からの相対パス)。空の場合は Assets 直下になります。")]
    public string outputFolderPath = "Assets";

    [Header("Image Recognition")]
    [Tooltip("マルチモーダルモデルに解析させる画像。")]
    public Texture2D imageToDescribe;

    [Tooltip("画像認識（Vision）に使用するモデル。GPT-4o や Gemini 2.5 Flash などが指定できます。")]
    public AIModelType recognitionModel = AIModelType.Gemini25Flash;

    [TextArea]
    [Tooltip("画像説明用の追加プロンプト。空の場合は一般的な説明を要求します。")]
    public string recognitionPrompt = "この画像に写っている内容を説明してください。";

    [ContextMenu("Generate Edited Image")]
    public async void GenerateEditedImageAsync()
    {
        if (sourceImage == null)
        {
            Debug.LogWarning("VisionSamples.GenerateEditedImageAsync: sourceImage が未設定です。");
            return;
        }

        // Texture から PNG への変換を含む画像パートを作成（非 readable なテクスチャも自動対応）
        var imagePart = MessageContent.FromImage(sourceImage, logWarnings: true);
        if (imagePart == null)
        {
            Debug.LogWarning("VisionSamples.GenerateEditedImageAsync: 画像のエンコードに失敗しました。Import Settings を確認してください。");
            return;
        }

        // Gemini へ渡すユーザーメッセージ（編集指示 + 元画像）
        var prompts = new List<Message>
        {
            new Message
            {
                role = MessageRole.User,
                parts = new List<MessageContent>
                {
                    MessageContent.FromText(string.IsNullOrWhiteSpace(editInstruction) ? "Add a watercolor effect." : editInstruction),
                    imagePart
                }
            }
        };

        // generationConfig の構築
        var config = new Dictionary<string, object>
        {
            { "responseModalities", new [] { "IMAGE" } }
        };
        
        var initBody = new Dictionary<string, object>
        {
            { "generationConfig", config }
        };

        // 画像生成 API を実行
        var response = await AIManager.GenerateImagesAsync(prompts, imageGenerationModel, initBody);

        if (response?.images.Count > 0)
        {
            var firstImage = response.images[0];
#if UNITY_EDITOR
            // 保存先パスの決定 (Assets からの相対パス)
            string relativeFolder = string.IsNullOrEmpty(outputFolderPath) ? "Assets" : outputFolderPath;
            if (!relativeFolder.StartsWith("Assets")) relativeFolder = System.IO.Path.Combine("Assets", relativeFolder);
            
            // 絶対パスに変換 (IO 用)
            string absoluteFolder = relativeFolder.Replace("Assets", Application.dataPath);
            
            // フォルダが存在しない場合は作成
            if (!System.IO.Directory.Exists(absoluteFolder))
            {
                System.IO.Directory.CreateDirectory(absoluteFolder);
            }

            var fileName = $"gemini_image_{System.DateTime.Now:yyyyMMdd_HHmmss}.png";
            var absolutePath = System.IO.Path.Combine(absoluteFolder, fileName);
            System.IO.File.WriteAllBytes(absolutePath, firstImage.data);
            
            // AssetDatabase を更新してエディタに反映
            UnityEditor.AssetDatabase.Refresh();
            
            var assetPath = System.IO.Path.Combine(relativeFolder, fileName).Replace("\\", "/");
            var texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture != null)
            {
                UnityEditor.Selection.activeObject = texture;
                UnityEditor.EditorGUIUtility.PingObject(texture);
            }
            Debug.Log($"Gemini image saved to {assetPath}");
#else
            Debug.Log($"Gemini image edit generated. Bytes: {firstImage.data?.Length ?? 0} ({firstImage.mimeType})");
#endif
        }
        else
        {
            Debug.LogWarning("VisionSamples.GenerateEditedImageAsync: 画像が返されませんでした。");
        }
    }

    [ContextMenu("Describe Image With Vision Model")]
    public async void DescribeImageAsync()
    {
        if (imageToDescribe == null)
        {
            Debug.LogWarning("VisionSamples.DescribeImageAsync: imageToDescribe が未設定です。");
            return;
        }

        // Vision モデルへ渡す画像パートを生成
        var imagePart = MessageContent.FromImage(imageToDescribe, logWarnings: true);
        if (imagePart == null)
        {
            Debug.LogWarning("VisionSamples.DescribeImageAsync: 画像のエンコードに失敗しました。Import Settings を確認してください。");
            return;
        }

        var promptText = string.IsNullOrWhiteSpace(recognitionPrompt)
            ? "Describe everything you can observe in this picture."
            : recognitionPrompt;

        var messages = new List<Message>
        {
            new Message
            {
                role = MessageRole.User,
                parts = new List<MessageContent>
                {
                    MessageContent.FromText(promptText),
                    imagePart
                }
            }
        };

        // Vision 対応モデルで画像を解析
        var reply = await AIManager.SendMessageAsync(messages, recognitionModel);
        if (string.IsNullOrEmpty(reply))
        {
            Debug.LogWarning($"VisionSamples.DescribeImageAsync: {recognitionModel} から応答が得られませんでした。");
        }
        else
        {
            Debug.Log($"Vision model ({recognitionModel}) response:\n{reply}");
        }
    }
}
