using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 埋め込みベクトルを「int8量子化 + scale」で軽量シリアライズする型。
/// ScriptableObject に大量格納しても読み込み負荷が小さい。
/// </summary>
[Serializable]
public class SerializableEmbedding : ISerializationCallbackReceiver
{
    [SerializeField, Tooltip("量子化済みのデータ（-127..127）")]
    private byte[] quantized; // 実体は sbyte を byte に詰め替えたもの
    [SerializeField, Tooltip("復元用スケール（max(|x|) / 127f）")]
    private float scale;
    [SerializeField, Tooltip("元の次元数（復元の整合性用）")]
    private int dimension;
    [SerializeField, Tooltip("元の次元数（復元の整合性用）")]
    private string model =string.Empty; // どのモデルで作成したかのメタ情報


    [NonSerialized] private float[] cache; // 遅延復元キャッシュ

    public string Model => model;
    public int Dimension => dimension;

    public SerializableEmbedding(string modelName) 
    {
        model = modelName;
    }
    /// <summary>現在の量子化データを float 配列へ復元（初回のみデコード＆キャッシュ）</summary>
    public float[] ToFloatArray()
    {
        if (cache != null) return cache;
        if (quantized == null || quantized.Length == 0 || dimension <= 0)
        {
            cache = Array.Empty<float>();
            return cache;
        }
        var arr = new float[dimension];
        for (int i = 0; i < dimension; i++)
        {
            // byte(0..255) → sbyte(-128..127) 注意: -128 はクリップされ得るので符号拡張に気をつける
            int v = quantized[i];
            if (v > 127) v -= 256; // sbyte 化
            arr[i] = v * scale;    // 逆量子化
        }
        cache = arr;
        return cache;
    }

    /// <summary>float 配列から量子化して格納。大きな配列も低コストで保存可能。</summary>
    public void SetFromFloatArray(ReadOnlySpan<float> source)
    {
        dimension = source.Length;
        if (dimension == 0)
        {
            quantized = Array.Empty<byte>();
            scale = 0f;
            cache = Array.Empty<float>();
            return;
        }

        // 最大絶対値を取得（スケーリング因子）
        float maxAbs = 0f;
        for (int i = 0; i < source.Length; i++)
        {
            float a = Math.Abs(source[i]);
            if (a > maxAbs) maxAbs = a;
        }
        // 全て0なら量子化不要
        if (maxAbs == 0f) { maxAbs = 1e-8f; }

        scale = maxAbs / 127f;
        quantized = new byte[dimension];

        for (int i = 0; i < source.Length; i++)
        {
            float q = source[i] / scale;
            // クリップ
            if (q > 127f) q = 127f;
            else if (q < -127f) q = -127f;
            sbyte sb = (sbyte)Math.Round(q);
            unchecked { quantized[i] = (byte)sb; }
        }

        // キャッシュも用意しておく（直後に使うケース最適化）
        cache = new float[dimension];
        for (int i = 0; i < dimension; i++)
        {
            int v = quantized[i];
            if (v > 127) v -= 256;
            cache[i] = v * scale;
        }
    }

    #region Word2Vec算術演算用のユーティリティ
    /*
    Word2Vec豆知識～各算術演算で何が起きるか～

    加重和（平均）：複数のベクトルの重心（中心点）を移動させる。
        つまり、「みんなの中間点」に移動する。
        （例１：「犬」と「猫」の平均 → 「ペット」方向に近づく）
        （例２：0.8×「今日は晴れ」＋0.2×「体育祭が始まる」 → 「天気の話が中心」）
    内積：類似度を測る。大きいほど似ている。
        （例１：「晴れ」と「快晴」→ 内積が大きい（方向が近い））
        （例２：「晴れ」と「雨」→ 内積が小さい（方向が遠い））
    正規化(コサイン類似度)：「意味の方向」だけを純粋に見る。出現頻度や強さの影響を取り除く。
        （例１：「晴れ」(よく出る単語で長いベクトル)）
        （例２：「曇天」(珍しい単語で短いベクトル)）
        →正規化すると両方とも“天気”方向の純粋な意味として比較可能。
        ※コサイン類似度は正規化後の内積。
    減算：「AからBを引く」→「AにあってBにないもの」を強調する。
        （例１：「王様」-「男」+「女」→「女王様」）
        （例２：「猫」-「ペット」+「犬」→「犬」）
    スカラー倍：ベクトルの「強さ」を変える。
        （例１：2倍 → 重要度が増す（正規化前にやると影響大））
        （例２：-1倍 → 意味が反転する（意味的な対義語と直接相関があるわけではない点に注意））
     */

