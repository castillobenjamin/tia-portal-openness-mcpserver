using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace TiaPortalMcpServer.Models
{
    /// <summary>
    /// One node of the compiler's message tree, copied out of TIA Portal so it can be flattened without it.
    /// </summary>
    public class CompileMessageNode
    {
        /// <summary>
        /// The object or location the node stands for, such as "MSPLC_M2", "Program blocks", "Axis_FB" or "Network 2".
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// "Error", "Warning", "Information" or "Success".
        /// </summary>
        public string State { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public IList<CompileMessageNode> Children { get; set; } = new List<CompileMessageNode>();
    }

    /// <summary>
    /// A compiler message with the path of the object it belongs to.
    /// </summary>
    public class CompileMessageInfo
    {
        /// <summary>
        /// Object path from the compiled item down to the object, such as "MSPLC_M2/Program blocks/Motion/Axis_FB".
        /// </summary>
        [JsonProperty("path")]
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Location inside the object when the compiler gives one, such as "Network 2".
        /// </summary>
        [JsonProperty("location", NullValueHandling = NullValueHandling.Ignore)]
        public string? Location { get; set; }

        [JsonProperty("state")]
        public string State { get; set; } = string.Empty;

        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// Result of a compilation with the messages flattened and filtered.
    /// </summary>
    public class CompileReport
    {
        public const string FilterErrors = "Errors";
        public const string FilterErrorsAndWarnings = "ErrorsAndWarnings";
        public const string FilterAll = "All";

        /// <summary>
        /// What was compiled, such as "project 'Line'", "MSPLC_M2/SWS_Main" or "block 'Axis_FB' in MSPLC_M2".
        /// </summary>
        [JsonProperty("target")]
        public string Target { get; set; } = string.Empty;

        /// <summary>
        /// Overall state reported by TIA Portal: "Success", "Information", "Warning" or "Error".
        /// </summary>
        [JsonProperty("state")]
        public string State { get; set; } = string.Empty;

        [JsonProperty("errorCount")]
        public int ErrorCount { get; set; }

        [JsonProperty("warningCount")]
        public int WarningCount { get; set; }

        /// <summary>
        /// Number of messages that matched the filter, including those left out by the message limit.
        /// </summary>
        [JsonProperty("matchedMessageCount")]
        public int MatchedMessageCount { get; set; }

        /// <summary>
        /// True when more messages matched than <see cref="Messages"/> holds.
        /// </summary>
        [JsonProperty("truncated")]
        public bool Truncated { get; set; }

        [JsonProperty("messages")]
        public IList<CompileMessageInfo> Messages { get; set; } = new List<CompileMessageInfo>();

        /// <summary>
        /// Checks a messageFilter parameter value; returns the canonical name or null when invalid.
        /// </summary>
        public static string? NormalizeFilter(string? filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                return FilterErrorsAndWarnings;
            }

            foreach (var name in new[] { FilterErrors, FilterErrorsAndWarnings, FilterAll })
            {
                if (string.Equals(name, filter!.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }

            return null;
        }

        /// <summary>
        /// Flattens the message tree into one entry per leaf message (the messages that describe a problem; inner nodes
        /// only summarize their children). Each entry gets the path of its ancestors; the leaf's own path is its location.
        /// Keeps the messages that pass <paramref name="filter"/>, up to <paramref name="maxMessages"/> (0 for no limit).
        /// </summary>
        public void AddMessages(IEnumerable<CompileMessageNode> roots, string filter, int maxMessages)
        {
            foreach (var root in roots)
            {
                Collect(root, new List<string>(), filter, maxMessages);
            }
        }

        private void Collect(CompileMessageNode node, List<string> ancestors, string filter, int maxMessages)
        {
            if (node.Children.Count > 0)
            {
                var pushed = !string.IsNullOrWhiteSpace(node.Path);
                if (pushed)
                {
                    ancestors.Add(node.Path.Trim());
                }

                foreach (var child in node.Children)
                {
                    Collect(child, ancestors, filter, maxMessages);
                }

                if (pushed)
                {
                    ancestors.RemoveAt(ancestors.Count - 1);
                }

                return;
            }

            if (string.IsNullOrWhiteSpace(node.Description) || !Matches(node.State, filter))
            {
                return;
            }

            MatchedMessageCount++;
            if (maxMessages > 0 && Messages.Count >= maxMessages)
            {
                Truncated = true;
                return;
            }

            Messages.Add(new CompileMessageInfo
            {
                Path = string.Join("/", ancestors),
                Location = string.IsNullOrWhiteSpace(node.Path) ? null : node.Path.Trim(),
                State = node.State,
                Description = node.Description.Trim()
            });
        }

        private static bool Matches(string state, string filter)
        {
            switch (filter)
            {
                case FilterErrors:
                    return state == "Error";
                case FilterErrorsAndWarnings:
                    return state == "Error" || state == "Warning";
                default:
                    return true;
            }
        }
    }
}
