using System.Collections.Generic;
using UnityEngine;

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

    [Header("Image Recognition")]
    [Tooltip("マルチモーダルモデルに解析させる画像。")]
    public Texture2D imageToDescribe;

    [Tooltip("画像入力に対応したモデル。Gemini 2.5 Flash や GPT-4o などを指定できます。")]
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

        // 画像生成 API を実行
        var response = await AIManager.GenerateImagesAsync(prompts, AIModelType.Gemini25FlashImagePreview);

        if (response?.images.Count > 0)
        {
            var firstImage = response.images[0];
#if UNITY_EDITOR
            var outputPath = System.IO.Path.Combine(Application.persistentDataPath, "gemini_image_edit.png");
            System.IO.File.WriteAllBytes(outputPath, firstImage.data);
            Debug.Log($"Gemini image edit saved to {outputPath}");
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
