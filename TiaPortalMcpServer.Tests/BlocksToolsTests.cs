using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TiaPortalMcpServer.Models;
using TiaPortalMcpServer.Services;
using Xunit;

namespace TiaPortalMcpServer.Tests
{
    [Trait("Category", "Integration")]
    public class BlocksToolsTests : TestBase
    {
        private readonly BlocksTools _blocksTools;

        public BlocksToolsTests()
        {
            _blocksTools = ServiceProvider.GetRequiredService<BlocksTools>();
        }

        [Fact]
        public void GetAssignedProDiagFb_NoProject_ReturnsError()
        {
            // Arrange
            var sessionManager = ServiceProvider.GetRequiredService<TiaPortalSessionManager>();
            sessionManager.CloseCurrentProject();

            // Act
            var result = _blocksTools.blocks_prodiag_assigned_get("TestDevice", "TestMember", "db");

            // Assert
            var response = ParseToolResult<object>(result);
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
        }

        [Fact]
        public void SetAssignedProDiagFb_NoProject_ReturnsError()
        {
            // Arrange
            var sessionManager = ServiceProvider.GetRequiredService<TiaPortalSessionManager>();
            sessionManager.CloseCurrentProject();

            // Act
            var result = _blocksTools.blocks_prodiag_assigned_set("TestDevice", "TestMember", "db", "TestFb");

            // Assert
            var response = ParseToolResult<object>(result);
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
        }

        [Fact]
        public void ListSystemDataTypes_NoProject_ReturnsError()
        {
            // Arrange
            var sessionManager = ServiceProvider.GetRequiredService<TiaPortalSessionManager>();
            sessionManager.CloseCurrentProject();

            // Act
            var result = _blocksTools.blocks_system_types_list("TestDevice");

            // Assert
            var response = ParseToolResult<object>(result);
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
        }

        [Fact]
        public void GenerateSourceFromBlock_NoProject_ReturnsError()
        {
            // Arrange
            var sessionManager = ServiceProvider.GetRequiredService<TiaPortalSessionManager>();
            sessionManager.CloseCurrentProject();

            // Act
            var result = _blocksTools.blocks_source_generate_from_block("TestDevice", "TestBlock");

            // Assert
            var response = ParseToolResult<object>(result);
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
        }

        [Fact]
        public void ExportAsDocuments_NoProject_ReturnsError()
        {
            // Arrange
            var sessionManager = ServiceProvider.GetRequiredService<TiaPortalSessionManager>();
            sessionManager.CloseCurrentProject();

            // Act
            var result = _blocksTools.blocks_export_as_documents("TestDevice", "TestBlock", "C:\\temp");

            // Assert
            var response = ParseToolResult<object>(result);
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
        }

        [Fact]
        public void ExportAllAsDocuments_NoProject_ReturnsError()
        {
            // Arrange
            var sessionManager = ServiceProvider.GetRequiredService<TiaPortalSessionManager>();
            sessionManager.CloseCurrentProject();

            // Act
            var result = _blocksTools.blocks_export_all_as_documents("TestDevice", "C:\\temp");

            // Assert
            var response = ParseToolResult<object>(result);
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
        }

        [Fact]
        public void ImportFromDocuments_NoProject_ReturnsError()
        {
            // Arrange
            var sessionManager = ServiceProvider.GetRequiredService<TiaPortalSessionManager>();
            sessionManager.CloseCurrentProject();

            // Act
            var result = _blocksTools.blocks_import_from_documents("TestDevice", "C:\\temp", "TestBlock");

            // Assert
            var response = ParseToolResult<object>(result);
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
        }

        [Fact]
        public void ImportAllFromDocuments_NoProject_ReturnsError()
        {
            // Arrange
            var sessionManager = ServiceProvider.GetRequiredService<TiaPortalSessionManager>();
            sessionManager.CloseCurrentProject();

            // Act
            var result = _blocksTools.blocks_import_all_from_documents("TestDevice", "C:\\temp");

            // Assert
            var response = ParseToolResult<object>(result);
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.NoProject, response.ErrorCode);
        }

        [Fact]
        public void ImportFromDocuments_InvalidImportOption_ReturnsError()
        {
            // Act
            var result = _blocksTools.blocks_import_from_documents("TestDevice", "C:\\temp", "TestBlock", "", "NotAValidOption");

            // Assert
            var response = ParseToolResult<object>(result);
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
        }

        [Fact]
        public void ImportAllFromDocuments_InvalidImportOption_ReturnsError()
        {
            // Act
            var result = _blocksTools.blocks_import_all_from_documents("TestDevice", "C:\\temp", "", "", "NotAValidOption");

            // Assert
            var response = ParseToolResult<object>(result);
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
        }

        [Fact]
        public void ExportAllAsDocuments_InvalidRegex_ReturnsError()
        {
            // Act
            var result = _blocksTools.blocks_export_all_as_documents("TestDevice", "C:\\temp", "Motor[");

            // Assert
            var response = ParseToolResult<object>(result);
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.InvalidParameter, response.ErrorCode);
        }
    }
}