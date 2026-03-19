using System;
using System.IO;
using UnityEngine;
using UnityEngine.Video;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnityLLMAPI.Common
{
    internal static class MediaAssetEncodingUtility
    {
        public static bool TryGetWavBytes(AudioClip clip, out byte[] wavBytes, bool logWarnings = true)
        {
            wavBytes = null;
            if (clip == null)
            {
                if (logWarnings) Debug.LogWarning("MediaAssetEncodingUtility.TryGetWavBytes: AudioClip is null.");
                return false;
            }

            if (clip.loadType != AudioClipLoadType.DecompressOnLoad)
            {
                if (logWarnings)
                {
                    Debug.LogWarning($"MediaAssetEncodingUtility.TryGetWavBytes: AudioClip.loadType is {clip.loadType}. GetData requires Load Type = Decompress On Load.");
                }

                return false;
            }

            if (clip.loadState != AudioDataLoadState.Loaded)
            {
                if (logWarnings)
                {
                    Debug.LogWarning("MediaAssetEncodingUtility.TryGetWavBytes: AudioClip data is not loaded. Wait until loadState is Loaded before encoding.");
                }

                return false;
            }

            var sampleCount = clip.samples * clip.channels;
            if (sampleCount <= 0)
            {
                if (logWarnings) Debug.LogWarning("MediaAssetEncodingUtility.TryGetWavBytes: AudioClip does not contain sample data.");
                return false;
            }

            var samples = new float[sampleCount];
            if (!clip.GetData(samples, 0))
            {
                if (logWarnings)
                {
                    Debug.LogWarning("MediaAssetEncodingUtility.TryGetWavBytes: AudioClip.GetData failed. Check Load Type and loadState.");
                }

                return false;
            }

            wavBytes = EncodeWaveFile(samples, clip.channels, clip.frequency);
            return true;
        }

        public static bool TryGetVideoBytes(VideoClip clip, out byte[] videoBytes, out string mimeType, bool logWarnings = true)
        {
            videoBytes = null;
            mimeType = null;
            if (clip == null)
            {
                if (logWarnings) Debug.LogWarning("MediaAssetEncodingUtility.TryGetVideoBytes: VideoClip is null.");
                return false;
            }

#if UNITY_EDITOR
            var assetPath = ResolveVideoClipPath(clip);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                if (logWarnings)
                {
                    Debug.LogWarning("MediaAssetEncodingUtility.TryGetVideoBytes: Could not resolve a source path for the VideoClip asset.");
                }

                return false;
            }

            var fullPath = Path.GetFullPath(assetPath);
            if (!File.Exists(fullPath))
            {
                if (logWarnings)
                {
                    Debug.LogWarning($"MediaAssetEncodingUtility.TryGetVideoBytes: Video source file was not found at {fullPath}.");
                }

                return false;
            }

            videoBytes = File.ReadAllBytes(fullPath);
            mimeType = GuessVideoMimeType(fullPath);
            return videoBytes.Length > 0;
#else
            if (logWarnings)
            {
                Debug.LogWarning("MediaAssetEncodingUtility.TryGetVideoBytes: Direct VideoClip embedding is only supported in the Unity Editor. Use FromVideoData or FromFileUri in player builds.");
            }

            return false;
#endif
        }

        private static byte[] EncodeWaveFile(float[] samples, int channels, int sampleRate)
        {
            var dataSize = samples.Length * sizeof(short);

            using var stream = new MemoryStream(44 + dataSize);
            using var writer = new BinaryWriter(stream);

            WriteFourCc(writer, "RIFF");
            writer.Write(36 + dataSize);
            WriteFourCc(writer, "WAVE");
            WriteFourCc(writer, "fmt ");
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(sampleRate);

            var blockAlign = (short)(channels * sizeof(short));
            var byteRate = sampleRate * blockAlign;
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write((short)16);

            WriteFourCc(writer, "data");
            writer.Write(dataSize);

            for (int i = 0; i < samples.Length; i++)
            {
                var clamped = Mathf.Clamp(samples[i], -1f, 1f);
                writer.Write((short)Mathf.RoundToInt(clamped * short.MaxValue));
            }

            writer.Flush();
            return stream.ToArray();
        }

        private static void WriteFourCc(BinaryWriter writer, string value)
        {
            writer.Write((byte)value[0]);
            writer.Write((byte)value[1]);
            writer.Write((byte)value[2]);
            writer.Write((byte)value[3]);
        }

#if UNITY_EDITOR
        private static string ResolveVideoClipPath(VideoClip clip)
        {
            var assetPath = AssetDatabase.GetAssetPath(clip);
            if (!string.IsNullOrWhiteSpace(assetPath))
            {
                return assetPath;
            }

            return clip.originalPath;
        }
#endif

        private static string GuessVideoMimeType(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext switch
            {
                ".mov" => "video/quicktime",
                ".webm" => "video/webm",
                ".avi" => "video/x-msvideo",
                _ => "video/mp4"
            };
        }
    }
}
