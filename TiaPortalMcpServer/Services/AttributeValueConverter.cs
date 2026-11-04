using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TiaPortalMcpServer.Services
{
    /// <summary>
    /// Converts attribute values given as text by a client into the .NET type of the TIA Portal attribute.
    /// </summary>
    public static class AttributeValueConverter
    {
        /// <summary>
        /// Converts <paramref name="text"/> to <paramref name="targetType"/>. Supports strings, booleans, integer and
        /// floating-point numbers (invariant culture) and enums (member name, case-insensitive).
        /// </summary>
        public static bool TryConvert(string? text, Type targetType, out object? value, out string errorMessage)
        {
            value = null;
            errorMessage = string.Empty;

            var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
            var input = text ?? string.Empty;

            if (type == typeof(string))
            {
                value = input;
                return true;
            }

            if (type.IsEnum)
            {
                var names = Enum.GetNames(type);
                var match = names.FirstOrDefault(n => string.Equals(n, input.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    errorMessage = $"'{input}' is not a valid {type.Name}. Allowed values: {string.Join(", ", names)}";
                    return false;
                }

                value = Enum.Parse(type, match);
                return true;
            }

            if (type == typeof(bool))
            {
                if (bool.TryParse(input.Trim(), out var boolValue))
                {
                    value = boolValue;
                    return true;
                }

                errorMessage = $"'{input}' is not a valid boolean; use 'true' or 'false'";
                return false;
            }

            if (IsNumeric(type))
            {
                try
                {
                    value = Convert.ChangeType(input.Trim(), type, CultureInfo.InvariantCulture);
                    return true;
                }
                catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is InvalidCastException)
                {
                    errorMessage = $"'{input}' is not a valid {type.Name}: {ex.Message}";
                    return false;
                }
            }

            errorMessage = $"Attributes of type {type.Name} cannot be set from text";
            return false;
        }

        /// <summary>
        /// Parses a JSON object of attribute names to values, such as {"Left": 20, "Text": "Start"}. Values may be strings,
        /// numbers or booleans; they are returned as invariant-culture text. An empty string yields an empty map.
        /// </summary>
        public static bool TryParseAttributeMap(string? json, out Dictionary<string, string> attributes, out string errorMessage)
        {
            attributes = new Dictionary<string, string>(StringComparer.Ordinal);
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(json))
            {
                return true;
            }

            JObject parsed;
            try
            {
                parsed = JObject.Parse(json!);
            }
            catch (JsonReaderException ex)
            {
                errorMessage = $"attributes must be a JSON object such as {{\"Left\": 20, \"Text\": \"Start\"}}: {ex.Message}";
                return false;
            }

            foreach (var property in parsed.Properties())
            {
                if (!(property.Value is JValue value) || value.Type == JTokenType.Null)
                {
                    errorMessage = $"Attribute '{property.Name}' must have a string, number or boolean value";
                    return false;
                }

                attributes[property.Name] = value.Type == JTokenType.Boolean
                    ? ((bool)value ? "true" : "false")
                    : Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty;
            }

            return true;
        }

        /// <summary>
        /// Splits a multilingual text value: "de-DE:Start" sets one language, any other text sets all languages (culture null).
        /// </summary>
        public static (string? Culture, string Text) ParseCultureText(string? value)
        {
            var text = value ?? string.Empty;
            var match = CultureTextPattern.Match(text);
            return match.Success ? (match.Groups[1].Value, match.Groups[2].Value) : ((string?)null, text);
        }

        private static readonly Regex CultureTextPattern = new Regex(@"^([a-zA-Z]{2,3}-[a-zA-Z]{2,4}):(.*)$", RegexOptions.Singleline);

        /// <summary>
        /// Whether a value of this type can be returned as a plain JSON value.
        /// </summary>
        public static bool IsSimple(Type type)
        {
            var t = Nullable.GetUnderlyingType(type) ?? type;
            return t == typeof(string) || t == typeof(bool) || t.IsEnum || IsNumeric(t);
        }

        private static bool IsNumeric(Type type)
        {
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                case TypeCode.Single:
                case TypeCode.Double:
                case TypeCode.Decimal:
                    return true;
                default:
                    return false;
            }
        }
    }
}
