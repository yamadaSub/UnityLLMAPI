using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "EmbeddingAsset", menuName = "AI/Embedding Asset")]
public class EmbeddingAsset : ScriptableObject
{
    [Serializable]
    public class EmbeddingEntry
    {
        [TextArea] public string text;
        public SerializableEmbedding embedding;
        public string model = "text-embedding-3-small";
        public string note;
    }

    public List<EmbeddingEntry> entries = new();
}