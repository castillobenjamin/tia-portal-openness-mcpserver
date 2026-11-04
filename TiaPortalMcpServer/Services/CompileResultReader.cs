using System.Collections.Generic;
using Siemens.Engineering.Compiler;
using TiaPortalMcpServer.Models;

namespace TiaPortalMcpServer.Services
{
    /// <summary>
    /// Turns a TIA Portal <see cref="CompilerResult"/> into a <see cref="CompileReport"/> with flattened, filtered messages.
    /// </summary>
    public static class CompileResultReader
    {
        public static CompileReport ToReport(string target, CompilerResult result, string filter, int maxMessages)
        {
            var report = new CompileReport
            {
                Target = target,
                State = result.State.ToString(),
                ErrorCount = result.ErrorCount,
                WarningCount = result.WarningCount
            };

            report.AddMessages(ToNodes(result.Messages), filter, maxMessages);
            return report;
        }

        private static List<CompileMessageNode> ToNodes(CompilerResultMessageComposition messages)
        {
            var nodes = new List<CompileMessageNode>();
            foreach (CompilerResultMessage message in messages)
            {
                nodes.Add(new CompileMessageNode
                {
                    Path = message.Path ?? string.Empty,
                    State = message.State.ToString(),
                    Description = message.Description ?? string.Empty,
                    Children = ToNodes(message.Messages)
                });
            }

            return nodes;
        }
    }
}