    /// <summary>ディープコピー（量子化バッファを複製）</summary>
    public SerializableEmbedding Clone()
    {
        var copy = new SerializableEmbedding(model);
        if (quantized != null)
        {
            var q = new byte[quantized.Length];
            Buffer.BlockCopy(quantized, 0, q, 0, quantized.Length);
            copy.quantized = q;
        }
        copy.scale = scale;
        copy.dimension = dimension;
        copy.cache = null; // 遅延復元
        return copy;
    }

    /// <summary>float配列の新規Embeddingを作るヘルパ</summary>
    public static SerializableEmbedding FromFloatArray(float[] src, string modelName)
    {
        var e = new SerializableEmbedding(modelName);
        e.SetFromFloatArray(src);
        return e;
    }

    /// <summary>同次元チェック。違えば例外。</summary>
    private void EnsureSameDim(SerializableEmbedding b)
    {
        if (b == null) throw new ArgumentNullException("Embedding is null.");
        if (dimension <= 0 || b.dimension <= 0 || dimension != b.dimension)
            throw new ArgumentException("Embeddings must have the same positive dimension.");
    }

    /// <summary>要素ごとの加算（新規インスタンス）。</summary>
    public SerializableEmbedding Add(SerializableEmbedding b)
    {
        EnsureSameDim(b);
        var va = ToFloatArray();
        var vb = b.ToFloatArray();
        var dst = new float[va.Length];
        for (int i = 0; i < dst.Length; i++) dst[i] = va[i] + vb[i];
        return FromFloatArray(dst,Model);
    }

    /// <summary>要素ごとの減算（新規インスタンス）。</summary>
    public SerializableEmbedding Sub(SerializableEmbedding b)
    {
        EnsureSameDim(b);
        var va = ToFloatArray();
        var vb = b.ToFloatArray();
        var dst = new float[va.Length];
        for (int i = 0; i < dst.Length; i++) dst[i] = va[i] - vb[i];
        return FromFloatArray(dst, Model);
    }

    /// <summary>スカラー倍（新規インスタンス）。</summary>
    public SerializableEmbedding Scale(float s)
    {
        if (dimension <= 0) return Clone();
        var va = ToFloatArray();
        var dst = new float[va.Length];
        for (int i = 0; i < dst.Length; i++) dst[i] = va[i] * s;
        return FromFloatArray(dst, Model);
    }

    /// <summary>正規化ベクトル（新規インスタンス）。自身は変更しない。</summary>
    public SerializableEmbedding Normalized()
    {
        if (dimension <= 0) return Clone();
        var v = ToFloatArray();
        double n2 = 0d; for (int i = 0; i < v.Length; i++) n2 += v[i] * v[i];
        double n = Math.Sqrt(n2);
        if (n <= 0d) return Clone();
        var dst = new float[v.Length];
        float inv = (float)(1.0 / n);
        for (int i = 0; i < v.Length; i++) dst[i] = v[i] * inv;
        return FromFloatArray(dst,Model);
    }

    /// <summary>自身を正規化して上書き（量子化も更新）。</summary>
    public void NormalizeInPlace()
    {
        if (dimension <= 0) return;
        var v = ToFloatArray();
        double n2 = 0d; for (int i = 0; i < v.Length; i++) n2 += v[i] * v[i];
        double n = Math.Sqrt(n2);
        if (n <= 0d) return;
        float inv = (float)(1.0 / n);
        for (int i = 0; i < v.Length; i++) v[i] *= inv;
        SetFromFloatArray(v); // 再量子化して保存
    }

    /// <summary>内積</summary>
    public float Dot(SerializableEmbedding b)
    {
        EnsureSameDim(b);
        var va = ToFloatArray();
        var vb = b.ToFloatArray();
        double sum = 0d;
        for (int i = 0; i < va.Length; i++) sum += va[i] * vb[i];
        return (float)sum;
    }

