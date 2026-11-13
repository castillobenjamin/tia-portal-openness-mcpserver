using System.IO;
using Microsoft.Extensions.DependencyInjection;
using TiaPortalMcpServer.Models;
using TiaPortalMcpServer.Services;
using Xunit;

namespace TiaPortalMcpServer.Tests
{
    [Trait("Category", "Integration")]
    public class HmiUnifiedToolsTests : TestBase
    {
        private readonly HmiUnifiedTools _tools;

        public HmiUnifiedToolsTests()
        {
            _tools = ServiceProvider.GetRequiredService<HmiUnifiedTools>();
            ServiceProvider.GetRequiredService<TiaPortalSessionManager>().CloseCurrentProject();
        }

        [Fact]
        public void SoftwareList_NoProject_ReturnsError()
        {
            var response = ParseToolResult<object>(_tools.hmi_unified_software_list());
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
        }

        [Fact]
        public void ScreensList_NoProject_ReturnsError()
        {
            var response = ParseToolResult<object>(_tools.hmi_unified_screens_list("TabletHMI"));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
        }

        [Fact]
        public void ScreensSnapshot_MissingDirectory_ReturnsInvalidParameter()
        {
            var response = ParseToolResult<object>(_tools.hmi_unified_screens_snapshot("TabletHMI", "C:\\does-not-exist\\hmi-snapshot"));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
        }

        [Fact]
        public void ScreensSnapshot_InvalidRegex_ReturnsInvalidParameter()
        {
            using (var files = new SimaticMlTestFiles())
            {
                var response = ParseToolResult<object>(_tools.hmi_unified_screens_snapshot("TabletHMI", files.Root, "Main(["));
                Assert.False(response.Success);
                Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
            }
        }

        [Fact]
        public void ScreensSnapshot_NoProject_ReturnsError()
        {
            using (var files = new SimaticMlTestFiles())
            {
                var response = ParseToolResult<object>(_tools.hmi_unified_screens_snapshot("TabletHMI", files.Root));
                Assert.False(response.Success);
                Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
            }
        }

        [Fact]
        public void AlarmsSnapshot_MissingDirectory_ReturnsInvalidParameter()
        {
            var response = ParseToolResult<object>(_tools.hmi_unified_alarms_snapshot("TabletHMI", "C:\\does-not-exist\\hmi-snapshot"));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
        }

        [Fact]
        public void AlarmsSnapshot_NoProject_ReturnsError()
        {
            using (var files = new SimaticMlTestFiles())
            {
                var response = ParseToolResult<object>(_tools.hmi_unified_alarms_snapshot("TabletHMI", files.Root));
                Assert.False(response.Success);
                Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
            }
        }

        [Fact]
        public void SettingsSnapshot_MissingDirectory_ReturnsInvalidParameter()
        {
            var response = ParseToolResult<object>(_tools.hmi_unified_settings_snapshot("TabletHMI", "C:\\does-not-exist\\hmi-snapshot"));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
        }

        [Fact]
        public void SettingsSnapshot_NoProject_ReturnsError()
        {
            using (var files = new SimaticMlTestFiles())
            {
                var response = ParseToolResult<object>(_tools.hmi_unified_settings_snapshot("TabletHMI", files.Root));
                Assert.False(response.Success);
                Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
            }
        }

        [Fact]
        public void ScreenItemTypesList_ReturnsCommonTypes()
        {
            var response = ParseToolResult<Newtonsoft.Json.Linq.JObject>(_tools.hmi_unified_screen_item_types_list());
            Assert.True(response.Success);
            var types = response.Data["types"].ToObject<string[]>();
            Assert.Contains("Button", types);
            Assert.Contains("IOField", types);
        }

        [Fact]
        public void ScreenItemCreate_UnknownType_ReturnsInvalidParameter()
        {
            var response = ParseToolResult<object>(_tools.hmi_unified_screen_item_create("TabletHMI", "Main", "Spaceship", "Item_1"));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
        }

        [Fact]
        public void ScreenItemSetAttributes_InvalidJson_ReturnsInvalidParameter()
        {
            var response = ParseToolResult<object>(_tools.hmi_unified_screen_item_set_attributes("TabletHMI", "Main", "Button_1", "{\"Left\": "));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
        }

        [Fact]
        public void TagCreate_InternalTagWithoutDataType_ReturnsInvalidParameter()
        {
            var response = ParseToolResult<object>(_tools.hmi_unified_tag_create("TabletHMI", "Counter"));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
        }

