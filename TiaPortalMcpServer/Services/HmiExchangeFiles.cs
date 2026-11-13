using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TiaPortalMcpServer.Services
{
    /// <summary>
    /// File names of the WinCC Unified list exchange (text lists, graphic lists, system text lists). TIA Portal's
    /// Export(directory, name) writes "&lt;name&gt;.hmi.yml" (YAML) and, for system text lists, a second
    /// "&lt;name&gt;.TextLibrary.hmi.yml"; Import(directory, name) expects the full name of the main file.
    /// </summary>
    public static class HmiExchangeFiles
    {
        public const string YamlExtension = ".hmi.yml";

        /// <summary>
        /// The name to pass to Export: <paramref name="fileName"/> without a trailing ".hmi.yml", so the name of the
        /// written file can be used again.
        /// </summary>
        public static string ExportBaseName(string fileName)
        {
            var name = fileName.Trim();
            return name.EndsWith(YamlExtension, StringComparison.OrdinalIgnoreCase) && name.Length > YamlExtension.Length
                ? name.Substring(0, name.Length - YamlExtension.Length)
                : name;
        }

        /// <summary>
        /// The file to import: "&lt;fileName&gt;" when it exists, otherwise "&lt;fileName&gt;.hmi.yml", so both the base name
        /// given to the export and the name of the written file work. Null when neither exists.
        /// </summary>
        public static FileInfo? FindImportFile(string directory, string fileName)
        {
            var name = fileName.Trim();
            var exact = new FileInfo(Path.Combine(directory, name));
            if (exact.Exists)
            {
                return exact;
            }

            var yaml = new FileInfo(Path.Combine(directory, name + YamlExtension));
            return yaml.Exists ? yaml : null;
        }

        /// <summary>
        /// The path of a file an HMI export wrote. TIA Portal V21 returns some paths without the ".hmi.yml" the file
        /// got on disk (HMI tag tables); such a path is completed when only the completed one exists.
        /// </summary>
        public static string WrittenFile(FileInfo file)
        {
            var path = file.FullName;
            if (File.Exists(path))
            {
                return path;
            }

            var yaml = path + YamlExtension;
            return File.Exists(yaml) ? yaml : path;
        }

        public const string ScriptExtension = ".js";

        /// <summary>
        /// The comment TIA Portal writes at the end of the global definition area of a script module file.
        /// </summary>
        public const string GlobalAreaEndMarker = "***End of Global definition area***";

        /// <summary>
        /// The script files a script module import reads: the ".js" files among <paramref name="files"/>, plus the
        /// "&lt;module&gt;.hmi.js" next to each "&lt;module&gt;.hmi.yml". Each file once, in the order found.
        /// </summary>
        public static List<FileInfo> ScriptFiles(IEnumerable<string> files)
        {
            var result = new List<FileInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in files)
            {
                var script = path;
                if (path.EndsWith(YamlExtension, StringComparison.OrdinalIgnoreCase))
                {
                    script = path.Substring(0, path.Length - ".yml".Length) + ScriptExtension;
                }

                if (script.EndsWith(ScriptExtension, StringComparison.OrdinalIgnoreCase) && File.Exists(script) && seen.Add(Path.GetFullPath(script)))
                {
                    result.Add(new FileInfo(script));
                }
            }
            return result;
        }

        /// <summary>
        /// Warnings for script module files whose first function is preceded by comments after the end of the global
        /// definition area. TIA Portal V21 imports such comments into the global definition area, so the next export
        /// writes the end-of-area comment after them instead of before them. Empty when no file has such comments.
        /// </summary>
        public static List<string> GlobalAreaWarnings(IEnumerable<FileInfo> scriptFiles)
        {
            var affected = new List<string>();
            foreach (var file in scriptFiles)
            {
                var lines = File.ReadAllLines(file.FullName);
                var count = CommentLinesAfterGlobalArea(lines);
                if (count > 0)
                {
                    affected.Add($"{file.Name}: {count} comment line(s) after the end of the global definition area, before the first function");
                }
            }

            if (affected.Count == 0)
            {
                return affected;
            }

            var warnings = new List<string>
            {
                $"{affected.Count} script file(s) have comments between the end of the global definition area and the first function. " +
                "TIA Portal V21 imports these comments into the global definition area, so the next export writes the " +
                "'End of Global definition area' comment after them and the file changes once. The script code itself is not changed."
            };
            warnings.AddRange(affected);
            return warnings;
        }

        /// <summary>
        /// The number of comment lines between the end of the comment holding <see cref="GlobalAreaEndMarker"/> and the
        /// first line of code; 0 when there is no marker.
        /// </summary>
        public static int CommentLinesAfterGlobalArea(IReadOnlyList<string> lines)
        {
            var index = 0;
            while (index < lines.Count && lines[index].IndexOf(GlobalAreaEndMarker, StringComparison.Ordinal) < 0)
            {
                index++;
            }

            while (index < lines.Count && lines[index].IndexOf("*/", StringComparison.Ordinal) < 0)
            {
                index++;
            }

            if (index >= lines.Count)
            {
                return 0;
            }

            var count = 0;
            var inBlock = false;
            for (index++; index < lines.Count; index++)
            {
                var line = lines[index].Trim();
                if (inBlock)
                {
                    count++;
                    inBlock = line.IndexOf("*/", StringComparison.Ordinal) < 0;
                }
                else if (line.Length == 0)
                {
                    continue;
                }
                else if (line.StartsWith("//", StringComparison.Ordinal))
                {
                    count++;
                }
                else if (line.StartsWith("/*", StringComparison.Ordinal))
                {
                    count++;
                    inBlock = line.IndexOf("*/", 2, StringComparison.Ordinal) < 0;
                }
                else
                {
                    break;
                }
            }
            return count;
        }

        public const string TextLibrarySuffix = ".TextLibrary";

        public const string DoubleEscape = "@@";

        /// <summary>
        /// At most this many texts holding "@@" are listed one by one; the rest are only counted.
        /// </summary>
        public const int MaxListedDoubleEscapes = 20;

        /// <summary>
        /// The files a system text list import reads: the main file and, when it exists, the
        /// "&lt;name&gt;.TextLibrary.hmi.yml" written next to it, which holds the texts.
        /// </summary>
        public static List<FileInfo> SystemTextListFiles(FileInfo mainFile)
        {
            var files = new List<FileInfo> { mainFile };
            var name = mainFile.Name;
            var baseName = name.EndsWith(YamlExtension, StringComparison.OrdinalIgnoreCase)
                ? name.Substring(0, name.Length - YamlExtension.Length)
                : Path.GetFileNameWithoutExtension(name);
            var library = new FileInfo(Path.Combine(mainFile.DirectoryName ?? string.Empty, baseName + TextLibrarySuffix + YamlExtension));
            if (library.Exists && !string.Equals(library.FullName, mainFile.FullName, StringComparison.OrdinalIgnoreCase))
            {
                files.Add(library);
            }
            return files;
        }

        /// <summary>
        /// Warnings for the lines of <paramref name="files"/> that hold "@@". TIA Portal V21 was seen to double "@@" when
        /// importing system text lists (single "@" placeholders such as "@3%s@" stay as they are), so these texts must be
        /// checked after the import. Empty when no line holds "@@".
        /// </summary>
        public static List<string> DoubleEscapeWarnings(IEnumerable<FileInfo> files)
        {
            var hits = new List<string>();
            var total = 0;
            foreach (var file in files)
            {
                var lineNumber = 0;
                foreach (var line in File.ReadLines(file.FullName))
                {
                    lineNumber++;
                    if (line.IndexOf(DoubleEscape, StringComparison.Ordinal) < 0)
                    {
                        continue;
                    }

                    total++;
                    if (hits.Count < MaxListedDoubleEscapes)
                    {
                        hits.Add($"{file.Name} line {lineNumber}: {line.Trim()}");
                    }
                }
            }

            var warnings = new List<string>();
            if (total == 0)
            {
                return warnings;
            }

            warnings.Add($"{total} line(s) hold '{DoubleEscape}'. TIA Portal V21 was seen to double '{DoubleEscape}' when importing system text lists " +
                $"(for example '@@5%Y-%m-%d %H:%M:%S@@' became '@@@@5%Y-%m-%d %H:%M:%S@@@@'). Do not edit the file to compensate; " +
                "after the import, export again and compare these texts before saving the project.");
            warnings.AddRange(hits);
            if (total > hits.Count)
            {
                warnings.Add($"... and {total - hits.Count} more line(s) holding '{DoubleEscape}'");
            }
            return warnings;
        }

        /// <summary>
        /// Directory of one HMI tag table below an export root: exportPath/&lt;group path&gt;/&lt;table name&gt;/.
        /// </summary>
        public static string TagTableDirectory(string root, string groupPath, string tableName)
        {
            return Path.Combine(ExchangePaths.ToDirectory(root, groupPath), ExchangePaths.ToFileName(tableName));
        }

        /// <summary>
        /// The tag table directories below <paramref name="root"/>, as written by the HMI tag export: every directory
        /// below the root that holds files is one tag table, named after the directory, in the tag table group of its
        /// parent's path. Files directly in the root are not part of any table and are ignored. Ordered by path.
        /// </summary>
        public static List<(DirectoryInfo Directory, string GroupPath, string TableName)> FindTagTableDirectories(DirectoryInfo root)
        {
            var result = new List<(DirectoryInfo Directory, string GroupPath, string TableName)>();
            foreach (var directory in root.GetDirectories("*", SearchOption.AllDirectories).OrderBy(d => d.FullName, StringComparer.OrdinalIgnoreCase))
            {
                if (!directory.EnumerateFiles().Any())
                {
                    continue;
                }

                var parentPath = ExchangePaths.ToGroupPath(root.FullName, directory.Parent!.FullName);
                result.Add((new DirectoryInfo(directory.FullName), parentPath, directory.Name));
            }
            return result;
        }
    }
}
