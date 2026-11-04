using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Blocks.Interface;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Types;
using TiaPortalMcpServer.Models;
using TiaPortalMcpServer.Services;

namespace TiaPortalMcpServer
{
    [McpServerToolType]
    public class BlocksTools
    {
        private readonly ILogger<BlocksTools> _logger;
        private readonly TiaPortalSessionManager _sessionManager;
        private readonly BlocksAdapter _blocksAdapter;

        public BlocksTools(
            ILogger<BlocksTools> logger,
            TiaPortalSessionManager sessionManager,
            BlocksAdapter blocksAdapter)
        {
            _logger = logger;
            _sessionManager = sessionManager;
            _blocksAdapter = blocksAdapter;
        }

        #region ProDiag Tools

        [McpServerTool(ReadOnly = true, Idempotent = true), Description("Retrieve the assigned ProDiag Function Block for a specific Data Block member or tag table member. Use this to inspect ProDiag FB assignments for diagnostic monitoring. Returns the ProDiag FB name if assigned, or null. Prerequisites: Project must be open, device must exist, member path must be valid.")]
        public CallToolResult blocks_prodiag_assigned_get(
            [Description("Device name")] string deviceName,
            [Description("Path to member (e.g., 'DB_Name.SimpleUdtInstance' or 'ArrayUdtMember.ArrayUdtMember[0]')")] string memberPath,
            [Description("Member type: 'db' for DB member, 'tag' for tag table member")] string memberType,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("blocks_prodiag_assigned_get called with deviceName='{DeviceName}', memberPath='{MemberPath}', memberType='{MemberType}'", deviceName, memberPath, memberType);

            try
            {
                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<string?>.CreateError(ErrorCodes.NoProject, "No project is currently open"));
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<string?>.CreateError(scopeErrorCode, scopeError));
                }

                if (!TryResolveMember(plcScope, memberPath, memberType, out var member, out var errorCode, out var errorMessage))
                {
                    return McpToolResults.From(ToolResponse<string?>.CreateError(errorCode, errorMessage));
                }

                if (member == null)
                {
                    return McpToolResults.From(ToolResponse<string?>.CreateError(ErrorCodes.InvalidParameter, $"Member '{memberPath}' not found"));
                }

