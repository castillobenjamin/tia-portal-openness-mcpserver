using System.Collections.Generic;
using Newtonsoft.Json;

namespace TiaPortalMcpServer.Models
{
    /// <summary>
    /// Result of exporting or importing one exchange file: a SIMATIC SD document pair (.s7dcl/.s7res) or a SimaticML (.xml) file.
    /// </summary>
    public class DocumentOperationResult
    {
        public const string StateSuccess = "Success";
        public const string StatePartialSuccess = "PartialSuccess";
        public const string StateFailure = "Failure";
        public const string StateValidated = "Validated";
        public const string StateSkipped = "Skipped";

        /// <summary>
        /// Block or type name (export) or file name without extension (import).
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// "Success", "PartialSuccess" or "Failure" as reported by TIA Portal, "Validated" for a successful dry run,
        /// or "Skipped" for a file that does not hold the kind of object being imported.
        /// </summary>
        [JsonProperty("state")]
        public string State { get; set; } = StateSuccess;

        /// <summary>
        /// Block or type group path such as "Motion/Axis"; empty for the root group.
        /// </summary>
        [JsonProperty("groupPath", NullValueHandling = NullValueHandling.Ignore)]
        public string? GroupPath { get; set; }

        [JsonProperty("files", NullValueHandling = NullValueHandling.Ignore)]
        public IList<string>? Files { get; set; }

        [JsonProperty("blocks", NullValueHandling = NullValueHandling.Ignore)]
        public IList<BlockInfo>? Blocks { get; set; }

        [JsonProperty("types", NullValueHandling = NullValueHandling.Ignore)]
        public IList<PlcTypeInfo>? Types { get; set; }

        /// <summary>
        /// Names of the PLC tag tables an import created or replaced.
        /// </summary>
        [JsonProperty("tagTables", NullValueHandling = NullValueHandling.Ignore)]
        public IList<string>? TagTables { get; set; }

        /// <summary>
        /// Messages logged by TIA Portal during the operation, plus validation notes and errors.
        /// </summary>
        [JsonProperty("messages")]
        public IList<string> Messages { get; set; } = new List<string>();

        [JsonIgnore]
        public bool IsFailure => State == StateFailure;

        [JsonIgnore]
        public bool IsSkipped => State == StateSkipped;
    }

    /// <summary>
    /// Result of a bulk export or import. Each item is processed independently.
    /// </summary>
    public class DocumentBatchResult
    {
        [JsonProperty("succeededCount")]
        public int SucceededCount { get; set; }

        [JsonProperty("failedCount")]
        public int FailedCount { get; set; }

        [JsonProperty("skippedCount")]
        public int SkippedCount { get; set; }

        [JsonProperty("items")]
        public IList<DocumentOperationResult> Items { get; set; } = new List<DocumentOperationResult>();

        public static DocumentBatchResult From(IList<DocumentOperationResult> items)
        {
            var failed = 0;
            var skipped = 0;
            foreach (var item in items)
            {
                if (item.IsFailure)
                {
                    failed++;
                }
                else if (item.IsSkipped)
                {
                    skipped++;
                }
            }

            return new DocumentBatchResult
            {
                SucceededCount = items.Count - failed - skipped,
                FailedCount = failed,
                SkippedCount = skipped,
                Items = items
            };
        }
    }
}
