using System;

namespace UnityLLMAPI.Schema
{
    /// <summary>
    /// JSON Schema 生成用の範囲制約を付与する属性。
    /// System.ComponentModel.DataAnnotations.RangeAttribute の代替。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class SchemaRangeAttribute : Attribute
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
    /// JSON Schema 生成用の正規表現制約を付与する属性。
    /// System.ComponentModel.DataAnnotations.RegularExpressionAttribute の代替。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class SchemaRegularExpressionAttribute : Attribute
    {
        public string Pattern { get; }

        public SchemaRegularExpressionAttribute(string pattern)
        {
            Pattern = pattern;
        }
    }

    /// <summary>
    /// JSON Schema の enum 相当の制約を付与する属性。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class AllowedValuesAttribute : Attribute
    {
        public string[] Values { get; }

        public AllowedValuesAttribute(params string[] values)
        {
            Values = values ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// JSON Schema の multipleOf 制約を付与する属性。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class SchemaMultipleOfAttribute : Attribute
    {
        public double Step { get; }

        public SchemaMultipleOfAttribute(double step)
        {
            Step = step;
        }
    }
}
