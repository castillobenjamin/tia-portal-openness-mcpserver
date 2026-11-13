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
        public void PlanDirectory_WithPreservePath_ReadsExportedProjectTree()
        {
            using (var files = new SimaticMlTestFiles())
            {
                // Layout written by blocks_export_all_as_xml and types_export_all_as_xml with preservePath=true
                files.Write(Path.Combine("Program blocks", "Motion", "Axis_FB.xml"), SimaticMlTestFiles.Block("Axis_FB"));
                files.Write(Path.Combine("PLC data types", "Motion", "Axis_UDT.xml"), SimaticMlTestFiles.Type("Axis_UDT"));

                var blockPlans = XmlImportPlanner.PlanDirectory(new DirectoryInfo(files.Root), "", true, "", SimaticMlInspector.KindBlock);
                var typePlans = XmlImportPlanner.PlanDirectory(new DirectoryInfo(files.Root), "", true, "", SimaticMlInspector.KindType);

                var blockPlan = Assert.Single(blockPlans);
                Assert.Equal("Axis_FB", blockPlan.Name);
                Assert.Equal("Motion", blockPlan.GroupPath);
                Assert.Null(blockPlan.Result);

                var typePlan = Assert.Single(typePlans);
                Assert.Equal("Axis_UDT", typePlan.Name);
                Assert.Equal("Motion", typePlan.GroupPath);
                Assert.Null(typePlan.Result);
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
        public void PlanDirectory_TagTables_WithPreservePath_ReadsExportedProjectTree()
        {
            using (var files = new SimaticMlTestFiles())
            {
                // Layout written by tags_tagtable_export_all with preservePath=true
                files.Write(Path.Combine("PLC tags", "Default tag table.xml"), SimaticMlTestFiles.TagTable("Default tag table"));
                files.Write(Path.Combine("PLC tags", "Units", "SWS_IO.xml"), SimaticMlTestFiles.TagTable("SWS_IO"));
                files.Write(Path.Combine("Program blocks", "Main.xml"), SimaticMlTestFiles.Block("Main"));

                var plans = XmlImportPlanner.PlanDirectory(new DirectoryInfo(files.Root), "", true, "", SimaticMlInspector.KindTagTable);

                Assert.Equal(2, plans.Count);
                Assert.Equal("", plans.Single(p => p.Name == "Default tag table").GroupPath);
                Assert.Equal("Units", plans.Single(p => p.Name == "SWS_IO").GroupPath);
                Assert.All(plans, p => Assert.Null(p.Result));
            }
        }

        [Fact]
        public void PlanFile_BlockFileForTagTableImport_IsSkipped()
        {
            using (var files = new SimaticMlTestFiles())
            {
                var path = files.Write("Main.xml", SimaticMlTestFiles.Block("Main"));

                var plan = XmlImportPlanner.PlanFile(new FileInfo(path), "", SimaticMlInspector.KindTagTable);

                Assert.Equal(DocumentOperationResult.StateSkipped, plan.Result.State);
            }
        }

        [Fact]
        public void PlanFile_TagTableFileForBlockImport_IsSkipped()
        {
            using (var files = new SimaticMlTestFiles())
            {
                var path = files.Write("Default tag table.xml", SimaticMlTestFiles.TagTable("Default tag table"));

                var plan = XmlImportPlanner.PlanFile(new FileInfo(path), "", SimaticMlInspector.KindBlock);

                Assert.Equal(DocumentOperationResult.StateSkipped, plan.Result.State);
                Assert.Contains("PLC tag table", plan.Result.Messages.Single());
            }
        }

        [Theory]
        [InlineData(SimaticMlInspector.KindBlock, ExchangePaths.BlocksRootFolder)]
        [InlineData(SimaticMlInspector.KindType, ExchangePaths.TypesRootFolder)]
        [InlineData(SimaticMlInspector.KindTagTable, ExchangePaths.TagTablesRootFolder)]
        public void GetRootFolder_MapsKindToProjectTreeFolder(string kind, string expected)
        {
            Assert.Equal(expected, XmlImportPlanner.GetRootFolder(kind));
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
