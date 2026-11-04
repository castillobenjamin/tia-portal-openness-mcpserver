using System.Collections.Generic;
using System.Linq;
using TiaPortalMcpServer.Models;
using Xunit;

namespace TiaPortalMcpServer.Tests
{
    [Trait("Category", "Unit")]
    public class CompileReportTests
    {
        private static CompileMessageNode Node(string path, string state, string description, params CompileMessageNode[] children)
        {
            return new CompileMessageNode { Path = path, State = state, Description = description, Children = children.ToList() };
        }

        // Shape of a TIA Portal compile result: PLC > Program blocks > group > block > message
        private static List<CompileMessageNode> SampleTree()
        {
            return new List<CompileMessageNode>
            {
                Node("MSPLC_M2", "Error", "Compiling finished (errors: 1; warnings: 1)",
                    Node("Program blocks", "Error", "",
                        Node("Motion", "Error", "",
                            Node("Axis_FB", "Error", "Block was not compiled",
                                Node("Network 2", "Error", "Tag \"Speed\" is not defined."),
                                Node("", "Warning", "Unused variable 'tmp'."))),
                        Node("Main", "Success", "",
                            Node("", "Success", "Block was successfully compiled."))))
            };
        }

        [Fact]
        public void AddMessages_FlattensLeavesWithObjectPathAndLocation()
        {
            var report = new CompileReport();
            report.AddMessages(SampleTree(), CompileReport.FilterErrorsAndWarnings, 0);

            Assert.Equal(2, report.MatchedMessageCount);
            Assert.False(report.Truncated);

            var error = report.Messages[0];
            Assert.Equal("MSPLC_M2/Program blocks/Motion/Axis_FB", error.Path);
            Assert.Equal("Network 2", error.Location);
            Assert.Equal("Error", error.State);
            Assert.Equal("Tag \"Speed\" is not defined.", error.Description);

            var warning = report.Messages[1];
            Assert.Equal("MSPLC_M2/Program blocks/Motion/Axis_FB", warning.Path);
            Assert.Null(warning.Location);
            Assert.Equal("Warning", warning.State);
        }

        [Fact]
        public void AddMessages_ErrorsFilter_DropsWarnings()
        {
            var report = new CompileReport();
            report.AddMessages(SampleTree(), CompileReport.FilterErrors, 0);

            Assert.Equal("Error", Assert.Single(report.Messages).State);
        }

        [Fact]
        public void AddMessages_AllFilter_IncludesSuccessMessages()
        {
            var report = new CompileReport();
            report.AddMessages(SampleTree(), CompileReport.FilterAll, 0);

            Assert.Equal(3, report.Messages.Count);
            Assert.Equal("MSPLC_M2/Program blocks/Main", report.Messages[2].Path);
        }

        [Fact]
        public void AddMessages_MaxMessages_TruncatesButCountsAll()
        {
            var report = new CompileReport();
            report.AddMessages(SampleTree(), CompileReport.FilterAll, 1);

            Assert.Single(report.Messages);
            Assert.Equal(3, report.MatchedMessageCount);
            Assert.True(report.Truncated);
        }

        [Theory]
        [InlineData(null, "ErrorsAndWarnings")]
        [InlineData("", "ErrorsAndWarnings")]
        [InlineData("errors", "Errors")]
        [InlineData(" ALL ", "All")]
        [InlineData("Warnings", null)]
        public void NormalizeFilter_AcceptsKnownNamesCaseInsensitive(string filter, string expected)
        {
            Assert.Equal(expected, CompileReport.NormalizeFilter(filter));
        }
    }
}