        [Fact]
        public void TagsList_NoProject_ReturnsError()
        {
            var response = ParseToolResult<object>(_tools.hmi_unified_tags_list("TabletHMI"));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
        }

        [Fact]
        public void TagsExport_MissingDirectory_ReturnsInvalidParameter()
        {
            var response = ParseToolResult<object>(_tools.hmi_unified_tags_export("TabletHMI", "C:\\does-not-exist\\hmi-tags"));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
        }

        [Fact]
        public void TagsExport_NoProject_ReturnsError()
        {
            using (var files = new SimaticMlTestFiles())
            {
                var response = ParseToolResult<object>(_tools.hmi_unified_tags_export("TabletHMI", files.Root));
                Assert.False(response.Success);
                Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
            }
        }

        [Fact]
        public void TagsImport_EmptyDirectory_ReturnsInvalidParameter()
        {
            using (var files = new SimaticMlTestFiles())
            {
                var response = ParseToolResult<object>(_tools.hmi_unified_tags_import("TabletHMI", files.Root, dryRun: true));
                Assert.False(response.Success);
                Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
            }
        }

        [Fact]
        public void TagsImport_NoProject_ReturnsError()
        {
            using (var files = new SimaticMlTestFiles())
            {
                files.Write(Path.Combine("Alarms", "HmiAlarmTags", "Tags.hmi.yml"), "x");
                var response = ParseToolResult<object>(_tools.hmi_unified_tags_import("TabletHMI", files.Root, dryRun: true));
                Assert.False(response.Success);
                Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
            }
        }

        [Fact]
        public void TagsImport_FilesOnlyInRoot_ReturnsInvalidParameter()
        {
            using (var files = new SimaticMlTestFiles())
            {
                // The layout of the old device-level export: no tag table directories
                files.Write("HmiTags.xlsx", "x");
                var response = ParseToolResult<object>(_tools.hmi_unified_tags_import("TabletHMI", files.Root, dryRun: true));
                Assert.False(response.Success);
                Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
            }
        }

        [Fact]
        public void TagsImport_SingleTagWithSeveralTables_ReturnsInvalidParameter()
        {
            using (var files = new SimaticMlTestFiles())
            {
                files.Write(Path.Combine("TableA", "Tags.hmi.yml"), "x");
                files.Write(Path.Combine("TableB", "Tags.hmi.yml"), "x");
                var response = ParseToolResult<object>(_tools.hmi_unified_tags_import("TabletHMI", files.Root, tagName: "Speed", dryRun: true));
                Assert.False(response.Success);
                Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
            }
        }

        [Fact]
        public void TextListsExport_PathInFileName_ReturnsInvalidParameter()
        {
            var response = ParseToolResult<object>(_tools.hmi_unified_textlists_export("TabletHMI", "C:\\temp", "..\\TextLists.xlsx"));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
        }

        [Fact]
        public void GraphicListsList_NoProject_ReturnsError()
        {
            var response = ParseToolResult<object>(_tools.hmi_unified_graphiclists_list("TabletHMI"));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
        }

        [Fact]
        public void GraphicListsExport_PathInFileName_ReturnsInvalidParameter()
        {
            var response = ParseToolResult<object>(_tools.hmi_unified_graphiclists_export("TabletHMI", "C:\\temp", "..\\GraphicLists.xlsx"));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
        }

        [Fact]
        public void GraphicListsExport_MissingDirectory_ReturnsInvalidParameter()
        {
            var response = ParseToolResult<object>(_tools.hmi_unified_graphiclists_export("TabletHMI", "C:\\does-not-exist\\hmi-lists", "GraphicLists.xlsx"));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
        }

        [Fact]
        public void GraphicListsExport_NoProject_ReturnsError()
        {
            using (var files = new SimaticMlTestFiles())
            {
                var response = ParseToolResult<object>(_tools.hmi_unified_graphiclists_export("TabletHMI", files.Root, "GraphicLists.xlsx"));
                Assert.False(response.Success);
                Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
            }
        }

        [Fact]
        public void GraphicListsImport_MissingFile_ReturnsInvalidParameter()
        {
            using (var files = new SimaticMlTestFiles())
            {
                var response = ParseToolResult<object>(_tools.hmi_unified_graphiclists_import("TabletHMI", files.Root, "GraphicLists.xlsx", dryRun: true));
                Assert.False(response.Success);
                Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
            }
        }

