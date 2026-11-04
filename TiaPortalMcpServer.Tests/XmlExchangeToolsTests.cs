using System.IO;
using Microsoft.Extensions.DependencyInjection;
using TiaPortalMcpServer.Models;
using TiaPortalMcpServer.Services;
using Xunit;

namespace TiaPortalMcpServer.Tests
{
    [Trait("Category", "Integration")]
    public class XmlExchangeToolsTests : TestBase
    {
        private readonly XmlExchangeTools _tools;

        public XmlExchangeToolsTests()
        {
            _tools = ServiceProvider.GetRequiredService<XmlExchangeTools>();
            ServiceProvider.GetRequiredService<TiaPortalSessionManager>().CloseCurrentProject();
        }

        [Fact]
        public void BlocksExportAsXml_NoProject_ReturnsError()
        {
            var response = ParseToolResult<object>(_tools.blocks_export_as_xml("PLC_1", "Main", Path.GetTempPath()));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
        }

        [Fact]
        public void BlocksExportAllAsXml_InvalidRegex_ReturnsInvalidParameter()
        {
            var response = ParseToolResult<object>(_tools.blocks_export_all_as_xml("PLC_1", Path.GetTempPath(), "(["));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
        }

        [Fact]
        public void BlocksImportFromXml_InvalidImportOption_ReturnsInvalidParameter()
        {
            var response = ParseToolResult<object>(_tools.blocks_import_from_xml("PLC_1", "Main.xml", importOption: "Overwrite"));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
        }

        [Fact]
        public void BlocksImportAllFromXml_NoProject_ReturnsError()
        {
            var response = ParseToolResult<object>(_tools.blocks_import_all_from_xml("PLC_1", Path.GetTempPath()));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
        }

        [Fact]
        public void TypesExportAsXml_NoProject_ReturnsError()
        {
            var response = ParseToolResult<object>(_tools.types_export_as_xml("PLC_1", "Axis_UDT", Path.GetTempPath()));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
        }

        [Fact]
        public void TypesImportFromXml_InvalidSwImportOption_ReturnsInvalidParameter()
        {
            var response = ParseToolResult<object>(_tools.types_import_from_xml("PLC_1", "Axis_UDT.xml", swImportOption: "IgnoreEverything"));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
        }

        [Fact]
        public void TypesImportAllFromXml_NoProject_ReturnsError()
        {
            var response = ParseToolResult<object>(_tools.types_import_all_from_xml("PLC_1", Path.GetTempPath()));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
        }
    }
}
