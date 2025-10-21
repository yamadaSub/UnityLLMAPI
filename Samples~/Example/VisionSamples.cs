using System;
using System.Collections.Generic;
using UnityEngine;

public class VisionSamples : MonoBehaviour
{
    [Header("Image Edit (Gemini 2.5 Flash Image Preview)")]
    [Tooltip("Source texture that will be edited. Texture must be readable so EncodeToPNG succeeds.")]
    public Texture2D sourceImage;

    [TextArea]
    [Tooltip("Instruction describing how the source image should be edited.")]
    public string editInstruction = "水彩画風にしてください。";

    [Header("Image Recognition")]
    [Tooltip("Image that will be described by a multimodal model.")]
    public Texture2D imageToDescribe;

    [Tooltip("Vision-capable model to use for recognition. Gemini 2.5 Flash and GPT-4o both support image prompts.")]
    public AIModelType recognitionModel = AIModelType.Gemini25Flash;

    [TextArea]
    [Tooltip("Prompt that accompanies the recognition image. Leave empty to use a default prompt.")]
    public string recognitionPrompt = "この画像に写っている内容を説明してください。";

    [ContextMenu("Generate Edited Image")]
    public async void GenerateEditedImageAsync()
    {
        if (sourceImage == null)
        {
            Debug.LogWarning("VisionSamples.GenerateEditedImageAsync: Assign a readable sourceImage texture.");
            return;
        }

        if (!TryGetPngBytes(sourceImage, out var imageBytes))
        {
            Debug.LogWarning("VisionSamples.GenerateEditedImageAsync: Failed to encode source image to PNG. Ensure the texture is marked readable or supported for GPU readback.");
            return;
        }

        var prompts = new List<Message>
        {
            new Message
            {
                role = MessageRole.User,
                parts = new List<MessageContent>
                {
                    MessageContent.FromText(string.IsNullOrWhiteSpace(editInstruction) ? "Add a watercolor effect." : editInstruction),
                    MessageContent.FromImageData(imageBytes, "image/png")
                }
            }
        };

        var response = await AIManager.GenerateImagesAsync(
            prompts,
            AIModelType.Gemini25FlashImagePreview);

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
            Debug.LogWarning("VisionSamples.GenerateEditedImageAsync: No image returned from Gemini.");
        }
    }

    [ContextMenu("Describe Image With Vision Model")]
    public async void DescribeImageAsync()
    {
        if (imageToDescribe == null)
        {
            Debug.LogWarning("VisionSamples.DescribeImageAsync: Assign an imageToDescribe texture.");
            return;
        }

        if (!TryGetPngBytes(imageToDescribe, out var imageBytes))
        {
            Debug.LogWarning("VisionSamples.DescribeImageAsync: Failed to encode image to PNG. Ensure the texture is readable.");
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
                    MessageContent.FromImageData(imageBytes, "image/png")
                }
            }
        };

        var reply = await AIManager.SendMessageAsync(messages, recognitionModel);
        if (string.IsNullOrEmpty(reply))
        {
            Debug.LogWarning($"VisionSamples.DescribeImageAsync: No response returned from {recognitionModel}.");
        }
        else
        {
            Debug.Log($"Vision model ({recognitionModel}) response:\n{reply}");
        }
    }

    private static bool TryGetPngBytes(Texture2D texture, out byte[] pngBytes)
    {
        pngBytes = null;
        if (texture == null) return false;

        try
        {
            pngBytes = texture.EncodeToPNG();
            if (pngBytes != null && pngBytes.Length > 0) return true;
        }
        catch (Exception ex) when (ex is UnityException || ex is ArgumentException)
        {
            // Fall through to GPU copy path.
        }

        // Attempt to read back via RenderTexture for non-readable textures
        RenderTexture tempRT = null;
        Texture2D readableCopy = null;
        var previous = RenderTexture.active;
        try
        {
            tempRT = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(texture, tempRT);

            RenderTexture.active = tempRT;
            readableCopy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            readableCopy.ReadPixels(new Rect(0, 0, tempRT.width, tempRT.height), 0, 0);
            readableCopy.Apply();
            pngBytes = readableCopy.EncodeToPNG();
            return pngBytes != null && pngBytes.Length > 0;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"VisionSamples: GPU readback failed. {ex.Message}");
            return false;
        }
        finally
        {
            RenderTexture.active = previous;
            if (tempRT != null) RenderTexture.ReleaseTemporary(tempRT);
            if (readableCopy != null)
            {
                if (Application.isPlaying) Destroy(readableCopy);
                else DestroyImmediate(readableCopy);
            }
        }
    }
}
