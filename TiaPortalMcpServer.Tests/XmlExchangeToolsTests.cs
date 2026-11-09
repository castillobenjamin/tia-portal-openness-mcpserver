using System;
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

        [Fact]
        public void TagTableExport_NoProject_ReturnsError()
        {
            var response = ParseToolResult<object>(_tools.tags_tagtable_export("PLC_1", "Default tag table", Path.GetTempPath()));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
        }

        [Fact]
        public void TagTableExport_EmptyName_ReturnsInvalidTagName()
        {
            var response = ParseToolResult<object>(_tools.tags_tagtable_export("PLC_1", "", Path.GetTempPath()));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.InvalidTagName, response.ErrorCode);
        }

        [Fact]
        public void TagTableExport_MissingDirectory_ReturnsInvalidParameter()
        {
            var missing = Path.Combine(Path.GetTempPath(), "TagTableExport_" + Guid.NewGuid().ToString("N"));
            var response = ParseToolResult<object>(_tools.tags_tagtable_export("PLC_1", "Default tag table", missing));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
        }

        [Fact]
        public void TagTableExportAll_InvalidRegex_ReturnsInvalidParameter()
        {
            var response = ParseToolResult<object>(_tools.tags_tagtable_export_all("PLC_1", Path.GetTempPath(), "(["));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
        }

        [Fact]
        public void TagTableExportAll_NoProject_ReturnsError()
        {
            var response = ParseToolResult<object>(_tools.tags_tagtable_export_all("PLC_1", Path.GetTempPath()));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
        }

        [Fact]
        public void TagTableImport_InvalidImportOption_ReturnsInvalidParameter()
        {
            var response = ParseToolResult<object>(_tools.tags_tagtable_import("PLC_1", "Default tag table.xml", importOption: "Overwrite"));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
        }

        [Fact]
        public void TagTableImport_NoProject_ReturnsError()
        {
            var response = ParseToolResult<object>(_tools.tags_tagtable_import("PLC_1", "Default tag table.xml"));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
        }

        [Fact]
        public void TagTableImportAll_MissingDirectory_ReturnsInvalidParameter()
        {
            var missing = Path.Combine(Path.GetTempPath(), "TagTableImport_" + Guid.NewGuid().ToString("N"));
            var response = ParseToolResult<object>(_tools.tags_tagtable_import_all("PLC_1", missing));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
        }

        [Fact]
        public void TagTableImportAll_NoProject_ReturnsError()
        {
            var response = ParseToolResult<object>(_tools.tags_tagtable_import_all("PLC_1", Path.GetTempPath()));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
        }

        /// <summary>
        /// Exports a tag table from a real project twice and checks the file is written, is not empty,
        /// is identical across exports and passes a dry-run import. Opt-in: set TIA_TEST_PROJECT (path of
        /// an .ap21 project) and TIA_TEST_DEVICE (PLC device name); TIA_TEST_TAGTABLE defaults to
        /// "Default tag table". Without TIA_TEST_PROJECT the test returns without checking anything.
        /// </summary>
        [Fact]
        public void TagTableExport_RealProject_WritesStableNonEmptyFile()
        {
            var projectPath = Environment.GetEnvironmentVariable("TIA_TEST_PROJECT");
            if (string.IsNullOrEmpty(projectPath))
            {
                return;
            }
            var deviceName = Environment.GetEnvironmentVariable("TIA_TEST_DEVICE") ?? "PLC_1";
            var tableName = Environment.GetEnvironmentVariable("TIA_TEST_TAGTABLE");
            if (string.IsNullOrEmpty(tableName))
            {
                tableName = "Default tag table";
            }

            var sessionManager = ServiceProvider.GetRequiredService<TiaPortalSessionManager>();
            sessionManager.OpenProject(projectPath);
            var directory = Path.Combine(Path.GetTempPath(), "TagTableExport_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var first = ParseToolResult<DocumentOperationResult>(_tools.tags_tagtable_export(deviceName, tableName, directory));
                Assert.True(first.Success, first.Error);
                var file = Assert.Single(first.Data.Files);
                Assert.True(new FileInfo(file).Length > 0);
                var firstBytes = File.ReadAllBytes(file);

                var second = ParseToolResult<DocumentOperationResult>(_tools.tags_tagtable_export(deviceName, tableName, directory, overwrite: true));
                Assert.True(second.Success, second.Error);
                Assert.Equal(firstBytes, File.ReadAllBytes(file));

                var dryRun = ParseToolResult<DocumentOperationResult>(_tools.tags_tagtable_import(deviceName, file, dryRun: true));
                Assert.True(dryRun.Success, dryRun.Error);
                Assert.Equal(DocumentOperationResult.StateValidated, dryRun.Data.State);
            }
            finally
            {
                sessionManager.CloseCurrentProject();
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
