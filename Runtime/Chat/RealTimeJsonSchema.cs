using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;




/// <summary>
/// 各パラメータの定義および値を保持するクラスです。
/// 値はすべて文字列で保持し、GetValue() で ParameterType に応じた型に変換します。
/// </summary>
[Serializable]
public class SchemaParameter
{
    [Tooltip("パラメータ名（必須）")]
    public string ParameterName;

    [Tooltip("パラメータの説明")]
    public string Description;

    [Tooltip("パラメータの型")]
    public SchemaParameterType ParameterType;

    [Tooltip("パラメータの値（すべて文字列として入力してください）")]
    [FlexibleInput("ParameterType")]
    public string Value;

    public bool Required = true;
    public string[] Enum;

    /// <summary>
    /// ParameterType に合わせた型に変換した値を返します。
    /// 変換に失敗した場合は、Number は 0、Boolean は false、DateTime は null を返します。
    /// </summary>
    public object GetValue()
    {
        switch (ParameterType)
        {
            case SchemaParameterType.String:
                return Value;
            case SchemaParameterType.Number:
                if (float.TryParse(Value, out float num))
                    return num;
                return 0;
            case SchemaParameterType.Boolean:
                if (bool.TryParse(Value, out bool b))
                    return b;
                return false;
            case SchemaParameterType.DateTime:
                if (DateTime.TryParse(Value, out DateTime dt))
                    return dt.ToString("o"); // ISO 8601 形式
                return null;
            case SchemaParameterType.None:
                return null;
            default:
                return Value;
        }
    }

    public Dictionary<string, object> ToJsonSchemaPiece()
        => JsonSchemaGenerator.CreatePrimitiveSchema(ParameterType, Description, Enum);

    public virtual string GenerateMarkDown(string description = null)
    {
        if (string.IsNullOrEmpty(ParameterName))
            return null;
        string result = ($"* {ParameterName} : {Value}");
        if (ParameterType == SchemaParameterType.None)
        {
            result = ($"* {ParameterName}");
        }
        if (!string.IsNullOrEmpty(description))
        {
            result += ($"\n  * {description}");
        }
        else if (!string.IsNullOrEmpty(Description))
        {
            result +=($"\n  * {Description}");
        }
        return result;
    }
}


public interface IJsonSchema:ICloneable
{
    public string Name { get; }
    public Dictionary<string, object> GenerateJsonSchema();
    public void PerseValueDict(Dictionary<string, object> dict);
}

/// <summary>
/// スキーマ名と複数のパラメータ定義を保持し、
/// そこから JSON Schema、値オブジェクト、そしてマークダウン形式の説明を自動生成するクラスです。
/// </summary>
[Serializable]
public class RealTimeJsonSchema<T> :IJsonSchema where T : SchemaParameter
{
    [Tooltip("スキーマ名")]
    public string Name { get; }

    [Tooltip("パラメータ定義の配列")]
    public T[] Parameters = new T[0];

    public RealTimeJsonSchema(string name)
    {
        Name = name;
    }

    public virtual Dictionary<string, object> GenerateJsonSchema(Func<T, bool> filter = null)
    {
        var members = Parameters?
            .Where(p => !string.IsNullOrEmpty(p.ParameterName) &&
                        (filter == null || filter(p)))
            .Select(p => (p.ParameterName, p.ToJsonSchemaPiece()))
            .ToList() ?? new List<(string, Dictionary<string, object>)>();
        return new Dictionary<string, object>{
            { "name", Name },
            { "schema", JsonSchemaGenerator.BuildObjectSchema(members)}
        };
    }

    /// <summary>
    /// 引数ナシ版
    /// </summary>
    /// <returns></returns>
    public Dictionary<string, object> GenerateJsonSchema() => GenerateJsonSchema(null);

    /// <summary>
    /// 現在設定されている各パラメータの値を、パラメータ名をキーとした Dictionary として返します。
    /// API への値渡しなどに利用できます。
    /// </summary>
    public Dictionary<string, object> GenerateValuesObject(Func<T, bool> filter = null)
    {
        Dictionary<string, object> values = new Dictionary<string, object>();
        if (Parameters != null)
        {
            foreach (var param in Parameters)
            {
                if (string.IsNullOrEmpty(param.ParameterName) || (filter != null && !filter(param)))
                    continue;
                values[param.ParameterName] = param.GetValue();
            }
        }
        return values;
    }

