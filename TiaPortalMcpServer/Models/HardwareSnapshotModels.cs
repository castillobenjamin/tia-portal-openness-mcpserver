using System.Collections.Generic;
using Newtonsoft.Json;

namespace TiaPortalMcpServer.Models
{
    /// <summary>
    /// Result of a hardware parameter snapshot: one JSON file per device below <see cref="SnapshotDirectory"/>.
    /// </summary>
    public class HardwareSnapshotResult
    {
        /// <summary>
        /// The directory below the export path that holds the snapshots ("Hardware").
        /// </summary>
        [JsonProperty("snapshotDirectory")]
        public string SnapshotDirectory { get; set; } = string.Empty;

        [JsonProperty("deviceCount")]
        public int DeviceCount { get; set; }

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
        /// Every snapshot file of this run, sorted; files not listed here are left over from earlier runs.
        /// </summary>
        [JsonProperty("files")]
        public IList<string> Files { get; set; } = new List<string>();

        [JsonProperty("unreadableCount")]
        public int UnreadableCount { get; set; }

        /// <summary>
        /// The first attributes that could not be read ("&lt;device&gt;: &lt;path&gt;: &lt;attribute&gt;: message"); also listed
        /// per object under "@unreadable".
        /// </summary>
        [JsonProperty("unreadable", NullValueHandling = NullValueHandling.Ignore)]
        public IList<string>? Unreadable { get; set; }
    }
}
