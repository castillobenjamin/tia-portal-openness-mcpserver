using System;
using System.Linq;

namespace TiaPortalMcpServer.Services
{
    /// <summary>
    /// Parses tool parameters that name one or more values of a [Flags] enum, such as "Override, SkipInactiveCultures".
    /// </summary>
    public static class EnumFlags
    {
        /// <summary>
        /// Parses a comma-separated list of enum member names (case-insensitive). An empty string yields the zero value.
        /// Numeric values and unknown names are rejected.
        /// </summary>
        public static bool TryParse<TEnum>(string? value, out TEnum result, out string errorMessage) where TEnum : struct
        {
            result = default;
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            var names = Enum.GetNames(typeof(TEnum));
            var parts = value!.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            var unknown = parts.Where(p => !names.Any(n => string.Equals(n, p, StringComparison.OrdinalIgnoreCase))).ToList();
            if (unknown.Count > 0)
            {
                errorMessage = $"Invalid value '{string.Join(", ", unknown)}'. Allowed values: {string.Join(", ", names)} (combine with commas)";
                return false;
            }

            result = (TEnum)Enum.Parse(typeof(TEnum), string.Join(", ", parts), ignoreCase: true);
            return true;
        }
    }
}
