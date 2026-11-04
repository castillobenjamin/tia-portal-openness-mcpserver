using System.Collections.Generic;
using Newtonsoft.Json;

namespace TiaPortalMcpServer.Models
{
    /// <summary>
    /// One PLC software in the project: a device's main PLC software or one of its Software Units.
    /// </summary>
    public class PlcSoftwarePathInfo
    {
        public const string KindPlcSoftware = "PlcSoftware";
        public const string KindSoftwareUnit = "SoftwareUnit";
        public const string KindSafetyUnit = "SafetyUnit";

        /// <summary>
        /// "Device" for the main PLC software, "Device/Unit" for a Software Unit.
        /// </summary>
        [JsonProperty("path")]
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Value to pass as deviceName to the PLC software tools.
        /// </summary>
        [JsonProperty("deviceName")]
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>
        /// Value to pass as unitName to the PLC software tools; null for the main PLC software.
        /// </summary>
        [JsonProperty("unitName")]
        public string? UnitName { get; set; }

        /// <summary>
        /// "PlcSoftware", "SoftwareUnit" or "SafetyUnit".
        /// </summary>
        [JsonProperty("kind")]
        public string Kind { get; set; } = KindPlcSoftware;

        /// <summary>
        /// Name of the PLC software (usually the CPU name), which can differ from the device name.
        /// </summary>
        [JsonProperty("softwareName")]
        public string SoftwareName { get; set; } = string.Empty;

        /// <summary>
        /// Device folder in the project tree, such as "Line 1/Cell A"; empty for devices at the top level.
        /// </summary>
        [JsonProperty("deviceGroupPath")]
        public string DeviceGroupPath { get; set; } = string.Empty;

        /// <summary>
        /// For the main PLC software: whether the CPU supports Software Units. Null for units.
        /// </summary>
        [JsonProperty("unitsSupported", NullValueHandling = NullValueHandling.Ignore)]
        public bool? UnitsSupported { get; set; }

        /// <summary>
        /// Formats the path of a PLC software: "Device", or "Device/Unit" when <paramref name="unitName"/> is set.
        /// </summary>
        public static string FormatPath(string deviceName, string? unitName)
        {
            return string.IsNullOrEmpty(unitName) ? deviceName : deviceName + "/" + unitName;
        }
    }

    /// <summary>
    /// A block, type, tag table or external source in the software tree.
    /// </summary>
    public class SoftwareTreeItem
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Object kind, such as "FB", "FC", "OB", "GlobalDB", "InstanceDB", "PlcStruct", "PlcTagTable" or "PlcExternalSource".
        /// </summary>
        [JsonProperty("kind")]
        public string Kind { get; set; } = string.Empty;

        [JsonProperty("number", NullValueHandling = NullValueHandling.Ignore)]
        public int? Number { get; set; }

        [JsonProperty("programmingLanguage", NullValueHandling = NullValueHandling.Ignore)]
        public string? ProgrammingLanguage { get; set; }

        /// <summary>
        /// Tag tables only: whether this is the default tag table.
        /// </summary>
        [JsonProperty("isDefault", NullValueHandling = NullValueHandling.Ignore)]
        public bool? IsDefault { get; set; }

        /// <summary>
        /// Tag tables only: number of tags.
        /// </summary>
        [JsonProperty("tagCount", NullValueHandling = NullValueHandling.Ignore)]
        public int? TagCount { get; set; }
    }

    /// <summary>
    /// A system or user group of the software tree with its subgroups and objects.
    /// </summary>
    public class SoftwareTreeNode
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Group path relative to the root group, such as "Motion/Axis"; empty for the root group. This is the
        /// groupName the exchange tools take, and the directory below the root folder that preservePath writes.
        /// </summary>
        [JsonProperty("path")]
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Number of objects in this group and all its subgroups.
        /// </summary>
        [JsonProperty("totalCount")]
        public int TotalCount { get; set; }

        [JsonProperty("items", NullValueHandling = NullValueHandling.Ignore)]
        public IList<SoftwareTreeItem>? Items { get; set; }

        [JsonProperty("groups")]
        public IList<SoftwareTreeNode> Groups { get; set; } = new List<SoftwareTreeNode>();

        /// <summary>
        /// Sets <see cref="TotalCount"/> of this node and all subgroups from the given direct item counts and returns this node's total.
        /// </summary>
        public int ComputeTotalCount(int directItemCount)
        {
            var total = directItemCount;
            foreach (var group in Groups)
            {
                total += group.TotalCount;
            }

            TotalCount = total;
            return total;
        }
    }

    /// <summary>
    /// The project tree of one PLC software or Software Unit.
    /// </summary>
    public class SoftwareTree
    {
        [JsonProperty("path")]
        public string Path { get; set; } = string.Empty;

        [JsonProperty("deviceName")]
        public string DeviceName { get; set; } = string.Empty;

        [JsonProperty("unitName")]
        public string? UnitName { get; set; }

        [JsonProperty("programBlocks", NullValueHandling = NullValueHandling.Ignore)]
        public SoftwareTreeNode? ProgramBlocks { get; set; }

        [JsonProperty("plcDataTypes", NullValueHandling = NullValueHandling.Ignore)]
        public SoftwareTreeNode? PlcDataTypes { get; set; }

        [JsonProperty("plcTags", NullValueHandling = NullValueHandling.Ignore)]
        public SoftwareTreeNode? PlcTags { get; set; }

        [JsonProperty("externalSources", NullValueHandling = NullValueHandling.Ignore)]
        public SoftwareTreeNode? ExternalSources { get; set; }

        /// <summary>
        /// For the main PLC software of a CPU with Software Units: the units, each with its path. Use
        /// software_tree_get with unitName to read a unit's tree.
        /// </summary>
        [JsonProperty("units", NullValueHandling = NullValueHandling.Ignore)]
        public IList<PlcSoftwarePathInfo>? Units { get; set; }
    }
}
