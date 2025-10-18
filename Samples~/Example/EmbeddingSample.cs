// 例: 任意の MonoBehaviour から
using System.Collections.Generic;
using UnityEngine;

public class EmbeddingSample : MonoBehaviour
{
    private async void Start()
    {
        // king - man + woman ≒ queen
        var king = await EmbeddingManager.CreateEmbeddingAsync("king");
        var man = await EmbeddingManager.CreateEmbeddingAsync("man");
        var woman = await EmbeddingManager.CreateEmbeddingAsync("woman");

        // 算術
        var query = (king - man + woman).Normalized();  // 仕上げに正規化

        Debug.Log($"コーパス構築");
        var corpusWard = new List<string> { "queen", "king", "woman", "man" };
        var corpus = new List<SerializableEmbedding>();
        for (int i = 0; i < corpusWard.Count; i++) corpus.Add(await EmbeddingManager.CreateEmbeddingAsync(corpusWard[i]));

        // 既存の EmbeddingSimilarity.RankByCosine を使って上位を取得
        Debug.Log($"類似度計算");
        var ranked = EmbeddingManager.RankByCosine(query, corpus, topK: -1);

        for (int i = 0; i < ranked.Count; i++)
        {
            var r = ranked[i];
            var word = corpusWard[r.Index];
            Debug.Log($"{i}: {word} ({r.Score})");
        }
    }
}
