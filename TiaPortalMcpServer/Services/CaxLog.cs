using System.Collections.Generic;
using System.Globalization;
using System.Text;
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
        /// to <paramref name="result"/>, up to <see cref="MaxReportedMessages"/> each.
        /// </summary>
        public static void CollectProblems(IEnumerable<CaxMessage> messages, CaxExportResult result)
        {
            CollectProblems(messages, string.Empty, result);
        }

        private static void CollectProblems(IEnumerable<CaxMessage> messages, string prefix, CaxExportResult result)
        {
            foreach (var message in messages)
            {
                var text = prefix.Length == 0 ? message.Message : prefix + " > " + message.Message;
                var target = message.State == StateError ? result.Errors
                    : message.State == StateWarning ? result.Warnings
                    : null;
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
