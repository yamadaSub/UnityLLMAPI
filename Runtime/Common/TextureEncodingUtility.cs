using System;
using UnityEngine;

namespace UnityLLMAPI.Common
{
    /// <summary>
    /// Texture から PNG バイト列を安全に取り出すための補助クラス。
    /// 読み取り不可な Texture2D でも GPU からの読み戻しでフォールバックできるようにする。
    /// </summary>
    internal static class TextureEncodingUtility
    {
        /// <summary>
        /// Texture から PNG 形式のバイト列を生成する。
        /// </summary>
        /// <param name="texture">入力となる Texture (Texture2D / RenderTexture など)。</param>
        /// <param name="pngBytes">生成した PNG バイト列。</param>
        /// <param name="allowGpuReadback">false の場合、GPU 読み戻しを行わず EncodeToPNG のみを試行する。</param>
        /// <param name="logWarnings">true の場合、失敗時に Debug.LogWarning で詳細を出力する。</param>
        /// <returns>PNG 生成に成功した場合は true。</returns>
        internal static bool TryGetPngBytes(Texture texture, out byte[] pngBytes, bool allowGpuReadback = true, bool logWarnings = false)
        {
            pngBytes = null;
            if (texture == null)
            {
                if (logWarnings) Debug.LogWarning("TextureEncodingUtility.TryGetPngBytes: texture is null.");
                return false;
            }

            if (texture is Texture2D tex2D)
            {
                try
                {
                    pngBytes = tex2D.EncodeToPNG();
                    if (pngBytes != null && pngBytes.Length > 0) return true;
                }
                catch (Exception ex) when (ex is UnityException || ex is ArgumentException)
                {
                    // Continue to GPU readback if allowed.
                    if (!allowGpuReadback && logWarnings)
                    {
                        Debug.LogWarning($"TextureEncodingUtility.TryGetPngBytes: EncodeToPNG failed ({ex.Message}).");
                    }
                }
            }

            if (!allowGpuReadback)
            {
                if (logWarnings) Debug.LogWarning("TextureEncodingUtility.TryGetPngBytes: GPU readback disabled and direct EncodeToPNG failed.");
                return false;
            }

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
                if (logWarnings) Debug.LogWarning($"TextureEncodingUtility.TryGetPngBytes: GPU readback failed. {ex.Message}");
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                if (tempRT != null) RenderTexture.ReleaseTemporary(tempRT);
                if (readableCopy != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(readableCopy);
                    else UnityEngine.Object.DestroyImmediate(readableCopy);
                }
            }
        }
    }
}
