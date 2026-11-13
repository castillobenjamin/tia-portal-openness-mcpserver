using System;
using System.Collections.Generic;
using System.Drawing;
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
        /// floating-point numbers (invariant culture), enums (member name, case-insensitive) and colors (see
        /// <see cref="TryParseColor"/>).
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

            if (type == typeof(Color))
            {
                if (TryParseColor(input, out var color, out errorMessage))
                {
                    value = color;
                    return true;
                }

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
        /// Parses a color: "#RRGGBB", "#AARRGGBB", "R,G,B", "A,R,G,B" (0-255 each, as in Color.FromArgb) or a known
        /// color name such as "Red" or "Transparent".
        /// </summary>
        public static bool TryParseColor(string? text, out Color color, out string errorMessage)
        {
            color = Color.Empty;
            errorMessage = string.Empty;
            var input = (text ?? string.Empty).Trim();

            if (input.StartsWith("#", StringComparison.Ordinal))
            {
                var hex = input.Substring(1);
                if ((hex.Length == 6 || hex.Length == 8) &&
                    uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var argb))
                {
                    color = Color.FromArgb(hex.Length == 6 ? (int)(argb | 0xFF000000) : unchecked((int)argb));
                    return true;
                }
            }
            else if (input.IndexOf(',') >= 0)
            {
                var parts = input.Split(',');
                var channels = new int[parts.Length];
                var valid = parts.Length == 3 || parts.Length == 4;
                for (var i = 0; valid && i < parts.Length; i++)
                {
                    valid = int.TryParse(parts[i].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out channels[i]) &&
                            channels[i] <= 255;
                }

                if (valid)
                {
                    color = parts.Length == 3
                        ? Color.FromArgb(255, channels[0], channels[1], channels[2])
                        : Color.FromArgb(channels[0], channels[1], channels[2], channels[3]);
                    return true;
                }
            }
            else if (input.Length > 0)
            {
                var named = Color.FromName(input);
                if (named.IsKnownColor)
                {
                    color = Color.FromArgb(named.ToArgb());
                    return true;
                }
            }

            errorMessage = $"'{text}' is not a valid color; use #RRGGBB, #AARRGGBB, R,G,B, A,R,G,B or a color name such as Red";
            return false;
        }

        /// <summary>
        /// Formats a color as "#RRGGBB", or "#AARRGGBB" when it is not fully opaque. Accepted back by <see cref="TryParseColor"/>.
        /// </summary>
        public static string FormatColor(Color color)
        {
            return color.A == 255
                ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
                : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        /// <summary>
        /// A simple attribute value as returned to a client: enums by member name, colors by <see cref="FormatColor"/>,
        /// everything else unchanged.
        /// </summary>
        public static object? ToClientValue(object? value)
        {
            if (value is Enum)
            {
                return value.ToString();
            }

            if (value is Color color)
            {
                return FormatColor(color);
            }

            return value;
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
            return t == typeof(string) || t == typeof(bool) || t == typeof(Color) || t.IsEnum || IsNumeric(t);
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
