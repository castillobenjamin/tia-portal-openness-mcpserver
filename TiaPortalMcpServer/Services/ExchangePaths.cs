using System;
using System.IO;
using System.Linq;

namespace TiaPortalMcpServer.Services
{
    /// <summary>
    /// Maps TIA object names and block/type group paths (such as "Motion/Axis") to file system paths and back.
    /// </summary>
    public static class ExchangePaths
    {
        private static readonly char[] GroupSeparators = { '/', '\\' };

        /// <summary>
        /// Turns a TIA object or group name into a safe file or directory name by replacing invalid characters with '_'.
        /// </summary>
        public static string ToFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var safe = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            return safe.Length == 0 || safe == "." || safe == ".." ? "_" : safe;
        }

        /// <summary>
        /// Joins group paths with '/', skipping empty parts.
        /// </summary>
        public static string CombineGroupPath(params string?[] parts)
        {
            return string.Join("/", parts
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .SelectMany(p => p!.Split(GroupSeparators, StringSplitOptions.RemoveEmptyEntries))
                .Select(p => p.Trim())
                .Where(p => p.Length > 0));
        }

        /// <summary>
        /// The directory below <paramref name="rootDirectory"/> that mirrors a group path, with each segment made file-system safe.
        /// </summary>
        public static string ToDirectory(string rootDirectory, string? groupPath)
        {
            var segments = CombineGroupPath(groupPath)
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(ToFileName)
                .ToArray();
            return segments.Length == 0 ? rootDirectory : Path.Combine(new[] { rootDirectory }.Concat(segments).ToArray());
        }

        /// <summary>
        /// The group path that a directory below <paramref name="rootDirectory"/> stands for, e.g. "Motion/Axis". Empty for the root itself.
        /// </summary>
        public static string ToGroupPath(string rootDirectory, string directory)
        {
            var root = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(root, full, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"'{directory}' is not below '{rootDirectory}'", nameof(directory));
            }

            return CombineGroupPath(full.Substring(root.Length + 1));
        }
    }
}
