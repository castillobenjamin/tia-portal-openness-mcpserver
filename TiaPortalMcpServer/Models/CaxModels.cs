using System.Collections.Generic;
using Newtonsoft.Json;

namespace TiaPortalMcpServer.Models
{
    /// <summary>
    /// Result of a CAx (AutomationML) export of the project or one device.
    /// </summary>
    public class CaxExportResult
    {
        /// <summary>
        /// Overall state reported by TIA Portal: "Success", "Information", "Warning" or "Error".
        /// An export with errors can still write a file that holds the objects TIA Portal could export.
        /// </summary>
        [JsonProperty("state")]
        public string State { get; set; } = string.Empty;

        /// <summary>
        /// What was exported: "project 'Name'" or "device 'Name'".
        /// </summary>
        [JsonProperty("scope")]
        public string Scope { get; set; } = string.Empty;

        [JsonProperty("file")]
        public string File { get; set; } = string.Empty;

        [JsonProperty("logFile")]
        public string LogFile { get; set; } = string.Empty;

        [JsonProperty("errorCount")]
        public int ErrorCount { get; set; }

        [JsonProperty("warningCount")]
        public int WarningCount { get; set; }

        /// <summary>
        /// Error messages, each prefixed with the messages it is nested in (" > " separated).
        /// </summary>
        [JsonProperty("errors")]
        public IList<string> Errors { get; set; } = new List<string>();

        [JsonProperty("warnings")]
        public IList<string> Warnings { get; set; } = new List<string>();

        /// <summary>
        /// True when more errors or warnings were logged than returned; the log file holds all of them.
        /// </summary>
        [JsonProperty("messagesTruncated", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool MessagesTruncated { get; set; }

        /// <summary>
        /// What normalize=true rewrote; absent without normalize.
        /// </summary>
        [JsonProperty("normalization", NullValueHandling = NullValueHandling.Ignore)]
        public AmlNormalizationResult? Normalization { get; set; }
    }

    /// <summary>
    /// Counts of the values <see cref="Services.AmlNormalizer"/> rewrote.
    /// </summary>
    public class AmlNormalizationResult
    {
        /// <summary>
        /// ID attributes replaced with IDs derived from the element path.
        /// </summary>
        [JsonProperty("idsRewritten")]
        public int IdsRewritten { get; set; }

        /// <summary>
        /// References to those IDs (links, mirror objects, values) rewritten to match.
        /// </summary>
        [JsonProperty("referencesRewritten")]
        public int ReferencesRewritten { get; set; }

        /// <summary>
        /// Write timestamps set to <see cref="Services.AmlNormalizer.FixedTimestamp"/>.
        /// </summary>
        [JsonProperty("timestampsRewritten")]
        public int TimestampsRewritten { get; set; }

        [JsonProperty("fileChanged")]
        public bool FileChanged { get; set; }
    }

    /// <summary>
    /// One CAx transfer message, copied out of the TIA Portal result so it can be formatted without Openness.
    /// </summary>
    public class CaxMessage
    {
        public string State { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public System.DateTime? DateTime { get; set; }

        public IList<CaxMessage> Children { get; set; } = new List<CaxMessage>();
    }
}
