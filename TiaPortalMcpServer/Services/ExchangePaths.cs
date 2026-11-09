using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TiaPortalMcpServer.Services
{
    /// <summary>
    /// A file found for an import, with the group path it maps to.
    /// </summary>
    public class ExchangeFile
    {
        public ExchangeFile(FileInfo file, string groupPath)
        {
            File = file;
            GroupPath = groupPath;
        }

        public FileInfo File { get; }

        /// <summary>
        /// Target group path such as "Motion/Axis", relative to the root block or type group.
        /// </summary>
        public string GroupPath { get; }

        public string Name => Path.GetFileNameWithoutExtension(File.Name);
    }

    /// <summary>
    /// Maps TIA object names and block/type group paths (such as "Motion/Axis") to file system paths and back.
    /// </summary>
    /// <remarks>
    /// With preservePath, exports mirror the TIA project tree: blocks go below a "Program blocks" folder, PLC data types
    /// below a "PLC data types" folder and PLC tag tables below a "PLC tags" folder, each followed by the group path.
    /// Imports read the same layout back.
    /// </remarks>
    public static class ExchangePaths
    {
        /// <summary>
        /// Folder that stands for the root block group in an exported tree, as named in the TIA project tree.
        /// </summary>
        public const string BlocksRootFolder = "Program blocks";

        /// <summary>
        /// Folder that stands for the root PLC data type group in an exported tree, as named in the TIA project tree.
        /// </summary>
        public const string TypesRootFolder = "PLC data types";

        /// <summary>
        /// Folder that stands for the root PLC tag table group in an exported tree, as named in the TIA project tree.
        /// </summary>
        public const string TagTablesRootFolder = "PLC tags";

        private static readonly char[] GroupSeparators = { '/', '\\' };

        /// <summary>
        /// The export directory for an object: <paramref name="exportPath"/> itself, or with <paramref name="preservePath"/>
        /// exportPath/rootFolder/group/path.
        /// </summary>
        public static string ToExportDirectory(string exportPath, string rootFolder, string? groupPath, bool preservePath)
        {
            return preservePath ? ToDirectory(Path.Combine(exportPath, rootFolder), groupPath) : exportPath;
        }

        /// <summary>
        /// The directory that stands for the root group in an exported tree: <paramref name="importDirectory"/>/rootFolder
        /// when it exists, so the export path can be passed back unchanged, otherwise <paramref name="importDirectory"/> itself.
        /// </summary>
        public static DirectoryInfo ResolveTreeRoot(DirectoryInfo importDirectory, string rootFolder)
        {
            var nested = new DirectoryInfo(Path.Combine(importDirectory.FullName, rootFolder));
            return nested.Exists ? nested : importDirectory;
        }

        /// <summary>
        /// Finds the files to import with the given extension (such as ".xml"), filtered by file name, ordered by path.
        /// Without <paramref name="preservePath"/>, only the files directly in <paramref name="directory"/> are found and all go
        /// into <paramref name="groupName"/>. With it, the tree below the resolved root folder is searched and each subdirectory
        /// maps to the group of the same path below <paramref name="groupName"/>.
        /// </summary>
        public static List<ExchangeFile> FindFiles(DirectoryInfo directory, string extension, string? groupName, bool preservePath, string? regexName, string rootFolder)
        {
            var root = preservePath ? ResolveTreeRoot(directory, rootFolder) : directory;
            var option = preservePath ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var result = new List<ExchangeFile>();

            // Exact extension check: the "*.xml" search pattern also matches longer extensions such as ".xmlx"
            foreach (var file in root.GetFiles("*" + extension, option).OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase))
            {
                if (!string.Equals(file.Extension, extension, StringComparison.OrdinalIgnoreCase) ||
                    !NameFilter.IsMatch(Path.GetFileNameWithoutExtension(file.Name), regexName))
                {
                    continue;
                }

                var groupPath = preservePath
                    ? CombineGroupPath(groupName, ToGroupPath(root.FullName, file.DirectoryName!))
                    : CombineGroupPath(groupName);
                result.Add(new ExchangeFile(file, groupPath));
            }

            return result;
        }

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
