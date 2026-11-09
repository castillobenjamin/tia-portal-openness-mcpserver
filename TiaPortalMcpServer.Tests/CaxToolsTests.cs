using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using TiaPortalMcpServer.Models;
using TiaPortalMcpServer.Services;
using Xunit;

namespace TiaPortalMcpServer.Tests
{
    [Trait("Category", "Integration")]
    public class CaxToolsTests : TestBase
    {
        private readonly CaxTools _tools;

        public CaxToolsTests()
        {
            _tools = ServiceProvider.GetRequiredService<CaxTools>();
            ServiceProvider.GetRequiredService<TiaPortalSessionManager>().CloseCurrentProject();
        }

        [Fact]
        public void CaxExport_MissingDirectory_ReturnsInvalidParameter()
        {
            var missing = Path.Combine(Path.GetTempPath(), "CaxExport_" + Guid.NewGuid().ToString("N"));
            var response = ParseToolResult<object>(_tools.cax_export(missing, "Hardware.aml"));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
        }

        [Fact]
        public void CaxExport_FileNameWithDirectory_ReturnsInvalidParameter()
        {
            var response = ParseToolResult<object>(_tools.cax_export(Path.GetTempPath(), Path.Combine("sub", "Hardware.aml")));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
        }

        [Fact]
        public void CaxExport_NotAmlFile_ReturnsInvalidParameter()
        {
            var response = ParseToolResult<object>(_tools.cax_export(Path.GetTempPath(), "Hardware.xml"));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
        }

        [Fact]
        public void CaxExport_ExistingFileWithoutOverwrite_ReturnsInvalidParameter()
        {
            var directory = Path.Combine(Path.GetTempPath(), "CaxExport_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                File.WriteAllText(Path.Combine(directory, "Hardware.aml"), "<CAEXFile />");
                var response = ParseToolResult<object>(_tools.cax_export(directory, "Hardware.aml"));
                Assert.False(response.Success);
                Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void CaxExport_NoProject_ReturnsError()
        {
            var response = ParseToolResult<object>(_tools.cax_export(Path.GetTempPath(), "Hardware_" + Guid.NewGuid().ToString("N") + ".aml"));
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
        }

        /// <summary>
        /// Exports a real project twice with normalize=true and checks both exports are identical and not empty.
        /// Opt-in: set TIA_TEST_PROJECT (path of an .ap21 project); TIA_TEST_DEVICE limits the export to one device.
        /// Without TIA_TEST_PROJECT the test returns without checking anything.
        /// </summary>
        [Fact]
        public void CaxExport_RealProject_NormalizedExportsAreIdentical()
        {
            var projectPath = Environment.GetEnvironmentVariable("TIA_TEST_PROJECT");
            if (string.IsNullOrEmpty(projectPath))
            {
                return;
            }
            var deviceName = Environment.GetEnvironmentVariable("TIA_TEST_DEVICE") ?? string.Empty;

            var sessionManager = ServiceProvider.GetRequiredService<TiaPortalSessionManager>();
            sessionManager.OpenProject(projectPath);
            var directory = Path.Combine(Path.GetTempPath(), "CaxExport_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var first = ParseToolResult<CaxExportResult>(_tools.cax_export(directory, "First.aml", deviceName, normalize: true));
                Assert.True(first.Success, first.Error);
                Assert.True(new FileInfo(first.Data.File).Length > 0);
                Assert.True(File.Exists(first.Data.LogFile));

                var second = ParseToolResult<CaxExportResult>(_tools.cax_export(directory, "Second.aml", deviceName, normalize: true));
                Assert.True(second.Success, second.Error);

                // The files name themselves (CAEXFile FileName, log messages), so compare after aligning the names
                Assert.Equal(ReadAligned(first.Data.File, "First"), ReadAligned(second.Data.File, "Second"));
                Assert.Equal(ReadAligned(first.Data.LogFile, "First"), ReadAligned(second.Data.LogFile, "Second"));
            }
            finally
            {
                sessionManager.CloseCurrentProject();
                Directory.Delete(directory, recursive: true);
            }
        }

        private static string ReadAligned(string path, string baseName)
        {
            return File.ReadAllText(path).Replace(baseName + ".aml", "Export.aml").Replace(baseName + ".log", "Export.log");
        }
    }
}
