using System.Collections.Generic;
using System.IO;
using System.Linq;
using TiaPortalMcpServer.Models;

namespace TiaPortalMcpServer.Services
{
    /// <summary>
    /// One SimaticML file to import, with the group it goes into.
    /// </summary>
    public class XmlImportPlanItem
    {
        public XmlImportPlanItem(FileInfo file, string groupPath)
        {
            File = file;
            GroupPath = groupPath;
        }

        public FileInfo File { get; }

        /// <summary>
        /// Target group path such as "Motion/Axis", relative to the root block or type group.
        /// </summary>
        public string GroupPath { get; }

        /// <summary>
        /// What the file contains, when it could be read.
        /// </summary>
        public SimaticMlFileInfo? Content { get; set; }

        /// <summary>
        /// Set when the file is not imported: invalid (Failure) or holding another kind of object (Skipped).
        /// </summary>
        public DocumentOperationResult? Result { get; set; }

        public string Name => Path.GetFileNameWithoutExtension(File.Name);
    }

    /// <summary>
    /// Decides which SimaticML files an import covers and which group each one goes into, without TIA Portal.
    /// </summary>
    public static class XmlImportPlanner
    {
        /// <summary>
        /// Plans the import of the .xml files in <paramref name="directory"/>. With <paramref name="preservePath"/>,
        /// subdirectories are included and each one maps to a user group below <paramref name="groupName"/>.
        /// </summary>
        public static List<XmlImportPlanItem> PlanDirectory(DirectoryInfo directory, string? groupName, bool preservePath, string? regexName, string expectedKind)
        {
            var option = preservePath ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            return directory.GetFiles("*.xml", option)
                .Where(f => NameFilter.IsMatch(Path.GetFileNameWithoutExtension(f.Name), regexName))
                .OrderBy(f => f.FullName, System.StringComparer.OrdinalIgnoreCase)
                .Select(f => PlanFile(
                    f,
                    preservePath
                        ? ExchangePaths.CombineGroupPath(groupName, ExchangePaths.ToGroupPath(directory.FullName, f.DirectoryName!))
                        : ExchangePaths.CombineGroupPath(groupName),
                    expectedKind))
                .ToList();
        }

        /// <summary>
        /// Plans the import of a single file into the given group path.
        /// </summary>
        public static XmlImportPlanItem PlanFile(FileInfo file, string groupPath, string expectedKind)
        {
            var item = new XmlImportPlanItem(file, groupPath);

            if (!SimaticMlInspector.TryInspect(file.FullName, out var content, out var error))
            {
                item.Result = CreateResult(item, DocumentOperationResult.StateFailure, error);
                return item;
            }

            item.Content = content;
            if (content.Kind != expectedKind)
            {
                item.Result = CreateResult(item, DocumentOperationResult.StateSkipped,
                    $"File holds {DescribeKind(content.Kind)} ({content.ElementName}), not {DescribeKind(expectedKind)}");
                return item;
            }

            if (string.IsNullOrEmpty(content.Name))
            {
                item.Result = CreateResult(item, DocumentOperationResult.StateFailure, $"No object name found in {content.ElementName}");
            }

            return item;
        }

        public static DocumentOperationResult CreateResult(XmlImportPlanItem item, string state, string message)
        {
            return new DocumentOperationResult
            {
                Name = item.Content?.Name ?? item.Name,
                State = state,
                GroupPath = item.GroupPath,
                Files = new List<string> { item.File.FullName },
                Messages = new List<string> { message }
            };
        }

        private static string DescribeKind(string kind)
        {
            return kind switch
            {
                SimaticMlInspector.KindBlock => "a block",
                SimaticMlInspector.KindType => "a PLC data type",
                _ => "another kind of object"
            };
        }
    }
}
