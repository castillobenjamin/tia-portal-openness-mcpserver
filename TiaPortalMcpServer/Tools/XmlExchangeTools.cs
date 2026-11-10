using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using TiaPortalMcpServer.Models;
using TiaPortalMcpServer.Services;

namespace TiaPortalMcpServer
{
    /// <summary>
    /// SimaticML (.xml) export and import of PLC blocks, PLC data types and PLC tag tables.
    /// </summary>
    /// <remarks>
    /// No lambda in this class may capture a Siemens option enum (ExportOptions, ImportOptions, SWImportOptions):
    /// the closure type would then have a Siemens value-type field and could not be loaded without the Openness resolver.
    /// </remarks>
    [McpServerToolType]
    public class XmlExchangeTools
    {
        private const string ImportOptionDescription = "Import options, comma-separated: 'None', 'Override' (replace existing objects), 'SkipInactiveCultures', 'ActivateInactiveCultures'";
        private const string SwImportOptionDescription = "Software import options, comma-separated: 'IgnoreStructuralChanges', 'IgnoreMissingReferencedObjects', 'IgnoreUnitAttributes'. Empty for none";
        private const string PreservePathBlockExportDescription = "If true, mirror the TIA project tree below exportPath: exportPath/Program blocks/<group path>/Name.xml. If false, write all files directly into exportPath";
        private const string PreservePathTypeExportDescription = "If true, mirror the TIA project tree below exportPath: exportPath/PLC data types/<group path>/Name.xml. If false, write all files directly into exportPath";
        private const string PreservePathBlockImportDescription = "If true, read the tree written by an export with preservePath: start in importPath/Program blocks when it exists (otherwise importPath), include subdirectories and import each one into the block group of the same path below groupName. If false, import only the files directly in importPath";
        private const string PreservePathTypeImportDescription = "If true, read the tree written by an export with preservePath: start in importPath/PLC data types when it exists (otherwise importPath), include subdirectories and import each one into the type group of the same path below groupName. If false, import only the files directly in importPath";

        private const string PreservePathTagTableExportDescription = "If true, mirror the TIA project tree below exportPath: exportPath/PLC tags/<group path>/Name.xml. If false, write all files directly into exportPath";
        private const string PreservePathTagTableImportDescription = "If true, read the tree written by an export with preservePath: start in importPath/PLC tags when it exists (otherwise importPath), include subdirectories and import each one into the tag table group of the same path below groupName. If false, import only the files directly in importPath";
        private const string IncludeDefaultsDescription = "Also write attributes that still have their default value (ExportOptions.WithDefaults). Off by default, so files only list values that were changed and match exports made without this option";
        private const string SaveNote = " Changes are made in the open project; call projects_save to persist them.";
        private const string NotSavedMessage = "The project was not saved; call projects_save to keep this change";

        private readonly ILogger<XmlExchangeTools> _logger;
        private readonly TiaPortalSessionManager _sessionManager;
        private readonly BlocksAdapter _blocksAdapter;

        public XmlExchangeTools(
            ILogger<XmlExchangeTools> logger,
            TiaPortalSessionManager sessionManager,
            BlocksAdapter blocksAdapter)
        {
            _logger = logger;
            _sessionManager = sessionManager;
            _blocksAdapter = blocksAdapter;
        }

        #region Blocks

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Export a PLC block as a SimaticML (.xml) file named after the block. The block is searched in all block groups. Files are written without the document info header, so repeated exports of an unchanged object are identical. Returns the state, the written file and any error. Prerequisites: Project must be open, device must have PLC software, block must exist, exportPath must exist. Use blocks_import_from_xml to reverse this operation.")]
        public CallToolResult blocks_export_as_xml(
            [Description("Device name")] string deviceName,
            [Description("Block name")] string blockName,
            [Description("Directory to write the .xml file to")] string exportPath,
            [Description(PreservePathBlockExportDescription)] bool preservePath = false,
            [Description(IncludeDefaultsDescription)] bool includeDefaults = false,
            [Description("Include read-only values in the export")] bool includeReadOnly = false,
            [Description("Replace an existing file. If false, an existing file makes the export fail")] bool overwrite = false,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("blocks_export_as_xml called with deviceName='{DeviceName}', blockName='{BlockName}', exportPath='{ExportPath}', preservePath={PreservePath}, unitName='{UnitName}'",
                deviceName, blockName, exportPath, preservePath, unitName);

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

                if (!Directory.Exists(exportPath))
                {
                    return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.InvalidParameter, $"Export directory '{exportPath}' does not exist"));
                }

