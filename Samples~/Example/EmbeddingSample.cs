// ó·: îCà”ÇÃ MonoBehaviour Ç©ÇÁ
using System.Collections.Generic;
using UnityEngine;

public class EmbeddingSample : MonoBehaviour
{
    private async void Start()
    {
        // king - man + woman Å‡ queen
        var king = await EmbeddingManager.CreateEmbeddingAsync("king");
        var man = await EmbeddingManager.CreateEmbeddingAsync("man");
        var woman = await EmbeddingManager.CreateEmbeddingAsync("woman");

        // éZèp
        var query = (king - man + woman).Normalized();  // édè„Ç∞Ç…ê≥ãKâª

        Debug.Log($"ÉRÅ[ÉpÉXç\íz");
        var corpusWard = new List<string> { "queen", "king", "woman", "man" };
        var corpus = new List<SerializableEmbedding>();
        for (int i = 0; i < corpusWard.Count; i++) corpus.Add(await EmbeddingManager.CreateEmbeddingAsync(corpusWard[i]));

        // ä˘ë∂ÇÃ EmbeddingSimilarity.RankByCosine ÇégÇ¡Çƒè„à ÇéÊìæ
        Debug.Log($"óﬁéóìxåvéZ");
        var ranked = EmbeddingManager.RankByCosine(query, corpus, topK: -1);

        for (int i = 0; i < ranked.Count; i++)
        {
            var r = ranked[i];
            var word = corpusWard[r.Index];
            Debug.Log($"{i}: {word} ({r.Score})");
        }
    }
}
