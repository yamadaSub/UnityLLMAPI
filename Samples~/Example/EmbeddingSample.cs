using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// EmbeddingManager を用いたベクトル演算と類似度計算、Hadamard 演算のサンプル。
/// スクリプトをシーンに配置すると Start で自動実行され、結果が Console に出力される。
/// </summary>
public class EmbeddingSample : MonoBehaviour
{
    private async void Start()
    {
        var projected768 = await EmbeddingManager.CreateEmbeddingAsync(
            "example text for 768-dim projection",
            EmmbeddingModelType.Gemini01_768);
        Debug.Log($"[EmbeddingSample] Gemini embedding (dim=768) length: {projected768?.Dimension}");

        // 線形演算後に正規化してコサイン類似度に備える
        var king = await EmbeddingManager.CreateEmbeddingAsync("king", EmmbeddingModelType.Gemini01_768);
        var man = await EmbeddingManager.CreateEmbeddingAsync("man", EmmbeddingModelType.Gemini01_768);
        var woman = await EmbeddingManager.CreateEmbeddingAsync("woman", EmmbeddingModelType.Gemini01_768);

        // 線形演算後に正規化してコサイン類似度に備える
        var query = (king - man + woman).Normalized();

        var corpusWords = new List<string> { "queen", "king", "woman", "man" };
        var corpus = new List<SerializableEmbedding>();
        foreach (var word in corpusWords)
        {
            corpus.Add(await EmbeddingManager.CreateEmbeddingAsync(word, EmmbeddingModelType.Gemini01_768));
        }

        Debug.Log("[EmbeddingSample] 類似度計算 (Cosine)");
        var ranked = EmbeddingManager.RankByCosine(query, corpus, topK: -1);
        for (int i = 0; i < ranked.Count; i++)
        {
            var result = ranked[i];
            Debug.Log($"{i}: {corpusWords[result.Index]} (score={result.Score})");
        }

        // Hadamard 積 / 除算の例（同次元ベクトルが前提）
        var product = king.HadamardProduct(man);
        var quotient = king.HadamardQuotient(man);
        var quotientArray = quotient.ToFloatArray();
        var sampleValue = quotientArray.Length > 0 ? quotientArray[0] : 0f;
        Debug.Log($"[EmbeddingSample] Hadamard 積の要素数: {product.ToFloatArray().Length}, Hadamard 除算の一要素: {sampleValue}");
    }
}
