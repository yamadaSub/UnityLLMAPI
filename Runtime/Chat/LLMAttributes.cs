using System;

/// <summary>
/// JSON Schema 生成用の範囲制約属性。
/// System.ComponentModel.DataAnnotations.RangeAttribute の代わりに使用します。
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class SchemaRangeAttribute : Attribute
{
    public double Minimum { get; }
    public double Maximum { get; }

    public SchemaRangeAttribute(double minimum, double maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
    }

    public SchemaRangeAttribute(int minimum, int maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
    }
}

/// <summary>
/// JSON Schema 生成用の正規表現制約属性。
/// System.ComponentModel.DataAnnotations.RegularExpressionAttribute の代わりに使用します。
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class SchemaRegularExpressionAttribute : Attribute
{
    public string Pattern { get; }

    public SchemaRegularExpressionAttribute(string pattern)
    {
        Pattern = pattern;
    }
}
