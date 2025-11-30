using System;
using System.Collections.Generic;

namespace UnityLLMAPI.Chat
{
    // モデルが対応する機能をビットフラグで管理する
    [Flags]
    public enum AICapabilities
    {
        None = 0,
        TextChat = 1 << 0,
        Vision = 1 << 1,
        JsonSchema = 1 << 2,
        FunctionCalling = 1 << 3,
        ImageGeneration = 1 << 4,
        Embedding = 1 << 5,
    }

    public enum AIProvider
    {
        OpenAI,
        Grok,
        Gemini,
    }

    // 各モデルのメタ情報を集約するデータコンテナ
    public sealed class ModelSpec
    {
        public AIModelType ModelType { get; set; }
        public AIProvider Provider { get; set; }
        public string ModelId { get; set; }
        public AICapabilities Capabilities { get; set; }
        public int MaxContextTokens { get; set; }
    }

    // AIModelType -> ModelSpec を一元管理するレジストリ
    internal static class ModelRegistry
    {
        private static readonly Dictionary<AIModelType, ModelSpec> Models = new Dictionary<AIModelType, ModelSpec>
        {
            {
                AIModelType.GPT4o,
                new ModelSpec
                {
                    ModelType = AIModelType.GPT4o,
                    Provider = AIProvider.OpenAI,
                    ModelId = "gpt-4o",
                    Capabilities = AICapabilities.TextChat
                                   | AICapabilities.Vision
                                   | AICapabilities.JsonSchema
                                   | AICapabilities.FunctionCalling,
                    MaxContextTokens = 128000,
                }
            },
            {
                AIModelType.GPT5,
                new ModelSpec
                {
                    ModelType = AIModelType.GPT5,
                    Provider = AIProvider.OpenAI,
                    ModelId = "gpt-5",
                    Capabilities = AICapabilities.TextChat
                                   | AICapabilities.Vision
                                   | AICapabilities.JsonSchema
                                   | AICapabilities.FunctionCalling,
                    MaxContextTokens = 128000,
                }
            },
            {
                AIModelType.GPT5Mini,
                new ModelSpec
                {
                    ModelType = AIModelType.GPT5Mini,
                    Provider = AIProvider.OpenAI,
                    ModelId = "gpt-5-mini",
                    Capabilities = AICapabilities.TextChat
                                   | AICapabilities.Vision
                                   | AICapabilities.JsonSchema
                                   | AICapabilities.FunctionCalling,
                    MaxContextTokens = 128000,
                }
            },
            {
                AIModelType.GPT5Pro,
                new ModelSpec
                {
                    ModelType = AIModelType.GPT5Pro,
                    Provider = AIProvider.OpenAI,
                    ModelId = "gpt-5-pro",
                    Capabilities = AICapabilities.TextChat
                                   | AICapabilities.Vision
                                   | AICapabilities.JsonSchema
                                   | AICapabilities.FunctionCalling,
                    MaxContextTokens = 128000,
                }
            },
            {
                AIModelType.Grok2,
                new ModelSpec
                {
                    ModelType = AIModelType.Grok2,
                    Provider = AIProvider.Grok,
                    ModelId = "grok-2-latest",
                    Capabilities = AICapabilities.TextChat
                                   | AICapabilities.JsonSchema
                                   | AICapabilities.FunctionCalling,
                    MaxContextTokens = 131072,
                }
            },
            {
                AIModelType.Grok3,
                new ModelSpec
                {
                    ModelType = AIModelType.Grok3,
                    Provider = AIProvider.Grok,
                    ModelId = "grok-3-latest",
                    Capabilities = AICapabilities.TextChat
                                   | AICapabilities.JsonSchema
                                   | AICapabilities.FunctionCalling,
                    MaxContextTokens = 131072,
                }
            },
            {
                AIModelType.Gemini25,
                new ModelSpec
                {
                    ModelType = AIModelType.Gemini25,
                    Provider = AIProvider.Gemini,
                    ModelId = "gemini-2.5-pro",
                    Capabilities = AICapabilities.TextChat
                                   | AICapabilities.Vision
                                   | AICapabilities.JsonSchema
                                   | AICapabilities.FunctionCalling,
                    MaxContextTokens = 1048576,
                }
            },
            {
                AIModelType.Gemini25Pro,
                new ModelSpec
                {
                    ModelType = AIModelType.Gemini25Pro,
                    Provider = AIProvider.Gemini,
                    ModelId = "gemini-2.5-pro",
                    Capabilities = AICapabilities.TextChat
                                   | AICapabilities.Vision
                                   | AICapabilities.JsonSchema
                                   | AICapabilities.FunctionCalling,
                    MaxContextTokens = 1048576,
                }
            },
            {
                AIModelType.Gemini25Flash,
                new ModelSpec
                {
                    ModelType = AIModelType.Gemini25Flash,
                    Provider = AIProvider.Gemini,
                    ModelId = "gemini-2.5-flash",
                    Capabilities = AICapabilities.TextChat
                                   | AICapabilities.Vision
                                   | AICapabilities.JsonSchema
                                   | AICapabilities.FunctionCalling,
                    MaxContextTokens = 1048576,
                }
            },
            {
                AIModelType.Gemini25FlashLite,
                new ModelSpec
                {
                    ModelType = AIModelType.Gemini25FlashLite,
                    Provider = AIProvider.Gemini,
                    ModelId = "gemini-2.5-flash-lite",
                    Capabilities = AICapabilities.TextChat
                                    | AICapabilities.Vision
                                    | AICapabilities.JsonSchema
                                    | AICapabilities.FunctionCalling,
                    MaxContextTokens = 1048576,
                }
            },
            {
                AIModelType.Gemini25FlashImagePreview,
                new ModelSpec
                {
                    ModelType = AIModelType.Gemini25FlashImagePreview,
                    Provider = AIProvider.Gemini,
                    ModelId = "gemini-2.5-flash-image-preview",
                    Capabilities = AICapabilities.ImageGeneration | AICapabilities.Vision,
                    MaxContextTokens = 1048576,
                }
            },
            {
                AIModelType.Gemini3,
                new ModelSpec
                {
                    ModelType = AIModelType.Gemini3,
                    Provider = AIProvider.Gemini,
                    ModelId = "gemini-3.0-pro-exp",
                    Capabilities = AICapabilities.TextChat
                                   | AICapabilities.Vision
                                   | AICapabilities.JsonSchema
                                   | AICapabilities.FunctionCalling,
                    MaxContextTokens = 1048576,
                }
            },
            {
                AIModelType.Gemini3ProImagePreview,
                new ModelSpec
                {
                    ModelType = AIModelType.Gemini3ProImagePreview,
                    Provider = AIProvider.Gemini,
                    ModelId = "gemini-3-pro-image-preview",
                    Capabilities = AICapabilities.ImageGeneration | AICapabilities.Vision,
                    MaxContextTokens = 1048576,
                }
            },
        };

        /// <summary>
        /// 指定モデル種別に対応する <see cref="ModelSpec"/> を取得する。
        /// </summary>
        public static ModelSpec Get(AIModelType modelType)
        {
            if (Models.TryGetValue(modelType, out var spec)) return spec;
            throw new KeyNotFoundException($"ModelSpec not registered for {modelType}");
        }

        /// <summary>
        /// 登録済みの全モデルメタ情報を返す（読み取り専用）。
        /// </summary>
        public static IReadOnlyDictionary<AIModelType, ModelSpec> GetAll() => Models;
    }
}