        [Fact]
        public void GraphicListsImport_NoProject_ReturnsError()
        {
            using (var files = new SimaticMlTestFiles())
            {
                files.Write("GraphicLists.xlsx", "x");
                var response = ParseToolResult<object>(_tools.hmi_unified_graphiclists_import("TabletHMI", files.Root, "GraphicLists.xlsx", dryRun: true));
                Assert.False(response.Success);
                Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
            }
        }

        [Fact]
        public void SystemTextListsExport_PathInFileName_ReturnsInvalidParameter()
        {
            var response = ParseToolResult<object>(_tools.hmi_unified_systemtextlists_export("TabletHMI", "C:\\temp", "..\\SystemTextLists.xlsx"));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
        }

        [Fact]
        public void SystemTextListsExport_MissingDirectory_ReturnsInvalidParameter()
        {
            var response = ParseToolResult<object>(_tools.hmi_unified_systemtextlists_export("TabletHMI", "C:\\does-not-exist\\hmi-lists", "SystemTextLists.xlsx"));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
        }

        [Fact]
        public void SystemTextListsImport_MissingFile_ReturnsInvalidParameter()
        {
            using (var files = new SimaticMlTestFiles())
            {
                var response = ParseToolResult<object>(_tools.hmi_unified_systemtextlists_import("TabletHMI", files.Root, "SystemTextLists.xlsx", dryRun: true));
                Assert.False(response.Success);
                Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
            }
        }

        [Fact]
        public void ScriptsList_NoProject_ReturnsError()
        {
            var response = ParseToolResult<object>(_tools.hmi_unified_scripts_list("TabletHMI"));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
        }

        [Fact]
        public void ScriptsExport_MissingDirectory_ReturnsInvalidParameter()
        {
            var response = ParseToolResult<object>(_tools.hmi_unified_scripts_export("TabletHMI", "C:\\does-not-exist\\hmi-scripts"));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
        }

        [Fact]
        public void ScriptsExport_FileNameWithoutModule_ReturnsInvalidParameter()
        {
            using (var files = new SimaticMlTestFiles())
            {
                var response = ParseToolResult<object>(_tools.hmi_unified_scripts_export("TabletHMI", files.Root, fileName: "Module.js"));
                Assert.False(response.Success);
                Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
            }
        }

        [Fact]
        public void ScriptsExport_PathInFileName_ReturnsInvalidParameter()
        {
            using (var files = new SimaticMlTestFiles())
            {
                var response = ParseToolResult<object>(_tools.hmi_unified_scripts_export("TabletHMI", files.Root, "Module_1", "..\\Module.js"));
                Assert.False(response.Success);
                Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
            }
        }

        [Fact]
        public void ScriptsExport_NoProject_ReturnsError()
        {
            using (var files = new SimaticMlTestFiles())
            {
                var response = ParseToolResult<object>(_tools.hmi_unified_scripts_export("TabletHMI", files.Root));
                Assert.False(response.Success);
                Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
            }
        }

        [Fact]
        public void ScriptsImport_EmptyDirectory_ReturnsInvalidParameter()
        {
            using (var files = new SimaticMlTestFiles())
            {
                var response = ParseToolResult<object>(_tools.hmi_unified_scripts_import("TabletHMI", files.Root, dryRun: true));
                Assert.False(response.Success);
                Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
            }
        }

        [Fact]
        public void ScriptsImport_MissingFile_ReturnsInvalidParameter()
        {
            using (var files = new SimaticMlTestFiles())
            {
                files.Write("Other.js", "x");
                var response = ParseToolResult<object>(_tools.hmi_unified_scripts_import("TabletHMI", files.Root, "Module_1.js", dryRun: true));
                Assert.False(response.Success);
                Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
            }
        }

        [Fact]
        public void ScriptsImport_NoProject_ReturnsError()
        {
            using (var files = new SimaticMlTestFiles())
            {
                files.Write("Module_1.js", "x");
                var response = ParseToolResult<object>(_tools.hmi_unified_scripts_import("TabletHMI", files.Root, dryRun: true));
                Assert.False(response.Success);
                Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
            }
        }

        [Fact]
        public void SystemTextListsImport_NoProject_ReturnsError()
        {
            using (var files = new SimaticMlTestFiles())
            {
                files.Write("SystemTextLists.xlsx", "x");
                var response = ParseToolResult<object>(_tools.hmi_unified_systemtextlists_import("TabletHMI", files.Root, "SystemTextLists.xlsx", dryRun: true));
                Assert.False(response.Success);
                Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
            }
        }
    }
}
