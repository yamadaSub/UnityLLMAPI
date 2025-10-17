using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Text;

[Serializable]
public enum SchemaParameterType
{
    String,
    Number,
    Boolean,
    DateTime,
    None
}
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class AllowedValuesAttribute : Attribute
{
    public string[] Values { get; }

    public AllowedValuesAttribute(params string[] values)
    {
        Values = values;
    }
}


public static class JsonSchemaGenerator
{
    /// <summary>
    /// プリミティブ→Schema 変換
    /// </summary>
    public static Dictionary<string, object> CreatePrimitiveSchema(
        SchemaParameterType pt, string description = null, string[] enumList = null)
    {
        var d = new Dictionary<string, object>();
        switch (pt)
        {
            case SchemaParameterType.String:
            case SchemaParameterType.None: //Primitive変換がかかっている時点で不可能なのでStringで処理
                d["type"] = "string"; break;
            case SchemaParameterType.Number: d["type"] = "number"; break;
            case SchemaParameterType.Boolean: d["type"] = "boolean"; break;
            case SchemaParameterType.DateTime:
                d["type"] = "string";
                d["format"] = "date-time";
                break;

        }
        if(enumList != null&& enumList.Length != 0)
        {
            d["enum"] = enumList;
        }
        if (!string.IsNullOrEmpty(description)) d["description"] = description;
        return d;
    }

    /* CLR 型 → enum 変換（リフレクション側だけが使う） */
    private static SchemaParameterType ToSchemaParameterType(Type t)
    {
        if (t == typeof(string)) return SchemaParameterType.String;
        if (t == typeof(bool)) return SchemaParameterType.Boolean;
        if (t == typeof(DateTime)) return SchemaParameterType.DateTime;
        if (t.IsPrimitive || t == typeof(decimal) ||
            t == typeof(double) || t == typeof(float))
            return SchemaParameterType.Number;
        // それ以外はNoneとして扱う(ArrayやObject,Enum)
        return SchemaParameterType.None;
    }

    /* ------------------------------------------------------------------
     * ② properties / required をまとめて「完成形」にする
     * ------------------------------------------------------------------ */
    public static Dictionary<string, object> BuildObjectSchema(
        IEnumerable<(string key, Dictionary<string, object> schema)> members)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var (key, schema) in members)
        {
            if (schema == null) continue;   // optional
            properties[key] = schema;
            required.Add(key);
        }

        return new Dictionary<string, object>{
                { "type", "object" },
                { "properties", properties },
                { "required", required }
        };
    }

    /* ------------------------------------------------------------------
     * ③ 既存 API: 型 T → JSON-Schema
     * ------------------------------------------------------------------ */
    public static Dictionary<string, object> GenerateSchema<T>(string schemaName = null)
    {
        string name = string.IsNullOrEmpty(schemaName)
                    ? "schema" : schemaName;
        return new Dictionary<string, object>{
            { "name", name },
            { "schema", GenerateSchema(typeof(T))}
        };
    }

    public static Dictionary<string, object> GenerateSchema(Type type)
    {
        var members = new List<(string, Dictionary<string, object>)>();

        foreach (var m in type.GetMembers(BindingFlags.Public | BindingFlags.Instance))
        {
            Type mt = m switch
            {
                PropertyInfo p => p.PropertyType,
                FieldInfo f => f.FieldType,
                _ => null
            };
            if (mt == null) continue;

            var desc = m.GetCustomAttribute<DescriptionAttribute>()?.Description;
            var allowedValue = m.GetCustomAttribute<AllowedValuesAttribute>()?.Values;
            if (allowedValue == null && mt.IsEnum)
            {
                allowedValue = Enum.GetNames(mt);
            }
            if (mt.IsArrayOrList(out Type elementType))
            {
                // 配列の場合
                var elementSchemaType = ToSchemaParameterType(elementType);
                Dictionary<string, object> itemSchema;
                if (elementSchemaType == SchemaParameterType.None)
                {
                    itemSchema = GenerateSchema(elementType);
                }
                else
                {
                    itemSchema = CreatePrimitiveSchema(elementSchemaType);
                }
                var arraySchema = new Dictionary<string, object>
                    {
                        { "type", "array" },
                        { "items", itemSchema }
                    };
                if (!string.IsNullOrEmpty(desc)) arraySchema["description"] = desc;
                members.Add((m.Name, arraySchema));
            }
            else
            {
                // 通常のプリミティブ
                var elementSchemaType = ToSchemaParameterType(mt);
                members.Add((m.Name, CreatePrimitiveSchema(elementSchemaType, desc, allowedValue)));
            }
        }
        return BuildObjectSchema(members);
    }
}



