using System.Collections.Generic;
using Newtonsoft.Json;

namespace TiaPortalMcpServer.Models
{
    /// <summary>
    /// A WinCC Unified HMI software in the project.
    /// </summary>
    public class HmiUnifiedSoftwareInfo
    {
        [JsonProperty("deviceName")]
        public string DeviceName { get; set; } = string.Empty;

        [JsonProperty("softwareName")]
        public string SoftwareName { get; set; } = string.Empty;

        [JsonProperty("deviceGroupPath")]
        public string DeviceGroupPath { get; set; } = string.Empty;

        [JsonProperty("screenCount")]
        public int ScreenCount { get; set; }

        [JsonProperty("tagCount")]
        public int TagCount { get; set; }

        [JsonProperty("tagTableCount")]
        public int TagTableCount { get; set; }

        [JsonProperty("connectionCount")]
        public int ConnectionCount { get; set; }

        [JsonProperty("textListCount")]
        public int TextListCount { get; set; }

        [JsonProperty("graphicListCount")]
        public int GraphicListCount { get; set; }

        [JsonProperty("systemTextListCount")]
        public int SystemTextListCount { get; set; }
    }

    public class HmiScreenInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Screen group path such as "Overview/Lines"; empty for screens at the top level.
        /// </summary>
        [JsonProperty("groupPath")]
        public string GroupPath { get; set; } = string.Empty;

        [JsonProperty("screenNumber", NullValueHandling = NullValueHandling.Ignore)]
        public int? ScreenNumber { get; set; }

        [JsonProperty("width", NullValueHandling = NullValueHandling.Ignore)]
        public long? Width { get; set; }

        [JsonProperty("height", NullValueHandling = NullValueHandling.Ignore)]
        public long? Height { get; set; }

        [JsonProperty("itemCount")]
        public int ItemCount { get; set; }
    }

    public class HmiScreenItemInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Screen item type without the "Hmi" prefix, such as "Button", "IOField", "Rectangle" or "Text".
        /// </summary>
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;

        [JsonProperty("left", NullValueHandling = NullValueHandling.Ignore)]
        public long? Left { get; set; }

        [JsonProperty("top", NullValueHandling = NullValueHandling.Ignore)]
        public long? Top { get; set; }

        [JsonProperty("width", NullValueHandling = NullValueHandling.Ignore)]
        public long? Width { get; set; }

        [JsonProperty("height", NullValueHandling = NullValueHandling.Ignore)]
        public long? Height { get; set; }

        [JsonProperty("visible", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Visible { get; set; }

        [JsonProperty("enabled", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Enabled { get; set; }

        /// <summary>
        /// Further attributes when requested, as returned by TIA Portal (simple values only).
        /// </summary>
        [JsonProperty("attributes", NullValueHandling = NullValueHandling.Ignore)]
        public IDictionary<string, object?>? Attributes { get; set; }
    }

    public class HmiTagTableInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Tag table group path; empty for tag tables at the top level.
        /// </summary>
        [JsonProperty("groupPath")]
        public string GroupPath { get; set; } = string.Empty;

        [JsonProperty("tagCount")]
        public int TagCount { get; set; }
    }

    public class HmiUnifiedTagInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("tagTableName", NullValueHandling = NullValueHandling.Ignore)]
        public string? TagTableName { get; set; }

        [JsonProperty("dataType", NullValueHandling = NullValueHandling.Ignore)]
        public string? DataType { get; set; }

        [JsonProperty("hmiDataType", NullValueHandling = NullValueHandling.Ignore)]
        public string? HmiDataType { get; set; }

        /// <summary>
        /// HMI connection name; empty for internal tags.
        /// </summary>
        [JsonProperty("connection", NullValueHandling = NullValueHandling.Ignore)]
        public string? Connection { get; set; }

        [JsonProperty("plcTag", NullValueHandling = NullValueHandling.Ignore)]
        public string? PlcTag { get; set; }

        [JsonProperty("address", NullValueHandling = NullValueHandling.Ignore)]
        public string? Address { get; set; }

        [JsonProperty("acquisitionCycle", NullValueHandling = NullValueHandling.Ignore)]
        public string? AcquisitionCycle { get; set; }

        [JsonProperty("acquisitionMode", NullValueHandling = NullValueHandling.Ignore)]
        public string? AcquisitionMode { get; set; }
    }

    public class HmiConnectionInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("communicationDriver", NullValueHandling = NullValueHandling.Ignore)]
        public string? CommunicationDriver { get; set; }

        [JsonProperty("partner", NullValueHandling = NullValueHandling.Ignore)]
        public string? Partner { get; set; }

        [JsonProperty("station", NullValueHandling = NullValueHandling.Ignore)]
        public string? Station { get; set; }

        [JsonProperty("node", NullValueHandling = NullValueHandling.Ignore)]
        public string? Node { get; set; }

        [JsonProperty("initialAddress", NullValueHandling = NullValueHandling.Ignore)]
        public string? InitialAddress { get; set; }

        [JsonProperty("comment", NullValueHandling = NullValueHandling.Ignore)]
        public string? Comment { get; set; }
    }

    /// <summary>
    /// Result of a WinCC Unified write operation (create, delete, set attributes, import, export).
    /// </summary>
    public class HmiScreenSnapshotResult
    {
        [JsonProperty("deviceName")]
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>
        /// The "Screens" directory below the export path that holds the snapshots.
        /// </summary>
        [JsonProperty("snapshotDirectory")]
        public string SnapshotDirectory { get; set; } = string.Empty;

        [JsonProperty("screenCount")]
        public int ScreenCount { get; set; }

        /// <summary>
        /// Files rewritten because their content changed (or they are new).
        /// </summary>
        [JsonProperty("writtenCount")]
        public int WrittenCount { get; set; }

        /// <summary>
        /// Files that already held exactly the snapshot content and were left untouched.
        /// </summary>
        [JsonProperty("unchangedCount")]
        public int UnchangedCount { get; set; }

        /// <summary>
        /// Every snapshot and script file of this run, sorted; files not listed here are left over from earlier runs.
        /// </summary>
        [JsonProperty("files")]
        public IList<string> Files { get; set; } = new List<string>();

        [JsonProperty("unreadableCount")]
        public int UnreadableCount { get; set; }

        /// <summary>
        /// The first properties that could not be read ("Main: Button_1.Font.Size: message"); also listed per object under "@unreadable".
        /// </summary>
        [JsonProperty("unreadable", NullValueHandling = NullValueHandling.Ignore)]
        public IList<string>? Unreadable { get; set; }
    }

    public class HmiOperationResult
    {
        [JsonProperty("deviceName")]
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>
        /// The object acted on, such as "screen 'Main'" or "item 'Button_1' on screen 'Main'".
        /// </summary>
        [JsonProperty("target")]
        public string Target { get; set; } = string.Empty;

        [JsonProperty("dryRun")]
        public bool DryRun { get; set; }

        [JsonProperty("changes")]
        public IList<string> Changes { get; set; } = new List<string>();

        [JsonProperty("files", NullValueHandling = NullValueHandling.Ignore)]
        public IList<string>? Files { get; set; }
    }
}
