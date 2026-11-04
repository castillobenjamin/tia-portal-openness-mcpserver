using System;
using System.Text.RegularExpressions;

namespace TiaPortalMcpServer.Services
{
    /// <summary>
    /// Name filter shared by tools that accept a "name or regular expression" parameter.
    /// A filter without regex metacharacters is a case-insensitive substring match;
    /// otherwise it is a case-insensitive regular expression.
    /// </summary>
    public static class NameFilter
    {
        // closing parantheses for regex characters ommitted, because they are not relevant for regex detection
        private static readonly char[] RegexChars = ['.', '^', '$', '*', '+', '?', '(', '[', '{', '\\', '|'];

        /// <summary>
        /// Returns true when the name passes the filter. An empty filter matches everything.
        /// </summary>
        /// <exception cref="ArgumentException">The filter is an invalid regular expression.</exception>
        public static bool IsMatch(string name, string? filter)
        {
            if (string.IsNullOrEmpty(filter))
            {
                return true;
            }

            return (filter!.IndexOfAny(RegexChars) >= 0 && Regex.IsMatch(name, filter, RegexOptions.IgnoreCase)) ||
                name.Contains(filter, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Validates that the filter can be evaluated, so callers can reject it before doing any work.
        /// </summary>
        public static bool TryValidate(string? filter, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (string.IsNullOrEmpty(filter) || filter!.IndexOfAny(RegexChars) < 0)
            {
                return true;
            }

            try
            {
                _ = new Regex(filter);
                return true;
            }
            catch (ArgumentException ex)
            {
                errorMessage = $"Invalid regular expression '{filter}': {ex.Message}";
                return false;
            }
        }
    }
}
