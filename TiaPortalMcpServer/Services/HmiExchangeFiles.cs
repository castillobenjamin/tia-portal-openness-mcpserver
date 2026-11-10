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
