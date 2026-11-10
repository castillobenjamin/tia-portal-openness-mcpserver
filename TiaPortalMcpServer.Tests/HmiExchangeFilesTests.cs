using System.IO;
using TiaPortalMcpServer.Services;
using Xunit;

namespace TiaPortalMcpServer.Tests
{
    [Trait("Category", "Unit")]
    public class HmiExchangeFilesTests
    {
        [Theory]
        [InlineData("GraphicLists", "GraphicLists")]
        [InlineData(" GraphicLists ", "GraphicLists")]
        [InlineData("GraphicLists.hmi.yml", "GraphicLists")]
        [InlineData("GraphicLists.HMI.YML", "GraphicLists")]
        [InlineData("GraphicLists.xlsx", "GraphicLists.xlsx")]
        [InlineData(".hmi.yml", ".hmi.yml")]
        public void ExportBaseName_DropsTrailingYamlExtension(string fileName, string expected)
        {
            Assert.Equal(expected, HmiExchangeFiles.ExportBaseName(fileName));
        }

        [Fact]
        public void FindImportFile_BaseName_FindsTheExportedYamlFile()
        {
            using (var files = new SimaticMlTestFiles())
            {
                var written = files.Write("GraphicLists.hmi.yml", "#Version: 2.0");

                var found = HmiExchangeFiles.FindImportFile(files.Root, "GraphicLists");

                Assert.Equal(written, found.FullName);
            }
        }

        [Fact]
        public void FindImportFile_FullName_IsUsedAsGiven()
        {
            using (var files = new SimaticMlTestFiles())
            {
                var written = files.Write("SystemTextLists.hmi.yml", "#Version: 2.0");
                files.Write("SystemTextLists.TextLibrary.hmi.yml", "#Version: 2.0");

                var found = HmiExchangeFiles.FindImportFile(files.Root, "SystemTextLists.hmi.yml");

                Assert.Equal(written, found.FullName);
            }
        }

        [Fact]
        public void FindTagTableDirectories_MapsDirectoriesToGroupsAndTables()
        {
            using (var files = new SimaticMlTestFiles())
            {
                files.Write("stray.txt", "x");
                files.Write(Path.Combine("Default tag table", "Tags.hmi.yml"), "x");
                files.Write(Path.Combine("Alarms", "Station", "HmiAlarmTags", "Tags.hmi.yml"), "x");
                Directory.CreateDirectory(Path.Combine(files.Root, "Empty"));

                var found = HmiExchangeFiles.FindTagTableDirectories(new DirectoryInfo(files.Root));

                Assert.Equal(2, found.Count);
                Assert.Equal("Alarms/Station", found[0].GroupPath);
                Assert.Equal("HmiAlarmTags", found[0].TableName);
                Assert.Equal("", found[1].GroupPath);
                Assert.Equal("Default tag table", found[1].TableName);
            }
        }

        [Fact]
        public void TagTableDirectory_RoundTripsThroughFindTagTableDirectories()
        {
            using (var files = new SimaticMlTestFiles())
            {
                var directory = HmiExchangeFiles.TagTableDirectory(files.Root, "Alarms/Station", "HmiAlarmTags");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "Tags.hmi.yml"), "x");

                var found = Assert.Single(HmiExchangeFiles.FindTagTableDirectories(new DirectoryInfo(files.Root)));

                Assert.Equal(Path.Combine(files.Root, "Alarms", "Station", "HmiAlarmTags"), found.Directory.FullName);
                Assert.Equal("Alarms/Station", found.GroupPath);
                Assert.Equal("HmiAlarmTags", found.TableName);
            }
        }

        [Fact]
        public void FindImportFile_Missing_ReturnsNull()
        {
            using (var files = new SimaticMlTestFiles())
            {
                Assert.Null(HmiExchangeFiles.FindImportFile(files.Root, "GraphicLists"));
            }
        }
    }
}
