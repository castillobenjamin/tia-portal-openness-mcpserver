using System;
using System.Collections.Generic;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using TiaPortalMcpServer.Models;

namespace TiaPortalMcpServer.Services
{
    /// <summary>
    /// Builds the project tree of a PLC software or Software Unit: program blocks, PLC data types, PLC tags and external sources.
    /// </summary>
    public static class SoftwareTreeBuilder
    {
        /// <summary>
        /// The tree of <paramref name="scope"/>. Sections left out are null.
        /// </summary>
        public static SoftwareTree Build(PlcScope scope, bool includeBlocks, bool includeTypes, bool includeTagTables, bool includeExternalSources, bool includeItems)
        {
            return new SoftwareTree
            {
                Path = PlcSoftwarePathInfo.FormatPath(scope.DeviceName, scope.Unit?.Name),
                DeviceName = scope.DeviceName,
                UnitName = scope.Unit?.Name,
                ProgramBlocks = includeBlocks ? BuildBlockTree(scope.BlockGroup, string.Empty, includeItems) : null,
                PlcDataTypes = includeTypes ? BuildTypeTree(scope.TypeGroup, string.Empty, includeItems) : null,
                PlcTags = includeTagTables ? BuildTagTableTree(scope.TagTableGroup, string.Empty, includeItems) : null,
                ExternalSources = includeExternalSources ? BuildExternalSourceTree(scope.ExternalSourceGroup, string.Empty, includeItems) : null
            };
        }

        public static SoftwareTreeNode BuildBlockTree(PlcBlockGroup group, string path, bool includeItems)
        {
            var node = CreateNode(group.Name, path, includeItems);
            var count = 0;
            foreach (PlcBlock block in group.Blocks)
            {
                count++;
                node.Items?.Add(CreateBlockItem(block));
            }

            foreach (PlcBlockUserGroup child in group.Groups)
            {
                node.Groups.Add(BuildBlockTree(child, ExchangePaths.CombineGroupPath(path, child.Name), includeItems));
            }

            node.ComputeTotalCount(count);
            return node;
        }

        public static SoftwareTreeNode BuildTypeTree(PlcTypeGroup group, string path, bool includeItems)
        {
            var node = CreateNode(group.Name, path, includeItems);
            var count = 0;
            foreach (PlcType type in group.Types)
            {
                count++;
                node.Items?.Add(new SoftwareTreeItem { Name = type.Name, Kind = type.GetType().Name });
            }

            foreach (PlcTypeUserGroup child in group.Groups)
            {
                node.Groups.Add(BuildTypeTree(child, ExchangePaths.CombineGroupPath(path, child.Name), includeItems));
            }

            node.ComputeTotalCount(count);
            return node;
        }

        public static SoftwareTreeNode BuildTagTableTree(PlcTagTableGroup group, string path, bool includeItems)
        {
            var node = CreateNode(group.Name, path, includeItems);
            var count = 0;
            foreach (PlcTagTable table in group.TagTables)
            {
                count++;
                node.Items?.Add(new SoftwareTreeItem
                {
                    Name = table.Name,
                    Kind = table.GetType().Name,
                    IsDefault = table.IsDefault,
                    TagCount = table.Tags.Count
                });
            }

            foreach (PlcTagTableUserGroup child in group.Groups)
            {
                node.Groups.Add(BuildTagTableTree(child, ExchangePaths.CombineGroupPath(path, child.Name), includeItems));
            }

            node.ComputeTotalCount(count);
            return node;
        }

        public static SoftwareTreeNode BuildExternalSourceTree(PlcExternalSourceGroup group, string path, bool includeItems)
        {
            var node = CreateNode(group.Name, path, includeItems);
            var count = 0;
            foreach (PlcExternalSource source in group.ExternalSources)
            {
                count++;
                node.Items?.Add(new SoftwareTreeItem { Name = source.Name, Kind = source.GetType().Name });
            }

            foreach (PlcExternalSourceUserGroup child in group.Groups)
            {
                node.Groups.Add(BuildExternalSourceTree(child, ExchangePaths.CombineGroupPath(path, child.Name), includeItems));
            }

            node.ComputeTotalCount(count);
            return node;
        }

        private static SoftwareTreeNode CreateNode(string name, string path, bool includeItems)
        {
            return new SoftwareTreeNode
            {
                Name = name,
                Path = path,
                Items = includeItems ? new List<SoftwareTreeItem>() : null
            };
        }

        private static SoftwareTreeItem CreateBlockItem(PlcBlock block)
        {
            var item = new SoftwareTreeItem { Name = block.Name, Kind = block.GetType().Name };

            // Number and language are not readable on every block (e.g. some know-how protected or system blocks)
            try
            {
                item.Number = block.Number;
                item.ProgrammingLanguage = block.ProgrammingLanguage.ToString();
            }
            catch (Exception)
            {
            }

            return item;
        }
    }
}