                var found = _sessionManager.PortalService.FindBlockWithGroupPath(plcScope.BlockGroup, blockName);
                if (found.Block == null)
                {
                    return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.BlockNotFound, $"Block '{blockName}' not found in '{plcScope.DisplayName}'"));
                }

                var targetFile = GetExportFile(exportPath, ExchangePaths.BlocksRootFolder, found.GroupPath, found.Block.Name, preservePath);
                var result = _blocksAdapter.ExportBlockAsXml(found.Block, targetFile, found.GroupPath, ToExportOptions(includeDefaults, includeReadOnly), overwrite);
                return ToSingleResponse(result, $"Failed to export block '{blockName}'");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in blocks_export_as_xml");
                return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Export PLC blocks from all block groups as SimaticML (.xml) files without the document info header (repeated exports of unchanged blocks are identical), optionally filtered by name/regex and optionally mirroring the TIA group structure as directories. Each block is exported independently; returns succeeded/failed counts and a per-block state, group path and file. Prerequisites: Project must be open, device must have PLC software, exportPath must exist.")]
        public CallToolResult blocks_export_all_as_xml(
            [Description("Device name")] string deviceName,
            [Description("Directory to write the .xml files to")] string exportPath,
            [Description("Case-insensitive name fragment or regular expression to filter blocks. Empty string exports all blocks.")] string regexName = "",
            [Description(PreservePathBlockExportDescription)] bool preservePath = false,
            [Description(IncludeDefaultsDescription)] bool includeDefaults = false,
            [Description("Include read-only values in the export")] bool includeReadOnly = false,
            [Description("Replace existing files. If false, an existing file makes that block's export fail")] bool overwrite = false,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("blocks_export_all_as_xml called with deviceName='{DeviceName}', exportPath='{ExportPath}', regexName='{RegexName}', preservePath={PreservePath}, unitName='{UnitName}'",
                deviceName, exportPath, regexName, preservePath, unitName);

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

                if (!Directory.Exists(exportPath))
                {
                    return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateError(ErrorCodes.InvalidParameter, $"Export directory '{exportPath}' does not exist"));
                }

                var items = _sessionManager.PortalService.GetBlocksWithGroupPaths(plcScope.BlockGroup, regexName)
                    .Select(b => (b.Block, GetExportFile(exportPath, ExchangePaths.BlocksRootFolder, b.GroupPath, b.Block.Name, preservePath), b.GroupPath))
                    .ToList();

                var result = _blocksAdapter.ExportBlocksAsXml(items, ToExportOptions(includeDefaults, includeReadOnly), overwrite);
                return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateSuccess(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in blocks_export_all_as_xml");
                return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Import the PLC blocks of a SimaticML (.xml) file into a block group. Missing groups on the path are created. Returns the state and the imported blocks. Prerequisites: Project must be open, device must have PLC software, the file must be a SimaticML block export. Use dryRun=true to read the file and check it against the project without importing. groupName is a path such as 'Motion/Axis'; leave it empty for the root block group.")]
        public CallToolResult blocks_import_from_xml(
            [Description("Device name")] string deviceName,
            [Description("Path of the SimaticML .xml file")] string filePath,
            [Description("Target block group path, e.g. 'Motion/Axis'. Leave empty for the root block group.")] string groupName = "",
            [Description(ImportOptionDescription)] string importOption = "Override",
            [Description(SwImportOptionDescription)] string swImportOption = "",
            [Description("If true, only validate the file and options without importing")] bool dryRun = false,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("blocks_import_from_xml called with deviceName='{DeviceName}', filePath='{FilePath}', groupName='{GroupName}', importOption='{ImportOption}', swImportOption='{SwImportOption}', dryRun={DryRun}, unitName='{UnitName}'",
                deviceName, filePath, groupName, importOption, swImportOption, dryRun, unitName);

            try
            {
                if (!EnumFlags.TryParse<ImportOptions>(importOption, out var importOptions, out var importOptionError) ||
                    !EnumFlags.TryParse<SWImportOptions>(swImportOption, out var swImportOptions, out importOptionError))
                {
                    return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.InvalidParameter, importOptionError));
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

                var plan = XmlImportPlanner.PlanFile(new FileInfo(filePath), ExchangePaths.CombineGroupPath(groupName), SimaticMlInspector.KindBlock);
                if (plan.Result != null)
                {
                    return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.InvalidParameter,
                        $"Cannot import '{filePath}' as blocks: {string.Join("; ", plan.Result.Messages)}"));
                }

                var overrideExisting = (importOptions & ImportOptions.Override) != 0;
                if (dryRun)
                {
                    var validation = ValidateBlockImport(plcScope, plan, overrideExisting);
                    return ToSingleResponse(validation, $"Validation of '{filePath}' failed");
                }

                var result = ImportBlocks(plcScope, new List<XmlImportPlanItem> { plan }, importOptions, swImportOptions)[0];
                if (!result.IsFailure)
                {
                    SaveProjectAfterImport();
                }
                return ToSingleResponse(result, $"Import of '{filePath}' failed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in blocks_import_from_xml");
                return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Import the PLC blocks of all SimaticML (.xml) files in a directory, optionally filtered by file name/regex. With preservePath=true, subdirectories are included and map to block groups (created when missing). Files holding other objects, such as PLC data types, are skipped. Failed files are retried while another pass makes progress, so blocks that depend on each other can be imported in any order. Returns succeeded/failed/skipped counts and a per-file state. The project is saved if at least one import succeeded. Prerequisites: Project must be open, device must have PLC software. Use dryRun=true to check the files without importing.")]
        public CallToolResult blocks_import_all_from_xml(
            [Description("Device name")] string deviceName,
            [Description("Directory containing the SimaticML .xml files")] string importPath,
            [Description("Target block group path, e.g. 'Motion/Axis'. Leave empty for the root block group.")] string groupName = "",
            [Description("Case-insensitive name fragment or regular expression to filter which .xml files to import. Empty string imports all.")] string regexName = "",
            [Description(PreservePathBlockImportDescription)] bool preservePath = false,
            [Description(ImportOptionDescription)] string importOption = "Override",
            [Description(SwImportOptionDescription)] string swImportOption = "",
            [Description("If true, only validate the files and options without importing")] bool dryRun = false,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("blocks_import_all_from_xml called with deviceName='{DeviceName}', importPath='{ImportPath}', groupName='{GroupName}', regexName='{RegexName}', preservePath={PreservePath}, importOption='{ImportOption}', swImportOption='{SwImportOption}', dryRun={DryRun}, unitName='{UnitName}'",
                deviceName, importPath, groupName, regexName, preservePath, importOption, swImportOption, dryRun, unitName);

            try
            {
                if (!EnumFlags.TryParse<ImportOptions>(importOption, out var importOptions, out var importOptionError) ||
                    !EnumFlags.TryParse<SWImportOptions>(swImportOption, out var swImportOptions, out importOptionError))
                {
                    return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateError(ErrorCodes.InvalidParameter, importOptionError));
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

                var plans = XmlImportPlanner.PlanDirectory(directory, groupName, preservePath, regexName, SimaticMlInspector.KindBlock);
                var overrideExisting = (importOptions & ImportOptions.Override) != 0;

                IList<DocumentOperationResult> results;
                if (dryRun)
                {
                    results = new List<DocumentOperationResult>();
                    foreach (var plan in plans)
                    {
                        results.Add(plan.Result ?? ValidateBlockImport(plcScope, plan, overrideExisting));
                    }
                }
                else
                {
                    results = ImportBlocks(plcScope, plans, importOptions, swImportOptions);
                }

                var batch = DocumentBatchResult.From(results);
                if (!dryRun && batch.SucceededCount > 0)
                {
                    SaveProjectAfterImport();
                }
                return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateSuccess(batch));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in blocks_import_all_from_xml");
                return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        #endregion

        #region PLC data types

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Export a PLC data type (UDT) as a SimaticML (.xml) file named after the type. The type is searched in all type groups. Files are written without the document info header, so repeated exports of an unchanged object are identical. Returns the state, the written file and any error. Prerequisites: Project must be open, device must have PLC software, type must exist, exportPath must exist. Use types_import_from_xml to reverse this operation.")]
        public CallToolResult types_export_as_xml(
            [Description("Device name")] string deviceName,
            [Description("PLC data type name")] string typeName,
            [Description("Directory to write the .xml file to")] string exportPath,
            [Description(PreservePathTypeExportDescription)] bool preservePath = false,
            [Description(IncludeDefaultsDescription)] bool includeDefaults = false,
            [Description("Include read-only values in the export")] bool includeReadOnly = false,
            [Description("Replace an existing file. If false, an existing file makes the export fail")] bool overwrite = false,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("types_export_as_xml called with deviceName='{DeviceName}', typeName='{TypeName}', exportPath='{ExportPath}', preservePath={PreservePath}, unitName='{UnitName}'",
                deviceName, typeName, exportPath, preservePath, unitName);

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

                if (!Directory.Exists(exportPath))
                {
                    return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.InvalidParameter, $"Export directory '{exportPath}' does not exist"));
                }

                var found = _sessionManager.PortalService.FindTypeWithGroupPath(plcScope.TypeGroup, typeName);
                if (found.Type == null)
                {
                    return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.InvalidParameter, $"PLC data type '{typeName}' not found in '{plcScope.DisplayName}'"));
                }

                var targetFile = GetExportFile(exportPath, ExchangePaths.TypesRootFolder, found.GroupPath, found.Type.Name, preservePath);
                var result = _blocksAdapter.ExportTypeAsXml(found.Type, targetFile, found.GroupPath, ToExportOptions(includeDefaults, includeReadOnly), overwrite);
                return ToSingleResponse(result, $"Failed to export PLC data type '{typeName}'");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in types_export_as_xml");
                return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Export PLC data types (UDTs) from all type groups as SimaticML (.xml) files without the document info header (repeated exports of unchanged types are identical), optionally filtered by name/regex and optionally mirroring the TIA group structure as directories. Each type is exported independently; returns succeeded/failed counts and a per-type state, group path and file. Prerequisites: Project must be open, device must have PLC software, exportPath must exist.")]
        public CallToolResult types_export_all_as_xml(
            [Description("Device name")] string deviceName,
            [Description("Directory to write the .xml files to")] string exportPath,
            [Description("Case-insensitive name fragment or regular expression to filter types. Empty string exports all types.")] string regexName = "",
            [Description(PreservePathTypeExportDescription)] bool preservePath = false,
            [Description(IncludeDefaultsDescription)] bool includeDefaults = false,
            [Description("Include read-only values in the export")] bool includeReadOnly = false,
            [Description("Replace existing files. If false, an existing file makes that type's export fail")] bool overwrite = false,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("types_export_all_as_xml called with deviceName='{DeviceName}', exportPath='{ExportPath}', regexName='{RegexName}', preservePath={PreservePath}, unitName='{UnitName}'",
                deviceName, exportPath, regexName, preservePath, unitName);

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

                if (!Directory.Exists(exportPath))
                {
                    return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateError(ErrorCodes.InvalidParameter, $"Export directory '{exportPath}' does not exist"));
                }

                var items = _sessionManager.PortalService.GetTypesWithGroupPaths(plcScope.TypeGroup, regexName)
                    .Select(t => (t.Type, GetExportFile(exportPath, ExchangePaths.TypesRootFolder, t.GroupPath, t.Type.Name, preservePath), t.GroupPath))
                    .ToList();

                var result = _blocksAdapter.ExportTypesAsXml(items, ToExportOptions(includeDefaults, includeReadOnly), overwrite);
                return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateSuccess(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in types_export_all_as_xml");
                return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Import the PLC data types (UDTs) of a SimaticML (.xml) file into a type group. Missing groups on the path are created. Returns the state and the imported types. Prerequisites: Project must be open, device must have PLC software, the file must be a SimaticML PLC data type export. Use dryRun=true to read the file and check it against the project without importing. groupName is a path such as 'Motion/Axis'; leave it empty for the root type group.")]
        public CallToolResult types_import_from_xml(
            [Description("Device name")] string deviceName,
            [Description("Path of the SimaticML .xml file")] string filePath,
            [Description("Target type group path, e.g. 'Motion/Axis'. Leave empty for the root type group.")] string groupName = "",
            [Description(ImportOptionDescription)] string importOption = "Override",
            [Description(SwImportOptionDescription)] string swImportOption = "",
            [Description("If true, only validate the file and options without importing")] bool dryRun = false,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("types_import_from_xml called with deviceName='{DeviceName}', filePath='{FilePath}', groupName='{GroupName}', importOption='{ImportOption}', swImportOption='{SwImportOption}', dryRun={DryRun}, unitName='{UnitName}'",
                deviceName, filePath, groupName, importOption, swImportOption, dryRun, unitName);

            try
            {
                if (!EnumFlags.TryParse<ImportOptions>(importOption, out var importOptions, out var importOptionError) ||
                    !EnumFlags.TryParse<SWImportOptions>(swImportOption, out var swImportOptions, out importOptionError))
                {
                    return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.InvalidParameter, importOptionError));
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

                var plan = XmlImportPlanner.PlanFile(new FileInfo(filePath), ExchangePaths.CombineGroupPath(groupName), SimaticMlInspector.KindType);
                if (plan.Result != null)
                {
                    return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.InvalidParameter,
                        $"Cannot import '{filePath}' as PLC data types: {string.Join("; ", plan.Result.Messages)}"));
                }

                var overrideExisting = (importOptions & ImportOptions.Override) != 0;
                if (dryRun)
                {
                    var validation = ValidateTypeImport(plcScope, plan, overrideExisting);
                    return ToSingleResponse(validation, $"Validation of '{filePath}' failed");
                }

                var result = ImportTypes(plcScope, new List<XmlImportPlanItem> { plan }, importOptions, swImportOptions)[0];
                if (!result.IsFailure)
                {
                    SaveProjectAfterImport();
                }
                return ToSingleResponse(result, $"Import of '{filePath}' failed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in types_import_from_xml");
                return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Import the PLC data types (UDTs) of all SimaticML (.xml) files in a directory, optionally filtered by file name/regex. With preservePath=true, subdirectories are included and map to type groups (created when missing). Files holding other objects, such as blocks, are skipped. Failed files are retried while another pass makes progress, so types that use each other can be imported in any order. Returns succeeded/failed/skipped counts and a per-file state. The project is saved if at least one import succeeded. Prerequisites: Project must be open, device must have PLC software. Use dryRun=true to check the files without importing.")]
        public CallToolResult types_import_all_from_xml(
            [Description("Device name")] string deviceName,
            [Description("Directory containing the SimaticML .xml files")] string importPath,
            [Description("Target type group path, e.g. 'Motion/Axis'. Leave empty for the root type group.")] string groupName = "",
            [Description("Case-insensitive name fragment or regular expression to filter which .xml files to import. Empty string imports all.")] string regexName = "",
            [Description(PreservePathTypeImportDescription)] bool preservePath = false,
            [Description(ImportOptionDescription)] string importOption = "Override",
            [Description(SwImportOptionDescription)] string swImportOption = "",
            [Description("If true, only validate the files and options without importing")] bool dryRun = false,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("types_import_all_from_xml called with deviceName='{DeviceName}', importPath='{ImportPath}', groupName='{GroupName}', regexName='{RegexName}', preservePath={PreservePath}, importOption='{ImportOption}', swImportOption='{SwImportOption}', dryRun={DryRun}, unitName='{UnitName}'",
                deviceName, importPath, groupName, regexName, preservePath, importOption, swImportOption, dryRun, unitName);

            try
            {
                if (!EnumFlags.TryParse<ImportOptions>(importOption, out var importOptions, out var importOptionError) ||
                    !EnumFlags.TryParse<SWImportOptions>(swImportOption, out var swImportOptions, out importOptionError))
                {
                    return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateError(ErrorCodes.InvalidParameter, importOptionError));
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

                var plans = XmlImportPlanner.PlanDirectory(directory, groupName, preservePath, regexName, SimaticMlInspector.KindType);
                var overrideExisting = (importOptions & ImportOptions.Override) != 0;

                IList<DocumentOperationResult> results;
                if (dryRun)
                {
                    results = new List<DocumentOperationResult>();
                    foreach (var plan in plans)
                    {
                        results.Add(plan.Result ?? ValidateTypeImport(plcScope, plan, overrideExisting));
                    }
                }
                else
                {
                    results = ImportTypes(plcScope, plans, importOptions, swImportOptions);
                }

                var batch = DocumentBatchResult.From(results);
                if (!dryRun && batch.SucceededCount > 0)
                {
                    SaveProjectAfterImport();
                }
                return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateSuccess(batch));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in types_import_all_from_xml");
                return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        #endregion

        #region PLC tag tables

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Export a PLC tag table, with its tags and user constants, as a SimaticML (.xml) file without the document info header, so repeated exports of an unchanged table are identical. Without groupName the table is searched in all tag table groups. exportPath is either a directory (the file is named after the table) or the path of an .xml file. Fails when TIA Portal writes no file. Returns the state, the written file and any error. Prerequisites: Project must be open, device must have PLC software, tag table must exist. Use tags_tagtable_import to reverse this operation.")]
        public CallToolResult tags_tagtable_export(
            [Description("Device name")] string deviceName,
            [Description("Tag table name")] string tagTableName,
            [Description("Existing directory to write <table>.xml to, or the path of the .xml file to write")] string exportPath,
            [Description("Tag table group path, e.g. 'IO/Inputs'. Leave empty to search all groups.")] string groupName = "",
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "",
            [Description("Replace an existing file. If false, an existing file makes the export fail")] bool overwrite = false,
            [Description(PreservePathTagTableExportDescription)] bool preservePath = false,
            [Description(IncludeDefaultsDescription)] bool includeDefaults = false,
            [Description("Include read-only values in the export")] bool includeReadOnly = false)
        {
            _logger.LogInformation("tags_tagtable_export called with deviceName='{DeviceName}', tagTableName='{TagTableName}', exportPath='{ExportPath}', groupName='{GroupName}', unitName='{UnitName}', preservePath={PreservePath}",
                deviceName, tagTableName, exportPath, groupName, unitName, preservePath);

            try
            {
                if (string.IsNullOrWhiteSpace(tagTableName))
                {
                    return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.InvalidTagName, "Tag table name cannot be empty"));
                }

                if (string.IsNullOrWhiteSpace(exportPath))
                {
                    return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.InvalidParameter, "Export path cannot be empty"));
                }

                var exportsToFile = string.Equals(Path.GetExtension(exportPath), ".xml", StringComparison.OrdinalIgnoreCase);
                if (!exportsToFile && !Directory.Exists(exportPath))
                {
                    return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.InvalidParameter,
                        $"Export directory '{exportPath}' does not exist; pass an existing directory or the path of an .xml file"));
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

                PlcTagTable? table;
                string groupPath;
                if (string.IsNullOrWhiteSpace(groupName))
                {
                    var found = _sessionManager.PortalService.FindTagTableWithGroupPath(plcScope.TagTableGroup, tagTableName);
                    table = found.Table;
                    groupPath = found.GroupPath;
                }
                else
                {
                    groupPath = ExchangePaths.CombineGroupPath(groupName);
                    var group = _sessionManager.PortalService.FindTagTableGroup(plcScope.TagTableGroup, groupPath);
                    if (group == null)
                    {
                        return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.TagGroupNotFound, $"Tag table group '{groupPath}' not found in '{plcScope.DisplayName}'"));
                    }

                    table = group.TagTables.Find(tagTableName);
                }

                if (table == null)
                {
                    return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.TagTableNotFound, $"Tag table '{tagTableName}' not found in '{plcScope.DisplayName}'"));
                }

                var targetFile = exportsToFile
                    ? new FileInfo(Path.GetFullPath(exportPath))
                    : GetExportFile(exportPath, ExchangePaths.TagTablesRootFolder, groupPath, table.Name, preservePath);
                var result = _blocksAdapter.ExportTagTableAsXml(table, targetFile, groupPath, ToExportOptions(includeDefaults, includeReadOnly), overwrite);
                return ToSingleResponse(result, $"Failed to export tag table '{tagTableName}'");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in tags_tagtable_export");
                return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Export the PLC tag tables of all tag table groups, including the default tag table, as SimaticML (.xml) files with their tags and user constants, optionally filtered by name/regex and optionally mirroring the TIA group structure as directories (exportPath/PLC tags/<group path>/<table>.xml). Files are written without the document info header, so repeated exports of unchanged tables are identical. Each table is exported independently; returns succeeded/failed counts and a per-table state, group path and file. Prerequisites: Project must be open, device must have PLC software, exportPath must exist.")]
        public CallToolResult tags_tagtable_export_all(
            [Description("Device name")] string deviceName,
            [Description("Directory to write the .xml files to")] string exportPath,
            [Description("Case-insensitive name fragment or regular expression to filter tag tables. Empty string exports all tag tables.")] string regexName = "",
            [Description(PreservePathTagTableExportDescription)] bool preservePath = false,
            [Description(IncludeDefaultsDescription)] bool includeDefaults = false,
            [Description("Include read-only values in the export")] bool includeReadOnly = false,
            [Description("Replace existing files. If false, an existing file makes that table's export fail")] bool overwrite = false,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("tags_tagtable_export_all called with deviceName='{DeviceName}', exportPath='{ExportPath}', regexName='{RegexName}', preservePath={PreservePath}, unitName='{UnitName}'",
                deviceName, exportPath, regexName, preservePath, unitName);

            try
            {
                if (!NameFilter.TryValidate(regexName, out var filterError))
                {
                    return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateError(ErrorCodes.InvalidParameter, filterError));
                }

                if (!Directory.Exists(exportPath))
                {
                    return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateError(ErrorCodes.InvalidParameter, $"Export directory '{exportPath}' does not exist"));
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

                var items = _sessionManager.PortalService.GetTagTablesWithGroupPaths(plcScope.TagTableGroup, regexName)
                    .Select(t => (t.Table, GetExportFile(exportPath, ExchangePaths.TagTablesRootFolder, t.GroupPath, t.Table.Name, preservePath), t.GroupPath))
                    .ToList();

                var result = _blocksAdapter.ExportTagTablesAsXml(items, ToExportOptions(includeDefaults, includeReadOnly), overwrite);
                return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateSuccess(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in tags_tagtable_export_all");
                return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Import the PLC tag tables of a SimaticML (.xml) file into a tag table group. Missing groups on the path are created. Returns the state and the imported tag tables. Prerequisites: Project must be open, device must have PLC software, the file must be a SimaticML tag table export. Use dryRun=true to check the file against the project without importing; it lists the existing tag table that would be replaced. groupName is a path such as 'IO/Inputs'; leave it empty for the root tag table group." + SaveNote)]
        public CallToolResult tags_tagtable_import(
            [Description("Device name")] string deviceName,
            [Description("Path of the SimaticML .xml file")] string filePath,
            [Description("Target tag table group path, e.g. 'IO/Inputs'. Leave empty for the root tag table group.")] string groupName = "",
            [Description(ImportOptionDescription)] string importOption = "Override",
            [Description("If true, only validate the file and options without importing")] bool dryRun = false,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("tags_tagtable_import called with deviceName='{DeviceName}', filePath='{FilePath}', groupName='{GroupName}', importOption='{ImportOption}', dryRun={DryRun}, unitName='{UnitName}'",
                deviceName, filePath, groupName, importOption, dryRun, unitName);

            try
            {
                if (!EnumFlags.TryParse<ImportOptions>(importOption, out var importOptions, out var importOptionError))
                {
                    return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.InvalidParameter, importOptionError));
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

                var plan = XmlImportPlanner.PlanFile(new FileInfo(filePath), ExchangePaths.CombineGroupPath(groupName), SimaticMlInspector.KindTagTable);
                if (plan.Result != null)
                {
                    return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.InvalidParameter,
                        $"Cannot import '{filePath}' as a tag table: {string.Join("; ", plan.Result.Messages)}"));
                }

                var overrideExisting = (importOptions & ImportOptions.Override) != 0;
                if (dryRun)
                {
                    var validation = ValidateTagTableImport(plcScope, plan, overrideExisting);
                    return ToSingleResponse(validation, $"Validation of '{filePath}' failed");
                }

                var result = ImportTagTables(plcScope, new List<XmlImportPlanItem> { plan }, importOptions)[0];
                return ToSingleResponse(result, $"Import of '{filePath}' failed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in tags_tagtable_import");
                return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Import the PLC tag tables of all SimaticML (.xml) files in a directory, optionally filtered by file name/regex. With preservePath=true, subdirectories are included and map to tag table groups (created when missing), reading the tree written by tags_tagtable_export_all. Files holding other objects, such as blocks, are skipped. Failed files are retried while another pass makes progress. Returns succeeded/failed/skipped counts and a per-file state. Prerequisites: Project must be open, device must have PLC software. Use dryRun=true to check the files without importing; it lists the existing tag tables that would be replaced." + SaveNote)]
        public CallToolResult tags_tagtable_import_all(
            [Description("Device name")] string deviceName,
            [Description("Directory containing the SimaticML .xml files")] string importPath,
            [Description("Target tag table group path, e.g. 'IO/Inputs'. Leave empty for the root tag table group.")] string groupName = "",
            [Description("Case-insensitive name fragment or regular expression to filter which .xml files to import. Empty string imports all.")] string regexName = "",
            [Description(PreservePathTagTableImportDescription)] bool preservePath = false,
            [Description(ImportOptionDescription)] string importOption = "Override",
            [Description("If true, only validate the files and options without importing")] bool dryRun = false,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("tags_tagtable_import_all called with deviceName='{DeviceName}', importPath='{ImportPath}', groupName='{GroupName}', regexName='{RegexName}', preservePath={PreservePath}, importOption='{ImportOption}', dryRun={DryRun}, unitName='{UnitName}'",
                deviceName, importPath, groupName, regexName, preservePath, importOption, dryRun, unitName);

            try
            {
                if (!EnumFlags.TryParse<ImportOptions>(importOption, out var importOptions, out var importOptionError))
                {
                    return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateError(ErrorCodes.InvalidParameter, importOptionError));
                }

                if (!NameFilter.TryValidate(regexName, out var filterError))
                {
                    return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateError(ErrorCodes.InvalidParameter, filterError));
                }

                var directory = new DirectoryInfo(importPath);
                if (!directory.Exists)
                {
                    return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateError(ErrorCodes.InvalidParameter, $"Import directory '{importPath}' does not exist"));
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

                var plans = XmlImportPlanner.PlanDirectory(directory, groupName, preservePath, regexName, SimaticMlInspector.KindTagTable);
                var overrideExisting = (importOptions & ImportOptions.Override) != 0;

                IList<DocumentOperationResult> results;
                if (dryRun)
                {
                    results = new List<DocumentOperationResult>();
                    foreach (var plan in plans)
                    {
                        results.Add(plan.Result ?? ValidateTagTableImport(plcScope, plan, overrideExisting));
                    }
                }
                else
                {
                    results = ImportTagTables(plcScope, plans, importOptions);
                }

                return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateSuccess(DocumentBatchResult.From(results)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in tags_tagtable_import_all");
                return McpToolResults.From(ToolResponse<DocumentBatchResult>.CreateError(ErrorCodes.TiaError, "Unexpected error", ex.Message));
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Imports the planned block files: plans that already carry a result keep it, the rest are imported into their
        /// group (created when missing). Results come back in the order of <paramref name="plans"/>.
        /// </summary>
        private IList<DocumentOperationResult> ImportBlocks(PlcScope plcScope, IList<XmlImportPlanItem> plans, ImportOptions importOptions, SWImportOptions swImportOptions)
        {
            var results = new DocumentOperationResult[plans.Count];
            var pending = new List<(PlcBlockGroup Group, XmlImportPlanItem Item)>();
            var pendingIndexes = new List<int>();
            for (var i = 0; i < plans.Count; i++)
            {
                var plan = plans[i];
                if (plan.Result != null)
                {
                    results[i] = plan.Result;
                    continue;
                }

                try
                {
                    var existed = _sessionManager.PortalService.FindBlockGroup(plcScope.BlockGroup, plan.GroupPath) != null;
                    var group = _sessionManager.PortalService.GetOrCreateBlockGroup(plcScope.BlockGroup, plan.GroupPath);
                    if (!existed)
                    {
                        _logger.LogInformation("Created block group '{GroupPath}' in '{Scope}'", plan.GroupPath, plcScope.DisplayName);
                    }
                    pending.Add((group, plan));
                    pendingIndexes.Add(i);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create block group {GroupPath}", plan.GroupPath);
                    results[i] = XmlImportPlanner.CreateResult(plan, DocumentOperationResult.StateFailure, $"Cannot create block group '{plan.GroupPath}': {ex.Message}");
                }
            }

            var imported = _blocksAdapter.ImportBlocksFromXmlFiles(pending, importOptions, swImportOptions);
            for (var i = 0; i < imported.Count; i++)
            {
                results[pendingIndexes[i]] = imported[i];
            }
            return results;
        }

        /// <summary>
        /// Imports the planned PLC data type files, creating missing type groups. Results come back in the order of <paramref name="plans"/>.
        /// </summary>
        private IList<DocumentOperationResult> ImportTypes(PlcScope plcScope, IList<XmlImportPlanItem> plans, ImportOptions importOptions, SWImportOptions swImportOptions)
        {
            var results = new DocumentOperationResult[plans.Count];
            var pending = new List<(PlcTypeGroup Group, XmlImportPlanItem Item)>();
            var pendingIndexes = new List<int>();
            for (var i = 0; i < plans.Count; i++)
            {
                var plan = plans[i];
                if (plan.Result != null)
                {
                    results[i] = plan.Result;
                    continue;
                }

                try
                {
                    var existed = _sessionManager.PortalService.FindTypeGroup(plcScope.TypeGroup, plan.GroupPath) != null;
                    var group = _sessionManager.PortalService.GetOrCreateTypeGroup(plcScope.TypeGroup, plan.GroupPath);
                    if (!existed)
                    {
                        _logger.LogInformation("Created type group '{GroupPath}' in '{Scope}'", plan.GroupPath, plcScope.DisplayName);
                    }
                    pending.Add((group, plan));
                    pendingIndexes.Add(i);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create type group {GroupPath}", plan.GroupPath);
                    results[i] = XmlImportPlanner.CreateResult(plan, DocumentOperationResult.StateFailure, $"Cannot create type group '{plan.GroupPath}': {ex.Message}");
                }
            }

            var imported = _blocksAdapter.ImportTypesFromXmlFiles(pending, importOptions, swImportOptions);
            for (var i = 0; i < imported.Count; i++)
            {
                results[pendingIndexes[i]] = imported[i];
            }
            return results;
        }

        /// <summary>
        /// Imports the planned tag table files, creating missing tag table groups. Results come back in the order of <paramref name="plans"/>.
        /// </summary>
        private IList<DocumentOperationResult> ImportTagTables(PlcScope plcScope, IList<XmlImportPlanItem> plans, ImportOptions importOptions)
        {
            var results = new DocumentOperationResult[plans.Count];
            var pending = new List<(PlcTagTableGroup Group, XmlImportPlanItem Item)>();
            var pendingIndexes = new List<int>();
            for (var i = 0; i < plans.Count; i++)
            {
                var plan = plans[i];
                if (plan.Result != null)
                {
                    results[i] = plan.Result;
                    continue;
                }

                try
                {
                    var existed = _sessionManager.PortalService.FindTagTableGroup(plcScope.TagTableGroup, plan.GroupPath) != null;
                    var group = _sessionManager.PortalService.GetOrCreateTagTableGroup(plcScope.TagTableGroup, plan.GroupPath);
                    if (!existed)
                    {
                        _logger.LogInformation("Created tag table group '{GroupPath}' in '{Scope}'", plan.GroupPath, plcScope.DisplayName);
                    }
                    pending.Add((group, plan));
                    pendingIndexes.Add(i);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create tag table group {GroupPath}", plan.GroupPath);
                    results[i] = XmlImportPlanner.CreateResult(plan, DocumentOperationResult.StateFailure, $"Cannot create tag table group '{plan.GroupPath}': {ex.Message}");
                }
            }

            var imported = _blocksAdapter.ImportTagTablesFromXmlFiles(pending, importOptions);
            for (var i = 0; i < imported.Count; i++)
            {
                if (!imported[i].IsFailure)
                {
                    imported[i].Messages.Add(NotSavedMessage);
                }
                results[pendingIndexes[i]] = imported[i];
            }
            return results;
        }

        private DocumentOperationResult ValidateTagTableImport(PlcScope plcScope, XmlImportPlanItem plan, bool overrideExisting)
        {
            var groupExists = _sessionManager.PortalService.FindTagTableGroup(plcScope.TagTableGroup, plan.GroupPath) != null;
            var existing = _sessionManager.PortalService.FindTagTableWithGroupPath(plcScope.TagTableGroup, plan.Content!.Name!);
            return ValidateImport(plan, "tag table", groupExists, existing.Table != null, existing.GroupPath, overrideExisting);
        }

        private DocumentOperationResult ValidateBlockImport(PlcScope plcScope, XmlImportPlanItem plan, bool overrideExisting)
        {
            var groupExists = _sessionManager.PortalService.FindBlockGroup(plcScope.BlockGroup, plan.GroupPath) != null;
            var existing = _sessionManager.PortalService.FindBlockWithGroupPath(plcScope.BlockGroup, plan.Content!.Name!);
            return ValidateImport(plan, "block", groupExists, existing.Block != null, existing.GroupPath, overrideExisting);
        }

        private DocumentOperationResult ValidateTypeImport(PlcScope plcScope, XmlImportPlanItem plan, bool overrideExisting)
        {
            var groupExists = _sessionManager.PortalService.FindTypeGroup(plcScope.TypeGroup, plan.GroupPath) != null;
            var existing = _sessionManager.PortalService.FindTypeWithGroupPath(plcScope.TypeGroup, plan.Content!.Name!);
            return ValidateImport(plan, "PLC data type", groupExists, existing.Type != null, existing.GroupPath, overrideExisting);
        }

        private static DocumentOperationResult ValidateImport(XmlImportPlanItem plan, string objectKind, bool groupExists, bool objectExists, string existingGroupPath, bool overrideExisting)
        {
            var objectName = plan.Content!.Name;
            var result = XmlImportPlanner.CreateResult(plan, DocumentOperationResult.StateValidated,
                $"Would import {plan.Content.ElementName} '{objectName}' into '{DisplayGroupPath(plan.GroupPath)}'");

            if (!groupExists)
            {
                result.Messages.Add($"Group '{plan.GroupPath}' does not exist and would be created");
            }

            if (objectExists)
            {
                if (!overrideExisting)
                {
                    result.State = DocumentOperationResult.StateFailure;
                    result.Messages.Add($"A {objectKind} named '{objectName}' already exists in '{DisplayGroupPath(existingGroupPath)}'; add 'Override' to importOption to replace it");
                }
                else
                {
                    result.Messages.Add($"The existing {objectKind} '{objectName}' in '{DisplayGroupPath(existingGroupPath)}' would be replaced");
                }
            }

            return result;
        }

        private static FileInfo GetExportFile(string exportPath, string rootFolder, string groupPath, string objectName, bool preservePath)
        {
            var directory = ExchangePaths.ToExportDirectory(exportPath, rootFolder, groupPath, preservePath);
            return new FileInfo(Path.Combine(directory, ExchangePaths.ToFileName(objectName) + ".xml"));
        }

        private static ExportOptions ToExportOptions(bool includeDefaults, bool includeReadOnly)
        {
            var options = ExportOptions.None;
            if (includeDefaults)
            {
                options |= ExportOptions.WithDefaults;
            }
            if (includeReadOnly)
            {
                options |= ExportOptions.WithReadOnly;
            }
            return options;
        }

        private static CallToolResult ToSingleResponse(DocumentOperationResult result, string failurePrefix)
        {
            if (result.IsFailure)
            {
                return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateError(ErrorCodes.TiaError, $"{failurePrefix}: {string.Join("; ", result.Messages)}"));
            }

            return McpToolResults.From(ToolResponse<DocumentOperationResult>.CreateSuccess(result));
        }

        private static string DisplayGroupPath(string groupPath)
        {
            return string.IsNullOrEmpty(groupPath) ? "(root)" : groupPath;
        }

        private void SaveProjectAfterImport()
        {
            try
            {
                _sessionManager.SaveCurrentProject();
                _logger.LogInformation("Project saved after SimaticML import");
            }
            catch (Exception saveEx)
            {
                _logger.LogWarning(saveEx, "Failed to save project after SimaticML import");
            }
        }

        #endregion
    }
}
