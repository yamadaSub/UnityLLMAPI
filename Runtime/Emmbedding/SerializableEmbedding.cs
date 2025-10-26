using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 埋め込みベクトルを量子化して保持し、Unity 上でのシリアライズを容易にするクラス。
/// 量子化されたデータからの復元や、各種ベクトル演算をサポートする。
/// </summary>
[Serializable]
public class SerializableEmbedding : ISerializationCallbackReceiver
{
    [SerializeField, Tooltip("量子化済みのベクトル値（元の範囲 -127..127 を 0..255 に写像したもの）。")]
    private byte[] quantized;

    [SerializeField, Tooltip("元の float 値に戻す際のスケール値 (max(|x|) / 127)。")]
    private float scale;

    [SerializeField, Tooltip("元のベクトルの次元数。")]
    private int dimension;

    [SerializeField, Tooltip("埋め込みを生成したモデル名。")]
    private string model = string.Empty;

    [NonSerialized] private float[] cache; // 遅延復元キャッシュ

    public string Model => model;
    public int Dimension => dimension;

    public SerializableEmbedding(string modelName)
    {
        model = modelName;
    }

    /// <summary>量子化データを float 配列に復元して返す（初回以降はキャッシュを利用）。</summary>
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
            int v = quantized[i];
            if (v > 127) v -= 256; // sbyte 化
            arr[i] = v * scale;    // 逆量子化
        }
        cache = arr;
        return cache;
    }

    /// <summary>与えられた float 配列を量子化し、内部に保存する。</summary>
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

        float maxAbs = 0f;
        for (int i = 0; i < source.Length; i++)
        {
            float a = Math.Abs(source[i]);
            if (a > maxAbs) maxAbs = a;
        }
        if (maxAbs == 0f) maxAbs = 1e-8f;

        scale = maxAbs / 127f;
        quantized = new byte[dimension];

        for (int i = 0; i < source.Length; i++)
        {
            float q = source[i] / scale;
            q = Mathf.Clamp(q, -127f, 127f);
            sbyte sb = (sbyte)Mathf.Round(q);
            unchecked { quantized[i] = (byte)sb; }
        }

        cache = new float[dimension];
        for (int i = 0; i < dimension; i++)
        {
            int v = quantized[i];
            if (v > 127) v -= 256;
            cache[i] = v * scale;
        }
    }

    #region ユーティリティ

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
        copy.cache = cache != null ? (float[])cache.Clone() : null;
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
    private void EnsureSameDim(SerializableEmbedding other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        if (dimension <= 0 || other.dimension <= 0 || dimension != other.dimension)
            throw new ArgumentException("Embeddings must have the same positive dimension.");
    }

    private string GetResultModel(SerializableEmbedding other)
    {
        if (!string.IsNullOrEmpty(model)) return model;
        return other?.Model ?? string.Empty;
    }

    #endregion

    #region ベクトル演算

    public SerializableEmbedding Add(SerializableEmbedding other)
    {
        EnsureSameDim(other);
        var va = ToFloatArray();
        var vb = other.ToFloatArray();
        var dst = new float[va.Length];
        for (int i = 0; i < dst.Length; i++) dst[i] = va[i] + vb[i];
        return FromFloatArray(dst, GetResultModel(other));
    }

    public SerializableEmbedding Sub(SerializableEmbedding other)
    {
        EnsureSameDim(other);
        var va = ToFloatArray();
        var vb = other.ToFloatArray();
        var dst = new float[va.Length];
        for (int i = 0; i < dst.Length; i++) dst[i] = va[i] - vb[i];
        return FromFloatArray(dst, GetResultModel(other));
    }

    /// <summary>各次元ごとの積を計算する Hadamard 積。</summary>
    public SerializableEmbedding HadamardProduct(SerializableEmbedding other)
    {
        EnsureSameDim(other);
        var va = ToFloatArray();
        var vb = other.ToFloatArray();
        var dst = new float[va.Length];
        for (int i = 0; i < dst.Length; i++) dst[i] = va[i] * vb[i];
        return FromFloatArray(dst, GetResultModel(other));
    }

    /// <summary>各次元ごとの除算を行う Hadamard 除算。分母が 0 に近い場合は epsilon でクランプする。</summary>
    public SerializableEmbedding HadamardQuotient(SerializableEmbedding other, float epsilon = 1e-8f)
    {
        if (epsilon <= 0f) throw new ArgumentOutOfRangeException(nameof(epsilon), "epsilon must be positive.");
        EnsureSameDim(other);
        var va = ToFloatArray();
        var vb = other.ToFloatArray();
        var dst = new float[va.Length];
        for (int i = 0; i < dst.Length; i++)
        {
            var denom = vb[i];
            if (Mathf.Abs(denom) < epsilon)
            {
                denom = denom >= 0f ? epsilon : -epsilon;
            }
            dst[i] = va[i] / denom;
        }
        return FromFloatArray(dst, GetResultModel(other));
    }

    public SerializableEmbedding Scale(float scalar)
    {
        if (dimension <= 0) return Clone();
        var va = ToFloatArray();
        var dst = new float[va.Length];
        for (int i = 0; i < dst.Length; i++) dst[i] = va[i] * scalar;
        return FromFloatArray(dst, model);
    }

    /// <summary>正規化ベクトル（新規インスタンス）。自身は変更しない。</summary>
    public SerializableEmbedding Normalized()
    {
        if (dimension <= 0) return Clone();
        var v = ToFloatArray();
        double n2 = 0d;
        for (int i = 0; i < v.Length; i++) n2 += v[i] * v[i];
        double n = Math.Sqrt(n2);
        if (n <= 0d) return Clone();
        var dst = new float[v.Length];
        float inv = (float)(1.0 / n);
        for (int i = 0; i < v.Length; i++) dst[i] = v[i] * inv;
        return FromFloatArray(dst, model);
    }

    /// <summary>自身を正規化して上書き（量子化も更新）。</summary>
    public void NormalizeInPlace()
    {
        if (dimension <= 0) return;
        var v = ToFloatArray();
        double n2 = 0d;
        for (int i = 0; i < v.Length; i++) n2 += v[i] * v[i];
        double n = Math.Sqrt(n2);
        if (n <= 0d) return;
        float inv = (float)(1.0 / n);
        for (int i = 0; i < v.Length; i++) v[i] *= inv;
        SetFromFloatArray(v);
    }

    /// <summary>内積</summary>
    public float Dot(SerializableEmbedding other)
    {
        EnsureSameDim(other);
        var va = ToFloatArray();
        var vb = other.ToFloatArray();
        double sum = 0d;
        for (int i = 0; i < va.Length; i++) sum += va[i] * vb[i];
        return (float)sum;
    }

    /// <summary>コサイン類似度（0除算は0）。</summary>
    public float CosineSimilarity(SerializableEmbedding other)
    {
        EnsureSameDim(other);
        var va = ToFloatArray();
        var vb = other.ToFloatArray();
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

    #endregion

    #region Aggregation helpers

    public static SerializableEmbedding Average(IList<SerializableEmbedding> list, bool normalize)
    {
        if (list == null || list.Count == 0) return null;

        int dim = FindFirstValidDimension(list);
        if (dim <= 0) return null;

        var sum = new float[dim];
        int count = 0;

        foreach (var e in list)
        {
            if (e == null || e.dimension != dim) continue;
            var v = e.ToFloatArray();
            for (int k = 0; k < dim; k++) sum[k] += v[k];
            count++;
        }
        if (count == 0) return null;

        float inv = 1f / count;
        for (int k = 0; k < dim; k++) sum[k] *= inv;

        if (normalize) NormalizeVector(sum);

        return FromFloatArray(sum, list[0].Model);
    }

    public static SerializableEmbedding WeightedSum(IList<SerializableEmbedding> list, IList<float> weights, bool normalize)
    {
        if (list == null || weights == null || list.Count == 0 || list.Count != weights.Count) return null;

        int dim = FindFirstValidDimension(list);
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

        if (normalize) NormalizeVector(sum);

        return FromFloatArray(sum, list[0].Model);
    }

    private static int FindFirstValidDimension(IEnumerable<SerializableEmbedding> list)
    {
        foreach (var e in list)
        {
            if (e != null && e.dimension > 0) return e.dimension;
        }
        return -1;
    }

    private static void NormalizeVector(float[] vec)
    {
        double n2 = 0d;
        for (int i = 0; i < vec.Length; i++) n2 += vec[i] * vec[i];
        double n = Math.Sqrt(n2);
        if (n > 0d)
        {
            float inv = (float)(1.0 / n);
            for (int i = 0; i < vec.Length; i++) vec[i] *= inv;
        }
    }

    #endregion

    #region Operators

    public static SerializableEmbedding operator +(SerializableEmbedding a, SerializableEmbedding b) => a.Add(b);
    public static SerializableEmbedding operator -(SerializableEmbedding a, SerializableEmbedding b) => a.Sub(b);

    public static SerializableEmbedding operator -(SerializableEmbedding a)
    {
        if (a == null) throw new ArgumentNullException(nameof(a));
        if (a.dimension <= 0) return a.Clone();
        var v = a.ToFloatArray();
        var dst = new float[v.Length];
        for (int i = 0; i < dst.Length; i++) dst[i] = -v[i];
        return FromFloatArray(dst, a.Model);
    }

    public static SerializableEmbedding operator *(SerializableEmbedding a, float s) => a.Scale(s);
    public static SerializableEmbedding operator *(float s, SerializableEmbedding a) => a.Scale(s);

    public static SerializableEmbedding operator /(SerializableEmbedding a, float s)
    {
        if (s == 0f) throw new DivideByZeroException();
        return a.Scale(1f / s);
    }

    #endregion

    #region Serialization callback

    // JSON/Binary シリアライズ前後でのフック（ここでは何もしないが明示的に残す）
    public void OnBeforeSerialize() { }

    public void OnAfterDeserialize()
    {
        // ロード直後は未復元状態（必要時に ToFloatArray で復元）
        cache = null;
    }

    #endregion
}
