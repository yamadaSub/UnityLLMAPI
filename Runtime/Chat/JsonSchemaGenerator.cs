using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace UnityLLMAPI.Schema
{
    [Serializable]
    public enum SchemaParameterType
    {
        String,
        Number,
        Boolean,
        DateTime,
        None
    }

    public static class JsonSchemaGenerator
    {
        private static readonly Dictionary<Type, Dictionary<string, object>> SchemaCache = new Dictionary<Type, Dictionary<string, object>>();
        private static readonly object CacheLock = new object();

        private static SchemaParameterType ToSchemaParameterType(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            if (type == typeof(string) || type.IsEnum) return SchemaParameterType.String;
            if (type == typeof(bool)) return SchemaParameterType.Boolean;
            if (type == typeof(DateTime)) return SchemaParameterType.DateTime;
            if (type.IsPrimitive || type == typeof(decimal) || type == typeof(double) || type == typeof(float))
            {
                return SchemaParameterType.Number;
            }

            return SchemaParameterType.None;
        }

        public static Dictionary<string, object> BuildObjectSchema(
            IEnumerable<(string key, Dictionary<string, object> schema)> members)
        {
            var properties = new Dictionary<string, object>();
            var required = new List<string>();

            foreach (var (key, schema) in members)
            {
                if (schema == null) continue;
                properties[key] = schema;
                required.Add(key);
            }

            return new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", properties },
                { "required", required }
            };
        }

        public static Dictionary<string, object> GenerateSchema<T>(string schemaName = null, object schemaSource = null)
        {
            string name = string.IsNullOrEmpty(schemaName) ? "schema" : schemaName;
            return new Dictionary<string, object>
            {
                { "name", name },
                { "schema", GenerateSchema(typeof(T), schemaSource) }
            };
        }

        public static Dictionary<string, object> GenerateSchema(Type type, object schemaSource = null)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            if (schemaSource == null)
            {
                lock (CacheLock)
                {
                    if (SchemaCache.TryGetValue(type, out var cached))
                    {
                        return CloneSchema(cached);
                    }
                }
            }

            var schema = BuildSchemaForType(type, schemaSource, new HashSet<Type>());

            if (schemaSource == null)
            {
                lock (CacheLock)
                {
                    SchemaCache[type] = CloneSchema(schema);
                }
            }

            return CloneSchema(schema);
        }

        private static Dictionary<string, object> BuildSchemaForType(
            Type type,
            object schemaSource,
            HashSet<Type> visitedTypes)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            if (!visitedTypes.Add(type))
            {
                return new Dictionary<string, object>
                {
                    { "type", "object" }
                };
            }

            try
            {
                var members = new List<(string, Dictionary<string, object>)>();

                foreach (var member in GetSchemaMembers(type))
                {
                    if (member.GetCustomAttribute<SchemaIgnoreAttribute>() != null) continue;
                    if (!TryGetMemberType(member, out var memberType)) continue;

                    var memberSchema = CreateMemberSchema(member, memberType, schemaSource, visitedTypes);
                    if (memberSchema != null)
                    {
                        members.Add((member.Name, memberSchema));
                    }
                }

                return BuildObjectSchema(members);
            }
            finally
            {
                visitedTypes.Remove(type);
            }
        }

        private static IEnumerable<MemberInfo> GetSchemaMembers(Type type)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

            foreach (var field in type.GetFields(flags))
            {
                yield return field;
            }

            foreach (var property in type.GetProperties(flags))
            {
                if (!property.CanRead) continue;
                if (property.GetIndexParameters().Length > 0) continue;
                yield return property;
            }
        }

        private static Dictionary<string, object> CreateMemberSchema(
            MemberInfo member,
            Type memberType,
            object schemaSource,
            HashSet<Type> visitedTypes)
        {
            memberType = Nullable.GetUnderlyingType(memberType) ?? memberType;

            var description = member.GetCustomAttribute<DescriptionAttribute>()?.Description;
            var rangeAttribute = member.GetCustomAttribute<SchemaRangeAttribute>();
            var regexAttribute = member.GetCustomAttribute<SchemaRegularExpressionAttribute>();
            var multipleOf = member.GetCustomAttribute<SchemaMultipleOfAttribute>()?.Step;
            double? minimum = rangeAttribute != null ? rangeAttribute.Minimum : (double?)null;
            double? maximum = rangeAttribute != null ? rangeAttribute.Maximum : (double?)null;
            string pattern = regexAttribute?.Pattern;

            if (memberType.IsArrayOrList(out var elementType))
            {
                elementType = Nullable.GetUnderlyingType(elementType) ?? elementType;
                var itemType = ToSchemaParameterType(elementType);
                Dictionary<string, object> itemSchema;

                if (itemType == SchemaParameterType.None)
                {
                    itemSchema = BuildSchemaForType(elementType, null, visitedTypes);
                }
                else
                {
                    var enumValues = ResolveAllowedValues(member, elementType, schemaSource);
                    itemSchema = CreatePrimitiveSchema(itemType, enumValues: enumValues);
                }

                var arraySchema = new Dictionary<string, object>
                {
                    { "type", "array" },
                    { "items", itemSchema }
                };

                if (!string.IsNullOrEmpty(description))
                {
                    arraySchema["description"] = description;
                }

                return arraySchema;
            }

            var schemaType = ToSchemaParameterType(memberType);
            if (schemaType == SchemaParameterType.None)
            {
                var nestedSchemaSource = GetNestedSchemaSource(schemaSource, member);
                var objectSchema = BuildSchemaForType(memberType, nestedSchemaSource, visitedTypes);
                if (!string.IsNullOrEmpty(description))
                {
                    objectSchema["description"] = description;
                }
                return objectSchema;
            }

            var allowedValues = ResolveAllowedValues(member, memberType, schemaSource);
            return CreatePrimitiveSchema(schemaType, description, allowedValues, minimum, maximum, pattern, multipleOf);
        }

        private static bool TryGetMemberType(MemberInfo member, out Type memberType)
        {
            switch (member)
            {
                case PropertyInfo property:
                    memberType = property.PropertyType;
                    return true;
                case FieldInfo field:
                    memberType = field.FieldType;
                    return true;
                default:
                    memberType = null;
                    return false;
            }
        }

        private static object GetNestedSchemaSource(object schemaSource, MemberInfo member)
        {
            if (schemaSource == null) return null;
            return TryGetMemberValue(schemaSource, member.Name, out var value) ? value : null;
        }

        private static string[] ResolveAllowedValues(MemberInfo member, Type valueType, object schemaSource)
        {
            var dynamicValues = ResolveDynamicAllowedValues(member, schemaSource);
            if (dynamicValues != null && dynamicValues.Length > 0)
            {
                return dynamicValues;
            }

            var allowedValues = member.GetCustomAttribute<AllowedValuesAttribute>()?.Values;
            if (allowedValues != null && allowedValues.Length > 0)
            {
                return allowedValues;
            }

            valueType = Nullable.GetUnderlyingType(valueType) ?? valueType;
            if (valueType.IsEnum)
            {
                return Enum.GetNames(valueType);
            }

            return null;
        }

        private static string[] ResolveDynamicAllowedValues(MemberInfo member, object schemaSource)
        {
            if (schemaSource == null) return null;

            var attribute = member.GetCustomAttribute<DynamicAllowedValuesAttribute>();
            if (attribute == null || string.IsNullOrEmpty(attribute.SourceMemberName))
            {
                return null;
            }

            if (!TryGetMemberValue(schemaSource, attribute.SourceMemberName, out var sourceValue))
            {
                return null;
            }

            return ConvertAllowedValuesToStrings(sourceValue);
        }

        private static bool TryGetMemberValue(object instance, string memberName, out object value)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = instance.GetType();

            var property = type.GetProperty(memberName, flags);
            if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
            {
                value = property.GetValue(instance);
                return true;
            }

            var field = type.GetField(memberName, flags);
            if (field != null)
            {
                value = field.GetValue(instance);
                return true;
            }

            value = null;
            return false;
        }

        private static string[] ConvertAllowedValuesToStrings(object value)
        {
            if (value == null) return null;

            if (value is string text)
            {
                return string.IsNullOrEmpty(text) ? Array.Empty<string>() : new[] { text };
            }

            if (value is IEnumerable enumerable && !(value is IDictionary))
            {
                var values = new List<string>();
                foreach (var item in enumerable)
                {
                    if (TryConvertAllowedValueToString(item, out var converted))
                    {
                        values.Add(converted);
                    }
                }

                return values.Count == 0
                    ? null
                    : values.Distinct(StringComparer.Ordinal).ToArray();
            }

            return TryConvertAllowedValueToString(value, out var singleValue)
                ? new[] { singleValue }
                : null;
        }

        private static bool TryConvertAllowedValueToString(object value, out string result)
        {
            if (value == null)
            {
                result = null;
                return false;
            }

            switch (value)
            {
                case string text:
                    result = text;
                    return !string.IsNullOrEmpty(result);
                case Enum enumValue:
                    result = enumValue.ToString();
                    return true;
                default:
                    result = Convert.ToString(value, CultureInfo.InvariantCulture);
                    return !string.IsNullOrEmpty(result);
            }
        }

        public static Dictionary<string, object> CreatePrimitiveSchema(
            SchemaParameterType type,
            string description = null,
            string[] enumValues = null,
            double? min = null,
            double? max = null,
            string pattern = null,
            double? multipleOf = null)
        {
            var schema = new Dictionary<string, object>();

            switch (type)
            {
                case SchemaParameterType.String:
                    schema["type"] = "string";
                    if (enumValues != null && enumValues.Length > 0)
                    {
                        schema["enum"] = enumValues;
                    }
                    if (!string.IsNullOrEmpty(pattern))
                    {
                        schema["pattern"] = pattern;
                    }
                    break;
                case SchemaParameterType.Number:
                    schema["type"] = "number";
                    if (min.HasValue) schema["minimum"] = min.Value;
                    if (max.HasValue) schema["maximum"] = max.Value;
                    if (multipleOf.HasValue && multipleOf.Value > 0)
                    {
                        schema["multipleOf"] = multipleOf.Value;
                    }
                    break;
                case SchemaParameterType.Boolean:
                    schema["type"] = "boolean";
                    break;
                case SchemaParameterType.DateTime:
                    schema["type"] = "string";
                    schema["format"] = "date-time";
                    break;
                default:
                    schema["type"] = "string";
                    break;
            }

            if (!string.IsNullOrEmpty(description))
            {
                schema["description"] = description;
            }

            return schema;
        }

        private static Dictionary<string, object> CloneSchema(Dictionary<string, object> source)
        {
            var clone = new Dictionary<string, object>(source.Count);
            foreach (var kv in source)
            {
                clone[kv.Key] = CloneValue(kv.Value);
            }
            return clone;
        }

        private static object CloneValue(object value)
        {
            switch (value)
            {
                case null:
                    return null;
                case string[] stringArray:
                    return (string[])stringArray.Clone();
                case Array array:
                    {
                        var copied = new object[array.Length];
                        for (int i = 0; i < array.Length; i++)
                        {
                            copied[i] = CloneValue(array.GetValue(i));
                        }
                        return copied;
                    }
                case IList<string> stringList:
                    return new List<string>(stringList);
                case IList list:
                    {
                        var copied = new List<object>(list.Count);
                        foreach (var item in list)
                        {
                            copied.Add(CloneValue(item));
                        }
                        return copied;
                    }
                case Dictionary<string, object> dict:
                    return CloneSchema(dict);
                default:
                    return value;
            }
        }
    }

    public static class TypeExtend
    {
        public static bool IsArrayOrList(this Type type, out Type elementType)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            if (type.IsArray)
            {
                elementType = type.GetElementType();
                return true;
            }

            if (type.IsGenericType)
            {
                var genericType = type.GetGenericTypeDefinition();
                if (genericType == typeof(List<>)
                    || genericType == typeof(IList<>)
                    || genericType == typeof(IEnumerable<>))
                {
                    elementType = type.GetGenericArguments()[0];
                    return true;
                }
            }

            elementType = null;
            return false;
        }

        public static bool IsDictionary(this Type type, out Type keyType, out Type valueType)
        {
            if (type.IsGenericType && typeof(IDictionary<,>).IsAssignableFrom(type.GetGenericTypeDefinition()))
            {
                keyType = type.GetGenericArguments()[0];
                valueType = type.GetGenericArguments()[1];
                return true;
            }

            foreach (var iface in type.GetInterfaces())
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
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type.IsPrimitive
                || type.IsEnum
                || type == typeof(string)
                || type == typeof(DateTime)
                || type == typeof(decimal);
        }

        public static string ToMarkdown(this Type type)
        {
            var builder = new StringBuilder();
            var visited = new HashSet<Type>();
            AppendTypeMarkdown(builder, type, visited, 1);
            return builder.ToString();
        }

        private static void AppendTypeMarkdown(StringBuilder builder, Type type, HashSet<Type> visited, int level)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            if (visited.Contains(type))
            {
                builder.AppendLine($"{new string(' ', level * 2)}- **{type.Name}** (recursive reference)");
                return;
            }

            visited.Add(type);

            if (type.IsEnum)
            {
                builder.AppendLine($"{new string(' ', level * 2)}- **Enum: {type.Name}**");
                var enumFields = type.GetFields(BindingFlags.Public | BindingFlags.Static);
                foreach (var field in enumFields)
                {
                    var description = field.GetCustomAttribute<DescriptionAttribute>()?.Description ?? field.Name;
                    builder.AppendLine($"{new string(' ', (level + 1) * 2)}- `{field.Name}`: {description}");
                }
                return;
            }

            builder.AppendLine($"{new string(' ', level * 2)}- **Type: {type.Name}**");
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);

            foreach (var field in fields)
            {
                var description = field.GetCustomAttribute<DescriptionAttribute>()?.Description ?? field.Name;
                var fieldType = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;

                if (fieldType.IsDictionary(out Type keyType, out Type valueType))
                {
                    builder.AppendLine($"{new string(' ', (level + 1) * 2)}- **{field.Name}** (`Dictionary<{keyType.Name}, {valueType.Name}>`): {description}");
                    if (!IsSimple(valueType))
                    {
                        AppendTypeMarkdown(builder, valueType, visited, level + 2);
                    }
                    continue;
                }

                if (fieldType.IsArrayOrList(out var elementType))
                {
                    builder.AppendLine($"{new string(' ', (level + 1) * 2)}- **{field.Name}** (`List<{elementType.Name}>`): {description}");
                    if (!IsSimple(elementType))
                    {
                        AppendTypeMarkdown(builder, elementType, visited, level + 2);
                    }
                    continue;
                }

                if (fieldType.IsEnum)
                {
                    builder.AppendLine($"{new string(' ', (level + 1) * 2)}- **{field.Name}** (`Enum {fieldType.Name}`): {description}");
                    var enumFields = fieldType.GetFields(BindingFlags.Public | BindingFlags.Static);
                    foreach (var enumField in enumFields)
                    {
                        var enumDescription = enumField.GetCustomAttribute<DescriptionAttribute>()?.Description ?? enumField.Name;
                        builder.AppendLine($"{new string(' ', (level + 2) * 2)}- `{enumField.Name}`: {enumDescription}");
                    }
                    continue;
                }

                builder.AppendLine($"{new string(' ', (level + 1) * 2)}- **{field.Name}** (`{fieldType.Name}`): {description}");

                if (!IsSimple(fieldType))
                {
                    AppendTypeMarkdown(builder, fieldType, visited, level + 2);
                }
            }
        }
    }
}
