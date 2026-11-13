using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TiaPortalMcpServer.Models;

namespace TiaPortalMcpServer.Services
{
    /// <summary>
    /// Formats CAx transfer messages as a log file and picks out the errors and warnings for the tool response.
    /// </summary>
    public static class CaxLog
    {
        public const string StateError = "Error";
        public const string StateWarning = "Warning";

        /// <summary>
        /// Errors and warnings returned per list; the log file holds all messages.
        /// </summary>
        public const int MaxReportedMessages = 100;

        /// <summary>
        /// One line per message, "[timestamp] State: message", indented two spaces per nesting level.
        /// Without timestamps, an unchanged export gives an identical log.
        /// </summary>
        public static string Format(IEnumerable<CaxMessage> messages, bool includeTimestamps)
        {
            var builder = new StringBuilder();
            AppendMessages(builder, messages, 0, includeTimestamps);
            return builder.ToString();
        }

        /// <summary>
        /// Adds the error and warning messages, with the messages they are nested in as a " > " separated prefix,
        /// to <paramref name="result"/>, up to <see cref="MaxReportedMessages"/> each. TIA Portal passes the state of a
        /// message up to the messages it is nested in ("The export is being processed" > "TabletHMI is being exported."
        /// > the actual error), so a message is only reported where the problem starts: when none of its children has
        /// the same or a more severe state. The parents stay in the prefix.
        /// </summary>
        /// <remarks>
        /// TIA Portal's closing message ("Exporting of the CAx data is completed (errors: 1, warnings: 0)") carries the
        /// overall state; it goes to <see cref="CaxExportResult.Summary"/> instead of the errors or warnings.
        /// </remarks>
        public static void CollectProblems(IEnumerable<CaxMessage> messages, CaxExportResult result)
        {
            CollectProblems(messages, string.Empty, result);
        }

        private static readonly Regex SummaryPattern = new Regex(@"\(\s*errors\s*:\s*\d+\s*,\s*warnings\s*:\s*\d+\s*\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Whether <paramref name="message"/> is TIA Portal's closing count of errors and warnings.
        /// </summary>
        public static bool IsSummary(CaxMessage message)
        {
            return message.Children.Count == 0 && SummaryPattern.IsMatch(message.Message);
        }

        private static int Severity(string state)
        {
            return state == StateError ? 2 : state == StateWarning ? 1 : 0;
        }

        private static bool ChildIsAsSevere(CaxMessage message, int severity)
        {
            foreach (var child in message.Children)
            {
                if (Severity(child.State) >= severity)
                {
                    return true;
                }
            }
            return false;
        }

        private static void CollectProblems(IEnumerable<CaxMessage> messages, string prefix, CaxExportResult result)
        {
            foreach (var message in messages)
            {
                if (IsSummary(message))
                {
                    result.Summary = message.Message;
                    continue;
                }

                var text = prefix.Length == 0 ? message.Message : prefix + " > " + message.Message;
                var severity = Severity(message.State);
                var target = severity == 0 || ChildIsAsSevere(message, severity) ? null
                    : severity == 2 ? result.Errors
                    : result.Warnings;
                if (target != null)
                {
                    if (target.Count < MaxReportedMessages)
                    {
                        target.Add(text);
                    }
                    else
                    {
                        result.MessagesTruncated = true;
                    }
                }

                CollectProblems(message.Children, text, result);
            }
        }

        private static void AppendMessages(StringBuilder builder, IEnumerable<CaxMessage> messages, int depth, bool includeTimestamps)
        {
            foreach (var message in messages)
            {
                builder.Append(' ', depth * 2);
                if (includeTimestamps && message.DateTime.HasValue)
                {
                    builder.Append('[')
                        .Append(message.DateTime.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                        .Append("] ");
                }
                builder.Append(message.State).Append(": ").Append(message.Message).Append("\r\n");
                AppendMessages(builder, message.Children, depth + 1, includeTimestamps);
            }
        }
    }
}
