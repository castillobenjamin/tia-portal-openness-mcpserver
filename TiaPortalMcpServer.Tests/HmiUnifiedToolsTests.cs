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
                files.Write("HmiTags.xlsx", "x");
                var response = ParseToolResult<object>(_tools.hmi_unified_tags_import("TabletHMI", files.Root, dryRun: true));
                Assert.False(response.Success);
                Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
            }
        }

        [Fact]
        public void TextListsExport_PathInFileName_ReturnsInvalidParameter()
        {
            var response = ParseToolResult<object>(_tools.hmi_unified_textlists_export("TabletHMI", "C:\\temp", "..\\TextLists.xlsx"));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
        }
    }
}