                var result = _blocksAdapter.GetAssignedProDiagFb(member);
                return McpToolResults.From(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in blocks_prodiag_assigned_get");
                return McpToolResults.From(ToolResponse<string?>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Assign a ProDiag Function Block to a specific Data Block member or tag table member for diagnostic monitoring integration. Returns success confirmation. Prerequisites: Project must be open, device must exist, ProDiag FB must exist, member path must be valid. Use dryRun=true to validate assignment without making changes.")]
        public CallToolResult blocks_prodiag_assigned_set(
            [Description("Device name")] string deviceName,
            [Description("Path to member (e.g., 'DB_Name.SimpleUdtInstance' or 'ArrayUdtMember.ArrayUdtMember[0]')")] string memberPath,
            [Description("Member type: 'db' for DB member, 'tag' for tag table member")] string memberType,
            [Description("ProDiag FB name to assign")] string proDiagFbName,
            [Description("If true, only validate without making changes")] bool dryRun = false,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("blocks_prodiag_assigned_set called with deviceName='{DeviceName}', memberPath='{MemberPath}', memberType='{MemberType}', proDiagFbName='{ProDiagFbName}', dryRun={DryRun}", deviceName, memberPath, memberType, proDiagFbName, dryRun);

            try
            {
                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<bool>.CreateError(ErrorCodes.NoProject, "No project is currently open"));
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<bool>.CreateError(scopeErrorCode, scopeError));
                }

                if (!TryResolveMember(plcScope, memberPath, memberType, out var member, out var errorCode, out var errorMessage))
                {
                    return McpToolResults.From(ToolResponse<bool>.CreateError(errorCode, errorMessage));
                }

                if (member == null)
                {
                    return McpToolResults.From(ToolResponse<bool>.CreateError(ErrorCodes.InvalidParameter, $"Member '{memberPath}' not found"));
                }

                var result = _blocksAdapter.SetAssignedProDiagFb(member, proDiagFbName, dryRun);

                try
                {
                    _sessionManager.SaveCurrentProject();
                    _logger.LogInformation("Project saved after ProDiag assignment");
                }
                catch (Exception saveEx)
                {
                    _logger.LogWarning(saveEx, "Failed to save project after ProDiag assignment");
                }

                return McpToolResults.From(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in blocks_prodiag_assigned_set");
                return McpToolResults.From(ToolResponse<bool>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        [McpServerTool(ReadOnly = true, Idempotent = true), Description("Retrieve ProDiag attributes and configuration for a specific ProDiag Function Block. Returns diagnostic properties including monitoring parameters and alarm settings. Prerequisites: Project must be open, device must exist, ProDiag FB must exist. Use this to inspect ProDiag FB configuration before modification.")]
        public CallToolResult blocks_prodiag_attributes_get(
            [Description("Device name")] string deviceName,
            [Description("ProDiag FB name")] string fbName,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("blocks_prodiag_attributes_get called with deviceName='{DeviceName}', fbName='{FbName}'", deviceName, fbName);

            try
            {
                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<ProDiagAttributes>.CreateError(ErrorCodes.NoProject, "No project is currently open"));
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<ProDiagAttributes>.CreateError(scopeErrorCode, scopeError));
                }

                var fb = plcScope.BlockGroup.Blocks.Find(fbName) as FB;
                if (fb == null)
                {
                    return McpToolResults.From(ToolResponse<ProDiagAttributes>.CreateError(ErrorCodes.BlockNotFound, $"FB '{fbName}' not found"));
                }

                var result = _blocksAdapter.GetProDiagAttributes(fb);
                return McpToolResults.From(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in blocks_prodiag_attributes_get");
                return McpToolResults.From(ToolResponse<ProDiagAttributes>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Export ProDiag alarm messages from a ProDiag Function Block to CSV format for documentation or external processing. Returns the path to the exported CSV file. Prerequisites: Project must be open, device must exist, ProDiag FB must exist, directory must exist. Use this for alarm documentation and reporting.")]
        public CallToolResult blocks_prodiag_export_csv(
            [Description("Device name")] string deviceName,
            [Description("ProDiag FB name")] string fbName,
            [Description("Directory path to export CSV files")] string directoryPath,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("blocks_prodiag_export_csv called with deviceName='{DeviceName}', fbName='{FbName}', directoryPath='{DirectoryPath}'", deviceName, fbName, directoryPath);

            try
            {
                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<string>.CreateError(ErrorCodes.NoProject, "No project is currently open"));
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<string>.CreateError(scopeErrorCode, scopeError));
                }

                var fb = plcScope.BlockGroup.Blocks.Find(fbName) as FB;
                if (fb == null)
                {
                    return McpToolResults.From(ToolResponse<string>.CreateError(ErrorCodes.BlockNotFound, $"FB '{fbName}' not found"));
                }

                var directory = new DirectoryInfo(directoryPath);
                if (!directory.Exists)
                {
                    return McpToolResults.From(ToolResponse<string>.CreateError(ErrorCodes.InvalidParameter, $"Directory '{directoryPath}' does not exist"));
                }

                var result = _blocksAdapter.ExportProDiagCsv(fb, directory);
                return McpToolResults.From(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in blocks_prodiag_export_csv");
                return McpToolResults.From(ToolResponse<string>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        #endregion

        #region External Source Tools

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Import an external source file (.awl, .scl, .db, .udt) into the PLC project as an external source object. The source can later be compiled into blocks. Returns the external source object. Prerequisites: Project must be open, device must have PLC software, source file must exist. Supported formats: STL/AWL, SCL, DB, UDT.")]
        public CallToolResult blocks_external_source_add(
            [Description("Device name")] string deviceName,
            [Description("Name for the external source")] string name,
            [Description("Path to the source file (.awl, .scl, .db, .udt)")] string filePath,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("blocks_external_source_add called with deviceName='{DeviceName}', name='{Name}', filePath='{FilePath}'", deviceName, name, filePath);

            try
            {
                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<ExternalSourceInfo>.CreateError(ErrorCodes.NoProject, "No project is currently open"));
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<ExternalSourceInfo>.CreateError(scopeErrorCode, scopeError));
                }

                var result = _blocksAdapter.AddExternalSource(plcScope, name, filePath);

                try
                {
                    _sessionManager.SaveCurrentProject();
                    _logger.LogInformation("Project saved after external source addition");
                }
                catch (Exception saveEx)
                {
                    _logger.LogWarning(saveEx, "Failed to save project after external source addition");
                }

                return McpToolResults.From(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in blocks_external_source_add");
                return McpToolResults.From(ToolResponse<ExternalSourceInfo>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        [McpServerTool(ReadOnly = true, Idempotent = true), Description("List all external source files in the PLC project. Returns a list of external source objects with their names and paths. Prerequisites: Project must be open, device must have PLC software. Use to check what external sources are available before generating blocks or deleting.")]
        public CallToolResult blocks_external_source_list(
            [Description("Device name")] string deviceName,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("blocks_external_source_list called with deviceName='{DeviceName}'", deviceName);

            try
            {
                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<IList<ExternalSourceInfo>>.CreateError(ErrorCodes.NoProject, "No project is currently open"));
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<IList<ExternalSourceInfo>>.CreateError(scopeErrorCode, scopeError));
                }

                var results = new List<ExternalSourceInfo>();
                foreach (var externalSource in plcScope.ExternalSourceGroup.ExternalSources)
                {
                    results.Add(new ExternalSourceInfo
                    {
                        Name = externalSource.Name
                    });
                }

                return McpToolResults.From(ToolResponse<IList<ExternalSourceInfo>>.CreateSuccess(results));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in blocks_external_source_list");
                return McpToolResults.From(ToolResponse<IList<ExternalSourceInfo>>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Generate source code files from existing PLC blocks or user-defined types. Exports block logic to .awl, .scl, or appropriate format. Returns the generated file path. Prerequisites: Project must be open, blocks/types must exist. Options: 'WithDependencies' includes called blocks, 'None' only specified blocks. Use for version control, backup, or code review.")]
        public CallToolResult blocks_source_generate(
            [Description("Device name")] string deviceName,
            [Description("Block or type names to generate source from")] string[] sourceNames,
            [Description("Target file path")] string targetFilePath,
            [Description("Generate options: 'None' or 'WithDependencies'")] string options = "WithDependencies",
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("blocks_source_generate called with deviceName='{DeviceName}', sourceNames=[{SourceNames}], targetFilePath='{TargetFilePath}', options='{Options}'", deviceName, string.Join(",", sourceNames), targetFilePath, options);

            try
            {
                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<string>.CreateError(ErrorCodes.NoProject, "No project is currently open"));
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<string>.CreateError(scopeErrorCode, scopeError));
                }

                var sources = new List<IGenerateSource>();
                foreach (var name in sourceNames)
                {
                    var block = plcScope.BlockGroup.Blocks.Find(name);
                    if (block != null)
                    {
                        sources.Add(block);
                        continue;
                    }

                    var type = plcScope.TypeGroup.Types.Find(name);
                    if (type != null)
                    {
                        sources.Add(type);
                        continue;
                    }

                    return McpToolResults.From(ToolResponse<string>.CreateError(ErrorCodes.InvalidParameter, $"Source '{name}' not found as block or type"));
                }

                var targetFile = new FileInfo(targetFilePath);
                var generateOptions = options == "WithDependencies" ? GenerateOptions.WithDependencies : GenerateOptions.None;

                var result = _blocksAdapter.GenerateSource(plcScope, sources, targetFile, generateOptions);
                return McpToolResults.From(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in blocks_source_generate");
                return McpToolResults.From(ToolResponse<string>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Generate source code text representation from a specific PLC block and return as string. Does not create a file. Returns the source code text in the block's programming language. Prerequisites: Project must be open, block must exist. Options: 'WithDependencies' includes dependencies, 'None' only the block. Use for inline code inspection without file operations.")]
        public CallToolResult blocks_source_generate_from_block(
            [Description("Device name")] string deviceName,
            [Description("Block name")] string blockName,
            [Description("Generate options: 'None' or 'WithDependencies'")] string options = "WithDependencies",
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("blocks_source_generate_from_block called with deviceName='{DeviceName}', blockName='{BlockName}', options='{Options}'", deviceName, blockName, options);

            try
            {
                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<string>.CreateError(ErrorCodes.NoProject, "No project is currently open"));
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<string>.CreateError(scopeErrorCode, scopeError));
                }

                var block = plcScope.BlockGroup.Blocks.Find(blockName);
                if (block == null)
                {
                    return McpToolResults.From(ToolResponse<string>.CreateError(ErrorCodes.BlockNotFound, $"Block '{blockName}' not found"));
                }

                var generateOptions = options == "WithDependencies" ? GenerateOptions.WithDependencies : GenerateOptions.None;
                var extension = GetSourceExtension(block.ProgrammingLanguage);
                var sources = new List<IGenerateSource> { block };

                var result = _blocksAdapter.GenerateSourceText(plcScope, sources, extension, generateOptions);
                return McpToolResults.From(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in blocks_source_generate_from_block");
                return McpToolResults.From(ToolResponse<string>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Compile all external sources in the PLC project into executable blocks. Processes every external source file and generates corresponding block objects. Returns list of generated blocks. Prerequisites: Project must be open, device must have PLC software, external sources must be syntactically valid. Use after adding multiple external sources for batch compilation.")]
        public CallToolResult blocks_external_source_generate_all(
            [Description("Device name")] string deviceName,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("blocks_external_source_generate_all called with deviceName='{DeviceName}'", deviceName);

            try
            {
                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<IList<BlockInfo>>.CreateError(ErrorCodes.NoProject, "No project is currently open"));
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<IList<BlockInfo>>.CreateError(scopeErrorCode, scopeError));
                }

                var results = new List<BlockInfo>();
                foreach (var externalSource in plcScope.ExternalSourceGroup.ExternalSources)
                {
                    var result = _blocksAdapter.GenerateBlocksFromSource(externalSource, GenerateBlockOption.None);
                    if (!result.Success)
                    {
                        return McpToolResults.From(ToolResponse<IList<BlockInfo>>.CreateError(result.ErrorCode ?? ErrorCodes.TiaError, result.Error ?? "Failed to generate blocks", result.Details));
                    }
                    results.AddRange(result.Data);
                }

                return McpToolResults.From(ToolResponse<IList<BlockInfo>>.CreateSuccess(results));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in blocks_external_source_generate_all");
                return McpToolResults.From(ToolResponse<IList<BlockInfo>>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Compile a specific external source file into executable PLC blocks with generation options. Returns list of generated blocks. Prerequisites: Project must be open, external source must exist. Options: 'None' fails on error, 'KeepOnError' preserves partially generated blocks. Use for controlled, individual external source compilation.")]
        public CallToolResult blocks_external_source_generate_with_options(
            [Description("Device name")] string deviceName,
            [Description("External source name")] string sourceName,
            [Description("Generate options: 'None' or 'KeepOnError'")] string options = "None",
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("blocks_external_source_generate_with_options called with deviceName='{DeviceName}', sourceName='{SourceName}', options='{Options}'", deviceName, sourceName, options);

            try
            {
                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<IList<BlockInfo>>.CreateError(ErrorCodes.NoProject, "No project is currently open"));
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<IList<BlockInfo>>.CreateError(scopeErrorCode, scopeError));
                }

                var externalSource = plcScope.ExternalSourceGroup.ExternalSources.Find(sourceName);
                if (externalSource == null)
                {
                    return McpToolResults.From(ToolResponse<IList<BlockInfo>>.CreateError(ErrorCodes.InvalidParameter, $"External source '{sourceName}' not found"));
                }

                var generateOptions = options == "KeepOnError" ? GenerateBlockOption.KeepOnError : GenerateBlockOption.None;
                var result = _blocksAdapter.GenerateBlocksFromSource(externalSource, generateOptions);
                return McpToolResults.From(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in blocks_external_source_generate_with_options");
                return McpToolResults.From(ToolResponse<IList<BlockInfo>>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Remove an external source file from the PLC project. Returns success confirmation. Prerequisites: Project must be open, external source must exist. Use dryRun=true to validate deletion without removing. Warning: Blocks generated from this source remain but lose source link. Use software_add_block to recreate if needed.")]
        public CallToolResult blocks_external_source_delete(
            [Description("Device name")] string deviceName,
            [Description("External source name")] string sourceName,
            [Description("If true, only validate without deleting")] bool dryRun = false,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("blocks_external_source_delete called with deviceName='{DeviceName}', sourceName='{SourceName}', dryRun={DryRun}", deviceName, sourceName, dryRun);

            try
            {
                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<bool>.CreateError(ErrorCodes.NoProject, "No project is currently open"));
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<bool>.CreateError(scopeErrorCode, scopeError));
                }

                var externalSource = plcScope.ExternalSourceGroup.ExternalSources.Find(sourceName);
                if (externalSource == null)
                {
                    return McpToolResults.From(ToolResponse<bool>.CreateError(ErrorCodes.InvalidParameter, $"External source '{sourceName}' not found"));
                }

                var result = _blocksAdapter.DeleteExternalSource(externalSource, dryRun);
                return McpToolResults.From(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in blocks_external_source_delete");
                return McpToolResults.From(ToolResponse<bool>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        #endregion

        #region Block Management Tools

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Delete a User-Defined Type (UDT) from the PLC project. Returns success confirmation. Prerequisites: Project must be open, UDT must exist. Use dryRun=true to validate deletion without removing. Warning: Deletion fails if UDT is referenced by other blocks or tags. Check dependencies first with blocks_fingerprints_get.")]
        public CallToolResult blocks_udt_delete(
            [Description("Device name")] string deviceName,
            [Description("UDT name")] string udtName,
            [Description("If true, only validate without deleting")] bool dryRun = false,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("blocks_udt_delete called with deviceName='{DeviceName}', udtName='{UdtName}', dryRun={DryRun}", deviceName, udtName, dryRun);

            try
            {
                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<bool>.CreateError(ErrorCodes.NoProject, "No project is currently open"));
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<bool>.CreateError(scopeErrorCode, scopeError));
                }

                var udt = plcScope.TypeGroup.Types.Find(udtName);
                if (udt == null)
                {
                    return McpToolResults.From(ToolResponse<bool>.CreateError(ErrorCodes.InvalidParameter, $"UDT '{udtName}' not found"));
                }

                var result = _blocksAdapter.DeleteUdt(udt, dryRun);

                try
                {
                    _sessionManager.SaveCurrentProject();
                    _logger.LogInformation("Project saved after UDT deletion");
                }
                catch (Exception saveEx)
                {
                    _logger.LogWarning(saveEx, "Failed to save project after UDT deletion");
                }

                return McpToolResults.From(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in blocks_udt_delete");
                return McpToolResults.From(ToolResponse<bool>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        [McpServerTool(ReadOnly = true, Idempotent = true), Description("Enumerate all system-defined data types available in the PLC software including elementary types, complex types, and hardware types. Returns hierarchical list of system type groups. Prerequisites: Project must be open, device must have PLC software. Use this to discover available data types for tag/block creation.")]
        public CallToolResult blocks_system_types_list(
            [Description("Device name")] string deviceName,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("blocks_system_types_list called with deviceName='{DeviceName}'", deviceName);

            try
            {
                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<IList<PlcSystemTypeGroup>>.CreateError(ErrorCodes.NoProject, "No project is currently open"));
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<IList<PlcSystemTypeGroup>>.CreateError(scopeErrorCode, scopeError));
                }

                var result = _blocksAdapter.GetSystemTypes(plcScope.Software);
                return McpToolResults.From(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in blocks_system_types_list");
                return McpToolResults.From(ToolResponse<IList<PlcSystemTypeGroup>>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        [McpServerTool(ReadOnly = true, Idempotent = true), Description("Enumerate all user-defined types (UDTs) available in the PLC software. Returns list of UDT names and namespaces. Prerequisites: Project must be open, device must have PLC software. Use this to discover available UDTs for data block creation or tag definitions.")]
        public CallToolResult blocks_user_types_list(
            [Description("Device name")] string deviceName,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("blocks_user_types_list called with deviceName='{DeviceName}'", deviceName);

            try
            {
                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<IList<PlcTypeInfo>>.CreateError(ErrorCodes.NoProject, "No project is currently open"));
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<IList<PlcTypeInfo>>.CreateError(scopeErrorCode, scopeError));
                }

                var result = _blocksAdapter.GetUserTypes(plcScope);
                return McpToolResults.From(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in blocks_user_types_list");
                return McpToolResults.From(ToolResponse<IList<PlcTypeInfo>>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Open a specific PLC block in the TIA Portal graphical editor for manual editing. Returns success confirmation. Prerequisites: Project must be open, block must exist, TIA Portal UI must be accessible. Use this when programmatic modifications are insufficient and visual editing is required. The editor opens in the active TIA Portal instance.")]
        public CallToolResult blocks_editor_open_block(
            [Description("Device name")] string deviceName,
            [Description("Block name")] string blockName,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("blocks_editor_open_block called with deviceName='{DeviceName}', blockName='{BlockName}'", deviceName, blockName);

            try
            {
                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<bool>.CreateError(ErrorCodes.NoProject, "No project is currently open"));
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<bool>.CreateError(scopeErrorCode, scopeError));
                }

                var block = plcScope.BlockGroup.Blocks.Find(blockName);
                if (block == null)
                {
                    return McpToolResults.From(ToolResponse<bool>.CreateError(ErrorCodes.BlockNotFound, $"Block '{blockName}' not found"));
                }

                var result = _blocksAdapter.OpenBlockInEditor(block);
                return McpToolResults.From(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in blocks_editor_open_block");
                return McpToolResults.From(ToolResponse<bool>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Open a User-Defined Type (UDT) in the TIA Portal graphical editor for manual editing. Returns success confirmation. Prerequisites: Project must be open, UDT must exist, TIA Portal UI must be accessible. Use this for visual UDT structure editing when programmatic type modifications are not available.")]
        public CallToolResult blocks_editor_open_type(
            [Description("Device name")] string deviceName,
            [Description("Type name")] string typeName,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("blocks_editor_open_type called with deviceName='{DeviceName}', typeName='{TypeName}'", deviceName, typeName);

            try
            {
                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<bool>.CreateError(ErrorCodes.NoProject, "No project is currently open"));
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<bool>.CreateError(scopeErrorCode, scopeError));
                }

                var type = plcScope.TypeGroup.Types.Find(typeName);
                if (type == null)
                {
                    return McpToolResults.From(ToolResponse<bool>.CreateError(ErrorCodes.InvalidParameter, $"Type '{typeName}' not found"));
                }

                var result = _blocksAdapter.OpenTypeInEditor(type);
                return McpToolResults.From(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in blocks_editor_open_type");
                return McpToolResults.From(ToolResponse<bool>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        [McpServerTool(ReadOnly = true, Idempotent = true), Description("Retrieve interface fingerprints for a block or User-Defined Type to track interface version changes. Returns list of fingerprints representing interface structure. Prerequisites: Project must be open, block/UDT must exist. Use this to detect interface changes, manage version compatibility, or validate block dependencies before updates.")]
        public CallToolResult blocks_fingerprints_get(
            [Description("Device name")] string deviceName,
            [Description("Block or UDT name")] string name,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("blocks_fingerprints_get called with deviceName='{DeviceName}', name='{Name}'", deviceName, name);

            try
            {
                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<IList<Fingerprint>>.CreateError(ErrorCodes.NoProject, "No project is currently open"));
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<IList<Fingerprint>>.CreateError(scopeErrorCode, scopeError));
                }

                IEngineeringObject? obj = plcScope.BlockGroup.Blocks.Find(name);
                if (obj == null)
                {
                    obj = plcScope.TypeGroup.Types.Find(name);
                }

                if (obj == null)
                {
                    return McpToolResults.From(ToolResponse<IList<Fingerprint>>.CreateError(ErrorCodes.InvalidParameter, $"Block or UDT '{name}' not found"));
                }

                var result = _blocksAdapter.GetFingerprints(obj);
                return McpToolResults.From(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in blocks_fingerprints_get");
                return McpToolResults.From(ToolResponse<IList<Fingerprint>>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Set the execution priority for an Organization Block (OB) to control task scheduling in the PLC CPU. Returns success confirmation. Prerequisites: Project must be open, OB must exist, priority must be valid for OB type. Use dryRun=true to validate priority value. Valid priorities typically range based on CPU model (e.g., 1-26 for S7-1500).")]
        public CallToolResult blocks_ob_priority_set(
            [Description("Device name")] string deviceName,
            [Description("OB block name")] string obName,
            [Description("Priority number")] int priority,
            [Description("If true, only validate without setting")] bool dryRun = false,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("blocks_ob_priority_set called with deviceName='{DeviceName}', obName='{ObName}', priority={Priority}, dryRun={DryRun}", deviceName, obName, priority, dryRun);

            try
            {
                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<bool>.CreateError(ErrorCodes.NoProject, "No project is currently open"));
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<bool>.CreateError(scopeErrorCode, scopeError));
                }

                var obBlock = plcScope.BlockGroup.Blocks.Find(obName);
                if (obBlock == null)
                {
                    return McpToolResults.From(ToolResponse<bool>.CreateError(ErrorCodes.BlockNotFound, $"OB block '{obName}' not found"));
                }

                var result = _blocksAdapter.SetObPriority(obBlock, priority, dryRun);

                try
                {
                    _sessionManager.SaveCurrentProject();
                    _logger.LogInformation("Project saved after OB priority set");
                }
                catch (Exception saveEx)
                {
                    _logger.LogWarning(saveEx, "Failed to save project after OB priority set");
                }

                return McpToolResults.From(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in blocks_ob_priority_set");
                return McpToolResults.From(ToolResponse<bool>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        #endregion

        #region SD Document Tools

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Export a PLC block as SIMATIC SD documents (.s7dcl/.s7res) for version control or exchange with text editors/diff tools. The block is searched in all block groups. Returns the export state, the written files and any messages from TIA Portal. Prerequisites: Project must be open, device must exist, block must exist, TIA Portal V20 or newer. Use blocks_import_from_documents to reverse this operation.")]
        public CallToolResult blocks_export_as_documents(
            [Description("Device name")] string deviceName,
            [Description("Block name")] string blockName,
            [Description("Directory path to export the documents to")] string exportPath,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("blocks_export_as_documents called with deviceName='{DeviceName}', blockName='{BlockName}', exportPath='{ExportPath}'", deviceName, blockName, exportPath);

            try
            {
                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.NoProject, "No project is currently open"));
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(scopeErrorCode, scopeError));
                }

                var block = _sessionManager.PortalService.FindBlock(plcScope.BlockGroup, blockName);
                if (block == null)
                {
                    return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.BlockNotFound, $"Block '{blockName}' not found"));
                }

                var directory = new DirectoryInfo(exportPath);
                if (!directory.Exists)
                {
                    return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.InvalidParameter, $"Export directory '{exportPath}' does not exist"));
                }

                var result = _blocksAdapter.ExportAsDocuments(block, directory);
                if (result.IsFailure)
                {
                    return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.TiaError, $"Failed to export block '{blockName}' as documents: {string.Join("; ", result.Messages)}"));
                }

                return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateSuccess(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in blocks_export_as_documents");
                return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Export multiple PLC blocks from all block groups as SIMATIC SD documents (.s7dcl/.s7res), optionally filtered by name/regex. Each block is exported independently; returns succeeded/failed counts and a per-block state with files and TIA Portal messages. Prerequisites: Project must be open, device must exist, TIA Portal V20 or newer. Use an empty regexName to export all blocks.")]
        public CallToolResult blocks_export_all_as_documents(
            [Description("Device name")] string deviceName,
            [Description("Directory path to export the documents to")] string exportPath,
            [Description("Case-insensitive name fragment or regular expression to filter blocks. Empty string exports all blocks.")] string regexName = "",
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("blocks_export_all_as_documents called with deviceName='{DeviceName}', exportPath='{ExportPath}', regexName='{RegexName}'", deviceName, exportPath, regexName);

            try
            {
                if (!NameFilter.TryValidate(regexName, out var filterError))
                {
                    return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateError(ErrorCodes.InvalidParameter, filterError));
                }

                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateError(ErrorCodes.NoProject, "No project is currently open"));
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateError(scopeErrorCode, scopeError));
                }

                var directory = new DirectoryInfo(exportPath);
                if (!directory.Exists)
                {
                    return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateError(ErrorCodes.InvalidParameter, $"Export directory '{exportPath}' does not exist"));
                }

                var blocks = _sessionManager.PortalService.GetBlocks(plcScope, regexName);
                var result = _blocksAdapter.ExportAllAsDocuments(blocks, directory);
                return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateSuccess(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in blocks_export_all_as_documents");
                return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Import a PLC block from SIMATIC SD documents (.s7dcl/.s7res) into the PLC software. Returns the import state, the imported blocks and any messages from TIA Portal. Prerequisites: Project must be open, device must exist, matching .s7dcl file must exist in importPath, TIA Portal V20 or newer. Use dryRun=true to check the document pair and import option without importing. groupName is a path such as 'Motion/Axis'; leave it empty to import into the root block group.")]
        public CallToolResult blocks_import_from_documents(
            [Description("Device name")] string deviceName,
            [Description("Directory path containing the .s7dcl/.s7res document files")] string importPath,
            [Description("Block file name without extension (must match the .s7dcl file name)")] string fileNameWithoutExtension,
            [Description("Target block group path, e.g. 'Motion/Axis'. Leave empty for the root block group.")] string groupName = "",
            [Description("Import option: 'None' (fail if the block exists), 'Override', 'SkipInactiveCultures', or 'ActivateInactiveCultures'")] string importOption = "Override",
            [Description("If true, only validate the document pair and import option without importing")] bool dryRun = false,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("blocks_import_from_documents called with deviceName='{DeviceName}', importPath='{ImportPath}', fileNameWithoutExtension='{FileName}', groupName='{GroupName}', dryRun={DryRun}", deviceName, importPath, fileNameWithoutExtension, groupName, dryRun);

            try
            {
                if (!TryParseImportDocumentOptions(importOption, out var option, out var parseError))
                {
                    return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.InvalidParameter, parseError));
                }

                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.NoProject, "No project is currently open"));
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(scopeErrorCode, scopeError));
                }

                var directory = new DirectoryInfo(importPath);
                if (!directory.Exists)
                {
                    return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.InvalidParameter, $"Import directory '{importPath}' does not exist"));
                }

                var blockGroup = _sessionManager.PortalService.FindBlockGroup(plcScope.BlockGroup, groupName);
                if (blockGroup == null)
                {
                    return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.InvalidParameter, $"Block group '{groupName}' not found"));
                }

