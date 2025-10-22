using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// EmbeddingManager を利用して単語ベクトルの演算と類似度計算を行うサンプル。
/// Start で自動的に実行され、結果は Console に出力される。
/// </summary>
public class EmbeddingSample : MonoBehaviour
{
    private async void Start()
    {
        // king - man + woman ≒ queen を再現する例
        var king = await EmbeddingManager.CreateEmbeddingAsync("king");
        var man = await EmbeddingManager.CreateEmbeddingAsync("man");
        var woman = await EmbeddingManager.CreateEmbeddingAsync("woman");

        // ベクトルの線形演算後、Cosine 計算に備えて正規化
        var query = (king - man + woman).Normalized();

        var corpusWords = new List<string> { "queen", "king", "woman", "man" };
        var corpus = new List<SerializableEmbedding>();
        foreach (var word in corpusWords)
        {
            corpus.Add(await EmbeddingManager.CreateEmbeddingAsync(word));
        }

        Debug.Log("[EmbeddingSample] 類似度計算 (Cosine)");
        var ranked = EmbeddingManager.RankByCosine(query, corpus, topK: -1);

        for (int i = 0; i < ranked.Count; i++)
        {
            var result = ranked[i];
            Debug.Log($"{i}: {corpusWords[result.Index]} (score={result.Score})");
        }
    }
}
