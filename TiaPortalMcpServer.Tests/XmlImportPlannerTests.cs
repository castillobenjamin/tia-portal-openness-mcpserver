using System.IO;
using System.Linq;
using TiaPortalMcpServer.Models;
using TiaPortalMcpServer.Services;
using Xunit;

namespace TiaPortalMcpServer.Tests
{
    [Trait("Category", "Unit")]
    public class XmlImportPlannerTests
    {
        [Fact]
        public void PlanDirectory_WithoutPreservePath_ImportsTopLevelFilesIntoGroup()
        {
            using (var files = new SimaticMlTestFiles())
            {
                files.Write("Motor_FB.xml", SimaticMlTestFiles.Block("Motor_FB"));
                files.Write(Path.Combine("Motion", "Axis_FB.xml"), SimaticMlTestFiles.Block("Axis_FB"));

                var plans = XmlImportPlanner.PlanDirectory(new DirectoryInfo(files.Root), "Imported", false, "", SimaticMlInspector.KindBlock);

                var plan = Assert.Single(plans);
                Assert.Equal("Motor_FB", plan.Content.Name);
                Assert.Equal("Imported", plan.GroupPath);
                Assert.Null(plan.Result);
            }
        }

        [Fact]
        public void PlanDirectory_WithPreservePath_MapsSubdirectoriesToGroups()
        {
            using (var files = new SimaticMlTestFiles())
            {
                files.Write("Main.xml", SimaticMlTestFiles.Block("Main"));
                files.Write(Path.Combine("Motion", "Axis", "Axis_FB.xml"), SimaticMlTestFiles.Block("Axis_FB"));

                var plans = XmlImportPlanner.PlanDirectory(new DirectoryInfo(files.Root), "Imported", true, "", SimaticMlInspector.KindBlock);

                Assert.Equal(2, plans.Count);
                Assert.Equal("Imported", plans.Single(p => p.Name == "Main").GroupPath);
                Assert.Equal("Imported/Motion/Axis", plans.Single(p => p.Name == "Axis_FB").GroupPath);
            }
        }

        [Fact]
        public void PlanDirectory_RegexFilter_MatchesFileNames()
        {
            using (var files = new SimaticMlTestFiles())
            {
                files.Write("Motor_FB.xml", SimaticMlTestFiles.Block("Motor_FB"));
                files.Write("Valve_FC.xml", SimaticMlTestFiles.Block("Valve_FC"));

                var plans = XmlImportPlanner.PlanDirectory(new DirectoryInfo(files.Root), "", false, "^valve", SimaticMlInspector.KindBlock);

                Assert.Equal("Valve_FC", Assert.Single(plans).Name);
            }
        }

        [Fact]
        public void PlanFile_OtherKind_IsSkipped()
        {
            using (var files = new SimaticMlTestFiles())
            {
                var path = files.Write("Axis_UDT.xml", SimaticMlTestFiles.Type("Axis_UDT"));

                var plan = XmlImportPlanner.PlanFile(new FileInfo(path), "", SimaticMlInspector.KindBlock);

                Assert.NotNull(plan.Result);
                Assert.Equal(DocumentOperationResult.StateSkipped, plan.Result.State);
                Assert.Equal("Axis_UDT", plan.Result.Name);
                Assert.Contains("PLC data type", plan.Result.Messages.Single());
            }
        }

        [Fact]
        public void PlanFile_InvalidFile_Fails()
        {
            using (var files = new SimaticMlTestFiles())
            {
                var path = files.Write("Broken.xml", "<NotSimaticMl />");

                var plan = XmlImportPlanner.PlanFile(new FileInfo(path), "", SimaticMlInspector.KindType);

                Assert.Equal(DocumentOperationResult.StateFailure, plan.Result.State);
                Assert.Equal("Broken", plan.Result.Name);
                Assert.Equal(path, plan.Result.Files.Single());
            }
        }

        [Fact]
        public void PlanFile_MissingName_Fails()
        {
            using (var files = new SimaticMlTestFiles())
            {
                var path = files.Write("NoName.xml", "<Document><SW.Types.PlcStruct><AttributeList /></SW.Types.PlcStruct></Document>");

                var plan = XmlImportPlanner.PlanFile(new FileInfo(path), "", SimaticMlInspector.KindType);

                Assert.Equal(DocumentOperationResult.StateFailure, plan.Result.State);
                Assert.Contains("No object name", plan.Result.Messages.Single());
            }
        }

        [Fact]
        public void BatchResult_CountsSkippedSeparately()
        {
            var batch = DocumentBatchResult.From(new[]
            {
                new DocumentOperationResult { State = DocumentOperationResult.StateSuccess },
                new DocumentOperationResult { State = DocumentOperationResult.StateFailure },
                new DocumentOperationResult { State = DocumentOperationResult.StateSkipped },
                new DocumentOperationResult { State = DocumentOperationResult.StateValidated }
            });

            Assert.Equal(2, batch.SucceededCount);
            Assert.Equal(1, batch.FailedCount);
            Assert.Equal(1, batch.SkippedCount);
        }
    }
}