public static class TypeExtend
{
    public static bool IsArrayOrList(this Type t, out Type elementType)
    {
        if (t.IsArray)
        {
            elementType = t.GetElementType();
            return true;
        }
        if (t.IsGenericType)
        {
            var genericType = t.GetGenericTypeDefinition();
            if (genericType == typeof(List<>) || genericType == typeof(IList<>) || genericType == typeof(IEnumerable<>))
            {
                elementType = t.GetGenericArguments()[0];
                return true;
            }
        }
        elementType = null;
        return false;
    }

    public static bool IsDictionary(this Type t, out Type keyType, out Type valueType)
    {
        if (t.IsGenericType && typeof(IDictionary<,>).IsAssignableFrom(t.GetGenericTypeDefinition()))
        {
            keyType = t.GetGenericArguments()[0];
            valueType = t.GetGenericArguments()[1];
            return true;
        }

        foreach (var iface in t.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IDictionary<,>))
            {
                keyType = iface.GetGenericArguments()[0];
                valueType = iface.GetGenericArguments()[1];
                return true;
            }
        }

        keyType = null;
        valueType = null;
        return false;
    }

    public static bool IsSimple(this Type type)
    {
        return type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type == typeof(DateTime)
            || type == typeof(decimal);
    }

    public static string ToMarkdown(this Type t)
    {
        var sb = new StringBuilder();
        var visited = new HashSet<Type>();
        AppendTypeMarkdown(sb, t, visited, 1);
        return sb.ToString();
    }

    private static void AppendTypeMarkdown(StringBuilder sb, Type t, HashSet<Type> visited, int level)
    {
        if (visited.Contains(t))
        {
            sb.AppendLine($"{new string(' ', level * 2)}- **{t.Name}** (再帰参照のため省略)");
            return;
        }

        visited.Add(t);

        // Enum自体が対象型の場合
        if (t.IsEnum)
        {
            sb.AppendLine($"{new string(' ', level * 2)}- **Enum: {t.Name}**");
            var enumFields = t.GetFields(BindingFlags.Public | BindingFlags.Static);
            foreach (var field in enumFields)
            {
                var desc = field.GetCustomAttribute<DescriptionAttribute>()?.Description ?? field.Name;
                sb.AppendLine($"{new string(' ', (level + 1) * 2)}- `{field.Name}`: {desc}");
            }
            return;
        }

        sb.AppendLine($"{new string(' ', level * 2)}- **Type: {t.Name}**");
        var fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance);

        foreach (var field in fields)
        {
            var fieldDesc = field.GetCustomAttribute<DescriptionAttribute>()?.Description ?? field.Name;
            var fieldType = field.FieldType;

            // Dictionary<K,V>
            if (fieldType.IsDictionary(out Type keyType, out Type valueType))
            {
                sb.AppendLine($"{new string(' ', (level + 1) * 2)}- **{field.Name}** (`Dictionary<{keyType.Name}, {valueType.Name}>`): {fieldDesc}");
                if (!IsSimple(valueType))
                    AppendTypeMarkdown(sb, valueType, visited, level + 2);
                continue;
            }

            // Array/List
            if (fieldType.IsArrayOrList(out var elementType))
            {
                sb.AppendLine($"{new string(' ', (level + 1) * 2)}- **{field.Name}** (`List<{elementType.Name}>`): {fieldDesc}");
                if (!IsSimple(elementType))
                    AppendTypeMarkdown(sb, elementType, visited, level + 2);
                continue;
            }

            // Enum フィールド
            if (fieldType.IsEnum)
            {
                sb.AppendLine($"{new string(' ', (level + 1) * 2)}- **{field.Name}** (`Enum {fieldType.Name}`): {fieldDesc}");
                var enumFields = fieldType.GetFields(BindingFlags.Public | BindingFlags.Static);
                foreach (var enumField in enumFields)
                {
                    var enumDesc = enumField.GetCustomAttribute<DescriptionAttribute>()?.Description ?? enumField.Name;
                    sb.AppendLine($"{new string(' ', (level + 2) * 2)}- `{enumField.Name}`: {enumDesc}");
                }
                continue;
            }

            // 単純型またはカスタム型
            sb.AppendLine($"{new string(' ', (level + 1) * 2)}- **{field.Name}** (`{fieldType.Name}`): {fieldDesc}");

            if (!IsSimple(fieldType))
            {
                AppendTypeMarkdown(sb, fieldType, visited, level + 2);
            }
        }
    }

}