                var result = dryRun
                    ? _blocksAdapter.ValidateDocuments(directory, fileNameWithoutExtension, option, name => _sessionManager.PortalService.FindBlock(plcScope.BlockGroup, name) != null)
                    : _blocksAdapter.ImportFromDocuments(blockGroup, directory, fileNameWithoutExtension, option);

                if (result.IsFailure)
                {
                    var action = dryRun ? "Validation of" : "Import of";
                    return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.TiaError, $"{action} '{fileNameWithoutExtension}' failed: {string.Join("; ", result.Messages)}"));
                }

                if (!dryRun)
                {
                    SaveProjectAfterDocumentImport();
                }

                return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateSuccess(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in blocks_import_from_documents");
                return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Import multiple PLC blocks from SIMATIC SD documents (.s7dcl/.s7res) in a directory, optionally filtered by name/regex. Each document pair is imported independently; returns succeeded/failed counts and a per-file state with imported blocks and TIA Portal messages. The project is saved if at least one import succeeded. Prerequisites: Project must be open, device must exist, TIA Portal V20 or newer. Use dryRun=true to check the document pairs and import option without importing. groupName is a path such as 'Motion/Axis'; leave it empty to import into the root block group.")]
        public CallToolResult blocks_import_all_from_documents(
            [Description("Device name")] string deviceName,
            [Description("Directory path containing the .s7dcl/.s7res document files")] string importPath,
            [Description("Target block group path, e.g. 'Motion/Axis'. Leave empty for the root block group.")] string groupName = "",
            [Description("Case-insensitive name fragment or regular expression to filter which .s7dcl files to import. Empty string imports all.")] string regexName = "",
            [Description("Import option: 'None' (fail if the block exists), 'Override', 'SkipInactiveCultures', or 'ActivateInactiveCultures'")] string importOption = "Override",
            [Description("If true, only validate the document pairs and import option without importing")] bool dryRun = false,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("blocks_import_all_from_documents called with deviceName='{DeviceName}', importPath='{ImportPath}', groupName='{GroupName}', regexName='{RegexName}', dryRun={DryRun}", deviceName, importPath, groupName, regexName, dryRun);

            try
            {
                if (!TryParseImportDocumentOptions(importOption, out var option, out var parseError))
                {
                    return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateError(ErrorCodes.InvalidParameter, parseError));
                }

                if (!NameFilter.TryValidate(regexName, out var filterError))
                {
                    return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateError(ErrorCodes.InvalidParameter, filterError));
                }

                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateError(ErrorCodes.NoProject, "No project is currently open"));
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateError(scopeErrorCode, scopeError));
                }

                var directory = new DirectoryInfo(importPath);
                if (!directory.Exists)
                {
                    return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateError(ErrorCodes.InvalidParameter, $"Import directory '{importPath}' does not exist"));
                }

                var blockGroup = _sessionManager.PortalService.FindBlockGroup(plcScope.BlockGroup, groupName);
                if (blockGroup == null)
                {
                    return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateError(ErrorCodes.InvalidParameter, $"Block group '{groupName}' not found"));
                }

                var fileNames = directory.GetFiles("*.s7dcl", SearchOption.TopDirectoryOnly)
                    .Select(f => Path.GetFileNameWithoutExtension(f.Name))
                    .Where(n => NameFilter.IsMatch(n, regexName))
                    .ToList();

                var result = dryRun
                    ? _blocksAdapter.ValidateAllDocuments(directory, fileNames, option, name => _sessionManager.PortalService.FindBlock(plcScope.BlockGroup, name) != null)
                    : _blocksAdapter.ImportAllFromDocuments(blockGroup, directory, fileNames, option);

                if (!dryRun && result.SucceededCount > 0)
                {
                    SaveProjectAfterDocumentImport();
                }

                return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateSuccess(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in blocks_import_all_from_documents");
                return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        private void SaveProjectAfterDocumentImport()
        {
            try
            {
                _sessionManager.SaveCurrentProject();
                _logger.LogInformation("Project saved after block import from documents");
            }
            catch (Exception saveEx)
            {
                _logger.LogWarning(saveEx, "Failed to save project after block import from documents");
            }
        }

        private static bool TryParseImportDocumentOptions(string importOption, out ImportDocumentOptions option, out string errorMessage)
        {
            switch ((importOption ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "none":
                    option = ImportDocumentOptions.None;
                    errorMessage = string.Empty;
                    return true;
                case "override":
                    option = ImportDocumentOptions.Override;
                    errorMessage = string.Empty;
                    return true;
                case "skipinactivecultures":
                    option = ImportDocumentOptions.SkipInactiveCultures;
                    errorMessage = string.Empty;
                    return true;
                case "activateinactivecultures":
                    option = ImportDocumentOptions.ActivateInactiveCultures;
                    errorMessage = string.Empty;
                    return true;
                default:
                    option = ImportDocumentOptions.Override;
                    errorMessage = $"Invalid importOption '{importOption}'. Must be 'None', 'Override', 'SkipInactiveCultures', or 'ActivateInactiveCultures'";
                    return false;
            }
        }

        #endregion

        #region Webserver Pages Tools

        // [McpServerTool, Description("Generate blocks for webserver user-defined pages")]
        // public CallToolResult blocks_webserver_pages_generate_blocks(
        //     [Description("Device name")] string deviceName,
        //     [Description("Generate options: 'None' or 'Override'")] string options = "None",
        //     [Description("HTML directory path (optional)")] string? htmlDirectory = null,
        //     [Description("Default HTML page path (optional)")] string? defaultHtmlPage = null,
        //     [Description("Application name (optional)")] string? applicationName = null)
        // {
        //     _logger.LogInformation("blocks_webserver_pages_generate_blocks called with deviceName='{DeviceName}', options='{Options}'", deviceName, options);

        //     try
        //     {
        //         var project = _sessionManager.CurrentProject;
        //         if (project == null)
        //         {
        //             return McpToolResults.From(ToolResponse<IList<PlcBlock>>.CreateError(ErrorCodes.NoProject, "No project is currently open"));
        //         }

        //         var device = project.Devices.FirstOrDefault(d => d.Name == deviceName);
        //         if (device == null)
        //         {
        //             return McpToolResults.From(ToolResponse<IList<PlcBlock>>.CreateError(ErrorCodes.DeviceNotFound, $"Device '{deviceName}' not found"));
        //         }

        //         var deviceItem = device.DeviceItems.FirstOrDefault();
        //         if (deviceItem == null)
        //         {
        //             return McpToolResults.From(ToolResponse<IList<PlcBlock>>.CreateError(ErrorCodes.TiaError, "No device item found"));
        //         }

        //         var generateOptions = options == "Override" ? WebDBGenerateOptions.Override : WebDBGenerateOptions.None;
        //         DirectoryInfo? htmlDir = htmlDirectory != null ? new DirectoryInfo(htmlDirectory) : null;
        //         FileInfo? defaultPage = defaultHtmlPage != null ? new FileInfo(defaultHtmlPage) : null;

        //         var result = _blocksAdapter.GenerateWebserverPagesBlocks(deviceItem, generateOptions, htmlDir, defaultPage, applicationName);
        //         return McpToolResults.From(result);
        //     }
        //     catch (Exception ex)
        //     {
        //         _logger.LogError(ex, "Unexpected error in blocks_webserver_pages_generate_blocks");
        //         return McpToolResults.From(ToolResponse<IList<PlcBlock>>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
        //     }
        // }

        #endregion

        #region Loadable Files Tools

        // [McpServerTool, Description("Generate loadable file for blocks")]
        // public CallToolResult blocks_loadable_generate(
        //     [Description("Device name")] string deviceName,
        //     [Description("Block names to include")] string[] blockNames,
        //     [Description("Output file path")] string outputFilePath,
        //     [Description("Target option: 'Plc' or 'PlcSim'")] string targetOption = "Plc",
        //     [Description("If true, only validate without generating")] bool dryRun = false)
        // {
        //     _logger.LogInformation("blocks_loadable_generate called with deviceName='{DeviceName}', blockNames=[{BlockNames}], outputFilePath='{OutputFilePath}', targetOption='{TargetOption}', dryRun={DryRun}", deviceName, string.Join(",", blockNames), outputFilePath, targetOption, dryRun);

        //     try
        //     {
        //         var project = _sessionManager.CurrentProject;
        //         if (project == null)
        //         {
        //             return McpToolResults.From(ToolResponse<string>.CreateError(ErrorCodes.NoProject, "No project is currently open"));
        //         }

        //         var device = project.Devices.FirstOrDefault(d => d.Name == deviceName);
        //         if (device == null)
        //         {
        //             return McpToolResults.From(ToolResponse<string>.CreateError(ErrorCodes.DeviceNotFound, $"Device '{deviceName}' not found"));
        //         }

        //         var plcSoftware = _sessionManager.PortalService.GetPlcSoftware(device);
        //         if (plcSoftware == null)
        //         {
        //             return McpToolResults.From(ToolResponse<string>.CreateError(ErrorCodes.TiaError, $"Device '{deviceName}' does not have PLC software"));
        //         }

        //         var blocks = new List<PlcBlock>();
        //         foreach (var name in blockNames)
        //         {
        //             var block = plcSoftware.BlockGroup.Blocks.Find(name);
        //             if (block == null)
        //             {
        //                 return McpToolResults.From(ToolResponse<string>.CreateError(ErrorCodes.BlockNotFound, $"Block '{name}' not found"));
        //             }
        //             blocks.Add(block);
        //         }

        //         var target = targetOption == "PlcSim" ? TargetOption.PlcSim : TargetOption.Plc;
        //         var outputFile = new FileInfo(outputFilePath);

        //         var result = _blocksAdapter.GenerateLoadableFile(plcSoftware, outputFile, blocks, target, dryRun);
        //         return McpToolResults.From(result);
        //     }
        //     catch (Exception ex)
        //     {
        //         _logger.LogError(ex, "Unexpected error in blocks_loadable_generate");
        //         return McpToolResults.From(ToolResponse<string>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
        //     }
        // }

        #endregion

        #region Group-based Generation Tools

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Compile an external source file into blocks within a specific user-defined block group for organized code structure. Returns list of generated blocks. Prerequisites: Project must be open, external source and block group must exist. Options: 'None' fails on error, 'KeepOnError' preserves partial results. Use for organized multi-library projects.")]
        public CallToolResult blocks_external_source_generate_in_group(
            [Description("Device name")] string deviceName,
            [Description("External source name")] string sourceName,
            [Description("Block group name")] string groupName,
            [Description("Generate options: 'None' or 'KeepOnError'")] string options = "None",
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("blocks_external_source_generate_in_group called with deviceName='{DeviceName}', sourceName='{SourceName}', groupName='{GroupName}', options='{Options}'", deviceName, sourceName, groupName, options);

            try
            {
                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<IList<BlockInfo>>.CreateError(ErrorCodes.NoProject, "No project is currently open"));
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<IList<BlockInfo>>.CreateError(scopeErrorCode, scopeError));
                }

                var externalSource = plcScope.ExternalSourceGroup.ExternalSources.Find(sourceName);
                if (externalSource == null)
                {
                    return McpToolResults.From(ToolResponse<IList<BlockInfo>>.CreateError(ErrorCodes.InvalidParameter, $"External source '{sourceName}' not found"));
                }

                var blockGroup = plcScope.BlockGroup.Groups.Find(groupName);
                if (blockGroup == null)
                {
                    return McpToolResults.From(ToolResponse<IList<BlockInfo>>.CreateError(ErrorCodes.InvalidParameter, $"Block group '{groupName}' not found"));
                }

                var generateOptions = options == "KeepOnError" ? GenerateBlockOption.KeepOnError : GenerateBlockOption.None;
                var result = _blocksAdapter.GenerateBlocksFromSourceInGroup(externalSource, blockGroup, generateOptions);
                return McpToolResults.From(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in blocks_external_source_generate_in_group");
                return McpToolResults.From(ToolResponse<IList<BlockInfo>>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        #endregion

        #region Helper Methods

        private static string GetSourceExtension(ProgrammingLanguage language)
        {
            var languageName = language.ToString();
            return languageName switch
            {
                "SCL" => ".scl",
                "STL" => ".awl",
                "LAD" => ".lad",
                "FBD" => ".fbd",
                "GRAPH" => ".s7g",
                _ => ".scl"
            };
        }

        private bool TryResolveMember(
            PlcScope plcScope,
            string memberPath,
            string memberType,
            out IEngineeringObject? member,
            out string errorCode,
            out string errorMessage)
        {
            member = null;
            errorCode = string.Empty;
            errorMessage = string.Empty;

            var rootName = memberPath.Split('.')[0];
            var memberSubPath = memberPath.Substring(memberPath.IndexOf('.') + 1);

            if (memberType.ToLowerInvariant() == "db")
            {
                var dataBlock = plcScope.BlockGroup.Blocks.Find(rootName);
                if (dataBlock == null)
                {
                    errorCode = ErrorCodes.BlockNotFound;
                    errorMessage = $"DB '{rootName}' not found";
                    return false;
                }

                var blockInterface = dataBlock.GetType().GetProperty("Interface")?.GetValue(dataBlock);
                if (blockInterface == null)
                {
                    errorCode = ErrorCodes.TiaError;
                    errorMessage = "Block does not have an accessible interface";
                    return false;
                }

                var members = blockInterface.GetType().GetProperty("Members")?.GetValue(blockInterface) as MemberComposition;
                if (members == null)
                {
                    errorCode = ErrorCodes.TiaError;
                    errorMessage = "Block interface does not have members";
                    return false;
                }

                member = FindMemberByPath(members, memberSubPath);
                return true;
            }

            if (memberType.ToLowerInvariant() == "tag")
            {
                var tagTable = plcScope.TagTableGroup.TagTables.Find(rootName);
                if (tagTable == null)
                {
                    errorCode = ErrorCodes.InvalidParameter;
                    errorMessage = $"Tag table '{rootName}' not found";
                    return false;
                }

                member = tagTable.Tags.Find(memberSubPath);
                return true;
            }

            errorCode = ErrorCodes.InvalidParameter;
            errorMessage = "memberType must be 'db' or 'tag'";
            return false;
        }

        private Member? FindMemberByPath(MemberComposition members, string path)
        {
            var parts = path.Split('.');
            Member? current = null;

            foreach (var part in parts)
            {
                if (current == null)
                {
                    current = members.Find(part);
                }
                else
                {
                    // Check if current member has a Members property that is a MemberComposition
                    var membersProperty = current.GetType().GetProperty("Members");
                    if (membersProperty != null)
                    {
                        var subMembers = membersProperty.GetValue(current) as MemberComposition;
                        if (subMembers != null)
                        {
                            current = subMembers.Find(part);
                        }
                        else
                        {
                            return null;
                        }
                    }
                    else
                    {
                        return null;
                    }
                }

                if (current == null)
                {
                    return null;
                }
            }

            return current;
        }

        #endregion
    }
}