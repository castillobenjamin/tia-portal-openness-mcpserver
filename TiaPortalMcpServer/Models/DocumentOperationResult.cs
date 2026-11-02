using System.Collections.Generic;
using Newtonsoft.Json;

namespace TiaPortalMcpServer.Models
{
    /// <summary>
    /// Result of exporting or importing one SIMATIC SD document pair (.s7dcl/.s7res).
    /// </summary>
    public class DocumentOperationResult
    {
        public const string StateSuccess = "Success";
        public const string StatePartialSuccess = "PartialSuccess";
        public const string StateFailure = "Failure";
        public const string StateValidated = "Validated";

        /// <summary>
        /// Block name (export) or document file name without extension (import).
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// "Success", "PartialSuccess" or "Failure" as reported by TIA Portal, or "Validated" for a successful dry run.
        /// </summary>
        [JsonProperty("state")]
        public string State { get; set; } = StateSuccess;

        [JsonProperty("files", NullValueHandling = NullValueHandling.Ignore)]
        public IList<string>? Files { get; set; }

        [JsonProperty("blocks", NullValueHandling = NullValueHandling.Ignore)]
        public IList<BlockInfo>? Blocks { get; set; }

        /// <summary>
        /// Messages logged by TIA Portal during the operation, plus validation notes and errors.
        /// </summary>
        [JsonProperty("messages")]
        public IList<string> Messages { get; set; } = new List<string>();

        [JsonIgnore]
        public bool IsFailure => State == StateFailure;
    }

    /// <summary>
    /// Result of a bulk SIMATIC SD document export or import. Each item is processed independently.
    /// </summary>
    public class DocumentBatchResult
    {
        [JsonProperty("succeededCount")]
        public int SucceededCount { get; set; }

        [JsonProperty("failedCount")]
        public int FailedCount { get; set; }

        [JsonProperty("items")]
        public IList<DocumentOperationResult> Items { get; set; } = new List<DocumentOperationResult>();

        public static DocumentBatchResult From(IList<DocumentOperationResult> items)
        {
            var failed = 0;
            foreach (var item in items)
            {
                if (item.IsFailure)
                {
                    failed++;
                }
            }

            return new DocumentBatchResult
            {
                SucceededCount = items.Count - failed,
                FailedCount = failed,
                Items = items
            };
        }
    }
}