    /// <summary>
    /// 各パラメータの説明をマークダウン形式で生成します。
    /// ヘッダレベル（数字）を指定でき、SchemaName を見出しとして使用します。
    /// 例（headerLevel=2 の場合）:
    /// ## 請求書番号
    /// * invoiceNumber : INV1234 
    ///   * 請求書番号
    /// </summary>
    public virtual string GenerateMarkDown(string header = null, int headerLevel = 2, Func<T, bool> filter = null)
    {
        StringBuilder sb = new StringBuilder();
        string headerPrefix = new string('#', headerLevel);
        sb.AppendLine($"{headerPrefix} {(string.IsNullOrEmpty(header) ? Name : header)}");

        if (Parameters != null)
        {
            foreach (var param in Parameters)
            {
                if (string.IsNullOrEmpty(param.ParameterName) || (filter != null && !filter(param)))
                    continue;
                var markdown = param.GenerateMarkDown();
                if (!string.IsNullOrEmpty(markdown))
                {
                    sb.AppendLine(markdown);
                }
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// schema に定義された各パラメータの名前に対応する値を Value に設定
    /// </summary>
    public virtual void PerseValueDict(Dictionary<string, object> valuesDict)
    {
        if (Parameters != null)
        {
            foreach (var param in Parameters)
            {
                if (!string.IsNullOrEmpty(param.ParameterName) && valuesDict.ContainsKey(param.ParameterName))
                {
                    param.Value = valuesDict[param.ParameterName]?.ToString();
                }
            }
        }
    }

    public virtual object Clone()
    {
        var json = JsonConvert.SerializeObject(this);
        return JsonConvert.DeserializeObject<T>(json);
    }
}

public class FlexibleInputAttribute : PropertyAttribute
{
    public string EnumFieldName;

    public FlexibleInputAttribute(string enumFieldName)
    {
        EnumFieldName = enumFieldName;
    }
}

#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(FlexibleInputAttribute))]
public class FlexibleInputDrawer : PropertyDrawer
{
    private SerializedProperty FindRelativeProperty(SerializedProperty property, string name)
    {
        string path = property.propertyPath;
        string prefix = path.Substring(0, path.LastIndexOf('.'));
        return property.serializedObject.FindProperty($"{prefix}.{name}");
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        FlexibleInputAttribute attr = (FlexibleInputAttribute)attribute;
        SerializedProperty enumProp = FindRelativeProperty(property, attr.EnumFieldName);

        if (enumProp == null || enumProp.propertyType != SerializedPropertyType.Enum)
        {
            EditorGUI.LabelField(position, label.text, "invalidEnumReference");
            return;
        }

        SchemaParameterType type = (SchemaParameterType)enumProp.enumValueIndex;

        switch (type)
        {
            case SchemaParameterType.String:
            case SchemaParameterType.DateTime:
                property.stringValue = EditorGUI.TextField(position, label, property.stringValue);
                break;
            case SchemaParameterType.Number:
                if (float.TryParse(property.stringValue, out float f))
                {
                    f = EditorGUI.FloatField(position, label, f);
                }
                else
                {
                    f = EditorGUI.FloatField(position, label, 0f);
                }
                property.stringValue = f.ToString();
                break;
            case SchemaParameterType.Boolean:
                bool b = property.stringValue == "true";
                b = EditorGUI.Toggle(position, label, b);
                property.stringValue = b ? "true" : "false";
                break;
            case SchemaParameterType.None:
                break;
        }
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        FlexibleInputAttribute attr = (FlexibleInputAttribute)attribute;
        SerializedProperty enumProp = FindRelativeProperty(property, attr.EnumFieldName);

        if (enumProp == null || enumProp.propertyType != SerializedPropertyType.Enum)
            return EditorGUIUtility.singleLineHeight;

        SchemaParameterType type = (SchemaParameterType)enumProp.enumValueIndex;
        return (type == SchemaParameterType.None) ? 0f : EditorGUIUtility.singleLineHeight;
    }
}
#endif