    /// <summary>コサイン類似度（0除算は0）。</summary>
    public float CosineSimilarity(SerializableEmbedding b)
    {
        EnsureSameDim(b);
        var va = ToFloatArray();
        var vb = b.ToFloatArray();
        double dot = 0d, na = 0d, nb = 0d;
        for (int i = 0; i < va.Length; i++)
        {
            double x = va[i];
            double y = vb[i];
            dot += x * y;
            na += x * x;
            nb += y * y;
        }
        double denom = Math.Sqrt(na) * Math.Sqrt(nb);
        if (denom <= 0d) return 0f;
        return (float)(dot / denom);
    }

    /// <summary>平均（空や null を除外）。正規化した平均にしたい場合は normalize=true。</summary>
    public static SerializableEmbedding Average(IList<SerializableEmbedding> list, bool normalize)
    {
        if (list == null || list.Count == 0) return null;

        // 最初の有効次元を探す
        int dim = -1;
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            if (e != null && e.dimension > 0) { dim = e.dimension; break; }
        }
        if (dim <= 0) return null;

        var sum = new float[dim];
        int cnt = 0;

        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            if (e == null || e.dimension != dim) continue;
            var v = e.ToFloatArray();
            for (int k = 0; k < dim; k++) sum[k] += v[k];
            cnt++;
        }
        if (cnt == 0) return null;

        float inv = 1f / cnt;
        for (int k = 0; k < dim; k++) sum[k] *= inv;

        if (normalize)
        {
            double n2 = 0d; for (int k = 0; k < dim; k++) n2 += sum[k] * sum[k];
            double n = Math.Sqrt(n2);
            if (n > 0d)
            {
                float invn = (float)(1.0 / n);
                for (int k = 0; k < dim; k++) sum[k] *= invn;
            }
        }

        return FromFloatArray(sum, list[0].Model);
    }

    /// <summary>加重和（weights.Count == list.Count を想定）。必要なら正規化。</summary>
    public static SerializableEmbedding WeightedSum(IList<SerializableEmbedding> list, IList<float> weights, bool normalize)
    {
        if (list == null || weights == null || list.Count == 0 || list.Count != weights.Count) return null;

        // 次元決定
        int dim = -1;
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            if (e != null && e.dimension > 0) { dim = e.dimension; break; }
        }
        if (dim <= 0) return null;

        var sum = new float[dim];
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            float w = weights[i];
            if (e == null || e.dimension != dim || w == 0f) continue;
            var v = e.ToFloatArray();
            for (int k = 0; k < dim; k++) sum[k] += v[k] * w;
        }

        if (normalize)
        {
            double n2 = 0d; for (int k = 0; k < dim; k++) n2 += sum[k] * sum[k];
            double n = Math.Sqrt(n2);
            if (n > 0d)
            {
                float invn = (float)(1.0 / n);
                for (int k = 0; k < dim; k++) sum[k] *= invn;
            }
        }

        return FromFloatArray(sum, list[0].Model);
    }
    #endregion

    #region 演算子オーバーロード
    public static SerializableEmbedding operator +(SerializableEmbedding a, SerializableEmbedding b) { return a.Add(b); }
    public static SerializableEmbedding operator -(SerializableEmbedding a, SerializableEmbedding b) { return a.Sub(b); }
    public static SerializableEmbedding operator -(SerializableEmbedding a)
    {
        if (a == null) throw new ArgumentNullException("Embedding is null.");
        if (a.dimension <= 0) return a.Clone();
        var v = a.ToFloatArray();
        var dst = new float[v.Length];
        for (int i = 0; i < dst.Length; i++) dst[i] = -v[i];
        return FromFloatArray(dst, a.Model);
    }
    public static SerializableEmbedding operator *(SerializableEmbedding a, float s) { return a.Scale(s); }
    public static SerializableEmbedding operator *(float s, SerializableEmbedding a) { return a.Scale(s); }
    public static SerializableEmbedding operator /(SerializableEmbedding a, float s)
    {
        if (s == 0f) throw new DivideByZeroException();
        return a.Scale(1f / s);
    }
    #endregion


    // JSON/Binary シリアライズ前後でのフック（ここでは何もしないが明示的に残す）
    public void OnBeforeSerialize() { }
    public void OnAfterDeserialize()
    {
        // ロード直後は未復元状態（必要時に ToFloatArray で復元）
        cache = null;
    }
}
