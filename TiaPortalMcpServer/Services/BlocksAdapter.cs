using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Blocks.Interface;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Loader;
using TiaPortalMcpServer.Models;

namespace TiaPortalMcpServer.Services
{
    /// <summary>
    /// Adapter for TIA Portal Openness block-related operations
    /// Wraps TIA Openness APIs with error handling and normalization
    /// </summary>
    public class BlocksAdapter
    {
        private readonly ILogger<BlocksAdapter> _logger;

        public BlocksAdapter(ILogger<BlocksAdapter> logger)
        {
            _logger = logger;
        }

        #region ProDiag Operations

        /// <summary>
        /// Get assigned ProDiag FB for a DB or tag member
        /// </summary>
        public ToolResponse<string?> GetAssignedProDiagFb(IEngineeringObject member)
        {
            try
            {
                var value = member.GetAttribute("AssignedProDiagFB");
                return ToolResponse<string?>.CreateSuccess(value?.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get AssignedProDiagFB for member");
                return ToolResponse<string?>.CreateError(ErrorCodes.TiaError, "Failed to get AssignedProDiagFB", ex.Message);
            }
        }

        /// <summary>
        /// Set assigned ProDiag FB for a DB or tag member
        /// </summary>
        public ToolResponse<bool> SetAssignedProDiagFb(IEngineeringObject member, string proDiagFbName, bool dryRun = false)
        {
            try
            {
                if (!dryRun)
                {
                    member.SetAttribute("AssignedProDiagFB", proDiagFbName);
                }
                return ToolResponse<bool>.CreateSuccess(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set AssignedProDiagFB for member");
                return ToolResponse<bool>.CreateError(ErrorCodes.TiaError, "Failed to set AssignedProDiagFB", ex.Message);
            }
        }

        /// <summary>
        /// Get ProDiag FB attributes
        /// </summary>
        public ToolResponse<ProDiagAttributes> GetProDiagAttributes(FB fb)
        {
            try
            {
                var attributes = new ProDiagAttributes
                {
                    ProDiagVersion = fb.GetAttribute("ProDiagVersion")?.ToString(),
                    InitialValueAcquisition = Convert.ToBoolean(fb.GetAttribute("InitialValueAcquisition")),
                    UseCentralTimeStamp = Convert.ToBoolean(fb.GetAttribute("UseCentralTimeStamp"))
                };
                return ToolResponse<ProDiagAttributes>.CreateSuccess(attributes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get ProDiag attributes for FB {Name}", fb.Name);
                return ToolResponse<ProDiagAttributes>.CreateError(ErrorCodes.TiaError, "Failed to get ProDiag attributes", ex.Message);
            }
        }

        /// <summary>
        /// Export ProDiag alarm messages to CSV
        /// </summary>
        public ToolResponse<string> ExportProDiagCsv(FB fb, DirectoryInfo directory)
        {
            try
            {
                if (fb.ProgrammingLanguage != ProgrammingLanguage.ProDiag)
                {
                    return ToolResponse<string>.CreateError(ErrorCodes.InvalidParameter, "Block programming language must be ProDiag");
                }

                if (!fb.IsConsistent)
                {
                    return ToolResponse<string>.CreateError(ErrorCodes.TiaError, "ProDiag FB must be consistent");
                }

                fb.ExportProDIAGInfo(directory);
                return ToolResponse<string>.CreateSuccess($"Exported to {directory.FullName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to export ProDiag CSV for FB {Name}", fb.Name);
                return ToolResponse<string>.CreateError(ErrorCodes.TiaError, "Failed to export ProDiag CSV", ex.Message);
            }
        }

        #endregion

        #region External Sources

        /// <summary>
        /// Add external source file
        /// </summary>
        public ToolResponse<ExternalSourceInfo> AddExternalSource(PlcScope plcScope, string name, string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists)
                {
                    return ToolResponse<ExternalSourceInfo>.CreateError(ErrorCodes.InvalidParameter, "Source file does not exist");
                }

                var extension = fileInfo.Extension.ToLowerInvariant();
                if (!new[] { ".awl", ".scl", ".db", ".udt" }.Contains(extension))
                {
                    return ToolResponse<ExternalSourceInfo>.CreateError(ErrorCodes.InvalidParameter, "Unsupported file extension. Must be .awl, .scl, .db, or .udt");
                }

                var externalSource = plcScope.ExternalSourceGroup.ExternalSources.CreateFromFile(name, filePath);
                var info = new ExternalSourceInfo
                {
                    Name = externalSource.Name,
                    Path = filePath
                };
                return ToolResponse<ExternalSourceInfo>.CreateSuccess(info);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add external source {Name} from {Path}", name, filePath);
                return ToolResponse<ExternalSourceInfo>.CreateError(ErrorCodes.TiaError, "Failed to add external source", ex.Message);
            }
        }

        /// <summary>
        /// Generate source from blocks or types
        /// </summary>
        public ToolResponse<string> GenerateSource(PlcScope plcScope, IEnumerable<IGenerateSource> sources, FileInfo targetFile, GenerateOptions options)
        {
            try
            {
                // Get the external source system group of the main PLC software or Software Unit
                var systemGroup = plcScope.ExternalSourceGroup;
                if (systemGroup == null)
                {
                    return ToolResponse<string>.CreateError(ErrorCodes.TiaError, "External source system group not available");
                }

                systemGroup.GenerateSource(sources, targetFile, options);
                return ToolResponse<string>.CreateSuccess($"Generated source to {targetFile.FullName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate source");
                return ToolResponse<string>.CreateError(ErrorCodes.TiaError, "Failed to generate source", ex.Message);
            }
        }

        /// <summary>
        /// Generate source from blocks or types and return the generated text
        /// </summary>
        public ToolResponse<string> GenerateSourceText(PlcScope plcScope, IEnumerable<IGenerateSource> sources, string? fileExtension, GenerateOptions options)
        {
            var extension = string.IsNullOrWhiteSpace(fileExtension) ? ".scl" : fileExtension.Trim();
            if (!extension.StartsWith(".", StringComparison.Ordinal))
            {
                extension = "." + extension;
            }

            var tempFilePath = Path.Combine(Path.GetTempPath(), $"tia-source-{Guid.NewGuid():N}{extension}");
            var tempFile = new FileInfo(tempFilePath);

            try
            {
                var generateResult = GenerateSource(plcScope, sources, tempFile, options);
                if (!generateResult.Success)
                {
                    return ToolResponse<string>.CreateError(generateResult.ErrorCode ?? ErrorCodes.TiaError, generateResult.Error ?? "Failed to generate source", generateResult.Details);
                }

                var content = File.ReadAllText(tempFile.FullName);
                return ToolResponse<string>.CreateSuccess(content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate source text");
                return ToolResponse<string>.CreateError(ErrorCodes.TiaError, "Failed to generate source text", ex.Message);
            }
            finally
            {
                try
                {
                    if (tempFile.Exists)
                    {
                        tempFile.Delete();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temporary source file {Path}", tempFile.FullName);
                }
            }
        }

        private static BlockInfo CreateBlockInfo(IEngineeringObject block)
        {
            var plcBlock = block as PlcBlock;
            return new BlockInfo
            {
                Name = block.GetAttribute("Name")?.ToString() ?? string.Empty,
                Type = block.GetType().Name,
                ProgrammingLanguage = block.GetAttribute("ProgrammingLanguage")?.ToString(),
                AutoNumber = plcBlock?.AutoNumber ?? false,
                CreationDate = plcBlock?.CreationDate,
                ModifiedDate = plcBlock?.ModifiedDate,
                CompileDate = plcBlock?.CompileDate
            };
        }

        /// <summary>
        /// Generate blocks from external source
        /// </summary>
        public ToolResponse<IList<BlockInfo>> GenerateBlocksFromSource(PlcExternalSource externalSource, GenerateBlockOption options)
        {
            try
            {
                var objects = externalSource.GenerateBlocksFromSource(options);
                var blockInfos = objects.Select(CreateBlockInfo).ToList();
                return ToolResponse<IList<BlockInfo>>.CreateSuccess(blockInfos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate blocks from source {Name}", externalSource.Name);
                return ToolResponse<IList<BlockInfo>>.CreateError(ErrorCodes.TiaError, "Failed to generate blocks from source", ex.Message);
            }
        }

        /// <summary>
        /// Generate blocks from external source in specific group
        /// </summary>
        public ToolResponse<IList<BlockInfo>> GenerateBlocksFromSourceInGroup(PlcExternalSource externalSource, PlcBlockUserGroup blockGroup, GenerateBlockOption options)
        {
            try
            {
                var objects = externalSource.GenerateBlocksFromSource(blockGroup, options);
                var blockInfos = objects.Select(CreateBlockInfo).ToList();
                return ToolResponse<IList<BlockInfo>>.CreateSuccess(blockInfos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate blocks from source {Name} in group {Group}", externalSource.Name, blockGroup.Name);
                return ToolResponse<IList<BlockInfo>>.CreateError(ErrorCodes.TiaError, "Failed to generate blocks from source in group", ex.Message);
            }
        }

        /// <summary>
        /// Generate types from external source in specific group
        /// </summary>
        public ToolResponse<IList<BlockInfo>> GenerateTypesFromSourceInGroup(PlcExternalSource externalSource, PlcTypeUserGroup typeGroup, GenerateBlockOption options)
        {
            try
            {
                var objects = externalSource.GenerateBlocksFromSource(typeGroup, options);
                var blockInfos = objects.Select(CreateBlockInfo).ToList();
                return ToolResponse<IList<BlockInfo>>.CreateSuccess(blockInfos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate types from source {Name} in group {Group}", externalSource.Name, typeGroup.Name);
                return ToolResponse<IList<BlockInfo>>.CreateError(ErrorCodes.TiaError, "Failed to generate types from source in group", ex.Message);
            }
        }

        /// <summary>
        /// Delete external source
        /// </summary>
        public ToolResponse<bool> DeleteExternalSource(PlcExternalSource externalSource, bool dryRun = false)
        {
            try
            {
                if (!dryRun)
                {
                    externalSource.Delete();
                }
                return ToolResponse<bool>.CreateSuccess(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete external source {Name}", externalSource.Name);
                return ToolResponse<bool>.CreateError(ErrorCodes.TiaError, "Failed to delete external source", ex.Message);
            }
        }

        #endregion

        #region Block Operations

        /// <summary>
        /// Delete UDT
        /// </summary>
        public ToolResponse<bool> DeleteUdt(PlcType udt, bool dryRun = false)
        {
            try
            {
                if (!dryRun)
                {
                    udt.Delete();
                }
                return ToolResponse<bool>.CreateSuccess(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete UDT {Name}", udt.Name);
                return ToolResponse<bool>.CreateError(ErrorCodes.TiaError, "Failed to delete UDT", ex.Message);
            }
        }

        /// <summary>
        /// Get system data types
        /// </summary>
        public ToolResponse<IList<PlcSystemTypeGroup>> GetSystemTypes(PlcSoftware plcSoftware)
        {
            try
            {
                var systemTypeGroups = plcSoftware.TypeGroup.SystemTypeGroups.ToList();
                return ToolResponse<IList<PlcSystemTypeGroup>>.CreateSuccess(systemTypeGroups);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get system types");
                return ToolResponse<IList<PlcSystemTypeGroup>>.CreateError(ErrorCodes.TiaError, "Failed to get system types", ex.Message);
            }
        }

        /// <summary>
        /// Get user-defined types (UDTs)
        /// </summary>
        public ToolResponse<IList<PlcTypeInfo>> GetUserTypes(PlcScope plcScope)
        {
            try
            {
                var types = plcScope.TypeGroup.Types.ToList();
                var userTypes = types
                    .Select(t => new PlcTypeInfo
                    {
                        Name = t.Name,
                        Namespace = t.Namespace
                    })
                    .ToList();
                return ToolResponse<IList<PlcTypeInfo>>.CreateSuccess(userTypes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get user-defined types");
                return ToolResponse<IList<PlcTypeInfo>>.CreateError(ErrorCodes.TiaError, "Failed to get user-defined types", ex.Message);
            }
        }

        /// <summary>
        /// Open block in editor
        /// </summary>
        public ToolResponse<bool> OpenBlockInEditor(PlcBlock block)
        {
            try
            {
                block.ShowInEditor();
                return ToolResponse<bool>.CreateSuccess(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open block {Name} in editor", block.Name);
                return ToolResponse<bool>.CreateError(ErrorCodes.TiaError, "Failed to open block in editor", ex.Message);
            }
        }

        /// <summary>
        /// Open type in editor
        /// </summary>
        public ToolResponse<bool> OpenTypeInEditor(PlcType type)
        {
            try
            {
                type.ShowInEditor();
                return ToolResponse<bool>.CreateSuccess(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open type {Name} in editor", type.Name);
                return ToolResponse<bool>.CreateError(ErrorCodes.TiaError, "Failed to open type in editor", ex.Message);
            }
        }

        /// <summary>
        /// Get fingerprints for block or UDT
        /// </summary>
        public ToolResponse<IList<Fingerprint>> GetFingerprints(IEngineeringObject obj)
        {
            try
            {
                // Try to get the service using reflection since IEngineeringObject may not have GetService directly
                var getServiceMethod = obj.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "GetService" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);

                if (getServiceMethod == null)
                {
                    return ToolResponse<IList<Fingerprint>>.CreateError(ErrorCodes.TiaError, "GetService method not available on object");
                }

                var generic = getServiceMethod.MakeGenericMethod(typeof(FingerprintProvider));
                var provider = generic.Invoke(obj, null) as FingerprintProvider;

                if (provider == null)
                {
                    return ToolResponse<IList<Fingerprint>>.CreateError(ErrorCodes.TiaError, "Fingerprint provider not available");
                }

                var fingerprints = provider.GetFingerprints();
                return ToolResponse<IList<Fingerprint>>.CreateSuccess(fingerprints);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get fingerprints for {Type}", obj.GetType().Name);
                return ToolResponse<IList<Fingerprint>>.CreateError(ErrorCodes.TiaError, "Failed to get fingerprints", ex.Message);
            }
        }

        /// <summary>
        /// Set OB priority
        /// </summary>
        public ToolResponse<bool> SetObPriority(PlcBlock obBlock, int priority, bool dryRun = false)
        {
            try
            {
                if (!dryRun)
                {
                    obBlock.SetAttribute("PriorityNumber", priority);
                }
                return ToolResponse<bool>.CreateSuccess(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set priority for OB {Name}", obBlock.Name);
                return ToolResponse<bool>.CreateError(ErrorCodes.TiaError, "Failed to set OB priority", ex.Message);
            }
        }

        #endregion

        #region Webserver Pages

        /// <summary>
        /// Generate blocks for webserver user-defined pages
        /// </summary>
        // public ToolResponse<IList<PlcBlock>> GenerateWebserverPagesBlocks(DeviceItem deviceItem, WebDBGenerateOptions options, DirectoryInfo? htmlDirectory = null, FileInfo? defaultHtmlPage = null, string? applicationName = null)
        // {
        //     try
        //     {
        //         var service = deviceItem.GetService<WebserverUserDefinedPages>();
        //         if (service == null)
        //         {
        //             return ToolResponse<IList<PlcBlock>>.CreateError(ErrorCodes.TiaError, "Webserver user-defined pages service not available");
        //         }

        //         IList<PlcBlock> blocks;
        //         if (htmlDirectory != null && defaultHtmlPage != null && applicationName != null)
        //         {
        //             blocks = service.GenerateBlocks(htmlDirectory, defaultHtmlPage, applicationName, options);
        //         }
        //         else
        //         {
        //             blocks = service.GenerateBlocks(options);
        //         }

        //         return ToolResponse<IList<PlcBlock>>.CreateSuccess(blocks);
        //     }
        //     catch (Exception ex)
        //     {
        //         _logger.LogError(ex, "Failed to generate webserver pages blocks");
        //         return ToolResponse<IList<PlcBlock>>.CreateError(ErrorCodes.TiaError, "Failed to generate webserver pages blocks", ex.Message);
        //     }
        // }

        #endregion

        #region SD Documents (.s7dcl/.s7res)

        /// <summary>
        /// Export a block as SIMATIC SD documents (.s7dcl/.s7res) named after the block.
        /// Failures are reported in the result instead of being thrown.
        /// </summary>
        public DocumentOperationResult ExportAsDocuments(PlcBlock block, DirectoryInfo directory)
        {
            var blockName = block.Name;
            try
            {
                var exportResult = block.ExportAsDocuments(directory, blockName);
                return new DocumentOperationResult
                {
                    Name = blockName,
                    State = exportResult.State.ToString(),
                    Files = exportResult.ExportedDocuments.Select(f => f.FullName).ToList(),
                    Messages = GetDocumentMessages(exportResult.Messages)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to export block {Name} as documents", blockName);
                return CreateFailedDocumentResult(blockName, ex.Message);
            }
        }

        /// <summary>
        /// Import a block from SIMATIC SD documents (.s7dcl/.s7res) into the given block group.
        /// Failures are reported in the result instead of being thrown.
        /// </summary>
        public DocumentOperationResult ImportFromDocuments(PlcBlockGroup blockGroup, DirectoryInfo directory, string fileNameWithoutExtension, ImportDocumentOptions option)
        {
            try
            {
                var importResult = blockGroup.Blocks.ImportFromDocuments(directory, fileNameWithoutExtension, option);
                return new DocumentOperationResult
                {
                    Name = fileNameWithoutExtension,
                    State = importResult.State.ToString(),
                    Blocks = importResult.ImportedPlcBlocks.Select(block => CreateBlockInfo(block)).ToList(),
                    Messages = GetDocumentMessages(importResult.Messages)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import block {Name} from documents", fileNameWithoutExtension);
                return CreateFailedDocumentResult(fileNameWithoutExtension, ex.Message);
            }
        }

        /// <summary>
        /// Dry run for an import: checks the document pair and the import option against the project without importing.
        /// </summary>
        /// <param name="blockExists">Returns true when a block with the given name already exists anywhere in the PLC software.</param>
        public DocumentOperationResult ValidateDocuments(DirectoryInfo directory, string fileNameWithoutExtension, ImportDocumentOptions option, Func<string, bool> blockExists)
        {
            var result = new DocumentOperationResult { Name = fileNameWithoutExtension, State = DocumentOperationResult.StateValidated };

            var s7dclFile = new FileInfo(Path.Combine(directory.FullName, fileNameWithoutExtension + ".s7dcl"));
            if (!s7dclFile.Exists)
            {
                result.State = DocumentOperationResult.StateFailure;
                result.Messages.Add($"File '{s7dclFile.Name}' not found in '{directory.FullName}'");
                return result;
            }

            if (s7dclFile.Length == 0)
            {
                result.State = DocumentOperationResult.StateFailure;
                result.Messages.Add($"File '{s7dclFile.Name}' is empty");
                return result;
            }

            if (!File.Exists(Path.Combine(directory.FullName, fileNameWithoutExtension + ".s7res")))
            {
                result.Messages.Add($"No '{fileNameWithoutExtension}.s7res' next to the .s7dcl file; multilingual texts will not be imported");
            }

            try
            {
                if (option == ImportDocumentOptions.None && blockExists(fileNameWithoutExtension))
                {
                    result.State = DocumentOperationResult.StateFailure;
                    result.Messages.Add($"Block '{fileNameWithoutExtension}' already exists and importOption 'None' does not overwrite existing blocks. Use 'Override' to replace it");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check whether block {Name} already exists", fileNameWithoutExtension);
                result.Messages.Add($"Could not check whether block '{fileNameWithoutExtension}' already exists: {ex.Message}");
            }

            return result;
        }

        private static IList<string> GetDocumentMessages(DocumentResultMessageComposition messages)
        {
            return messages.Select(m => m.Message).ToList();
        }

        private static DocumentOperationResult CreateFailedDocumentResult(string name, string error)
        {
            return new DocumentOperationResult
            {
                Name = name,
                State = DocumentOperationResult.StateFailure,
                Messages = new List<string> { error }
            };
        }

        #endregion

        #region SimaticML XML (.xml)

        // Note: no lambdas here capture the Siemens option enums. A closure with a Siemens value-type field
        // cannot be loaded without the Openness resolver, which breaks reflection over this assembly in unit tests.

        /// <summary>
        /// Export a block as a SimaticML file. Failures are reported in the result instead of being thrown.
        /// </summary>
        public DocumentOperationResult ExportBlockAsXml(PlcBlock block, FileInfo targetFile, string groupPath, ExportOptions options, bool overwrite)
        {
            var name = block.Name;
            var prepareError = PrepareXmlExportTarget(targetFile, overwrite);
            if (prepareError != null)
            {
                return CreateXmlResult(name, groupPath, targetFile, DocumentOperationResult.StateFailure, prepareError);
            }

            try
            {
                block.Export(targetFile, options);
                return CreateXmlResult(name, groupPath, targetFile, DocumentOperationResult.StateSuccess, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to export block {Name} as SimaticML to {Path}", name, targetFile.FullName);
                var result = CreateXmlResult(name, groupPath, targetFile, DocumentOperationResult.StateFailure, ex.Message);
                AddExportHints(result, "Block", block);
                return result;
            }
        }

        /// <summary>
        /// Export each block as a SimaticML file. A failing block does not stop the remaining ones.
        /// </summary>
        public DocumentBatchResult ExportBlocksAsXml(IEnumerable<(PlcBlock Block, FileInfo File, string GroupPath)> items, ExportOptions options, bool overwrite)
        {
            var results = new List<DocumentOperationResult>();
            foreach (var item in items)
            {
                results.Add(ExportBlockAsXml(item.Block, item.File, item.GroupPath, options, overwrite));
            }
            return DocumentBatchResult.From(results);
        }

        /// <summary>
        /// Export a PLC data type as a SimaticML file. Failures are reported in the result instead of being thrown.
        /// </summary>
        public DocumentOperationResult ExportTypeAsXml(PlcType type, FileInfo targetFile, string groupPath, ExportOptions options, bool overwrite)
        {
            var name = type.Name;
            var prepareError = PrepareXmlExportTarget(targetFile, overwrite);
            if (prepareError != null)
            {
                return CreateXmlResult(name, groupPath, targetFile, DocumentOperationResult.StateFailure, prepareError);
            }

            try
            {
                type.Export(targetFile, options);
                return CreateXmlResult(name, groupPath, targetFile, DocumentOperationResult.StateSuccess, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to export PLC data type {Name} as SimaticML to {Path}", name, targetFile.FullName);
                var result = CreateXmlResult(name, groupPath, targetFile, DocumentOperationResult.StateFailure, ex.Message);
                AddExportHints(result, "PLC data type", type);
                return result;
            }
        }

        /// <summary>
        /// Export each PLC data type as a SimaticML file. A failing type does not stop the remaining ones.
        /// </summary>
        public DocumentBatchResult ExportTypesAsXml(IEnumerable<(PlcType Type, FileInfo File, string GroupPath)> items, ExportOptions options, bool overwrite)
        {
            var results = new List<DocumentOperationResult>();
            foreach (var item in items)
            {
                results.Add(ExportTypeAsXml(item.Type, item.File, item.GroupPath, options, overwrite));
            }
            return DocumentBatchResult.From(results);
        }

        /// <summary>
        /// Import the blocks of a SimaticML file into the given block group. Failures are reported in the result instead of being thrown.
        /// </summary>
        public DocumentOperationResult ImportBlocksFromXml(PlcBlockGroup blockGroup, XmlImportPlanItem item, ImportOptions options, SWImportOptions swOptions)
        {
            try
            {
                var imported = blockGroup.Blocks.Import(item.File, options, swOptions);
                var result = XmlImportPlanner.CreateResult(item, DocumentOperationResult.StateSuccess, $"Imported into '{DisplayGroupPath(item.GroupPath)}'");
                result.Blocks = imported.Select(block => CreateBlockInfo(block)).ToList();
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import blocks from {Path}", item.File.FullName);
                return XmlImportPlanner.CreateResult(item, DocumentOperationResult.StateFailure, ex.Message);
            }
        }

        /// <summary>
        /// Import the blocks of several SimaticML files. Files that fail are retried while another pass imports
        /// at least one more file, so files that depend on each other (an FB and its instance DB) can come in any order.
        /// Results are returned in the order of <paramref name="items"/>.
        /// </summary>
        public IList<DocumentOperationResult> ImportBlocksFromXmlFiles(IList<(PlcBlockGroup Group, XmlImportPlanItem Item)> items, ImportOptions options, SWImportOptions swOptions)
        {
            var results = new DocumentOperationResult[items.Count];
            var pending = Enumerable.Range(0, items.Count).ToList();
            for (var pass = 1; pending.Count > 0; pass++)
            {
                var failed = new List<int>();
                foreach (var index in pending)
                {
                    results[index] = ImportBlocksFromXml(items[index].Group, items[index].Item, options, swOptions);
                    if (results[index].IsFailure)
                    {
                        failed.Add(index);
                    }
                    else if (pass > 1)
                    {
                        results[index].Messages.Add($"Imported on pass {pass} after files it depends on");
                    }
                }

                if (failed.Count == pending.Count)
                {
                    break;
                }
                pending = failed;
            }
            return results;
        }

        /// <summary>
        /// Import the PLC data types of a SimaticML file into the given type group. Failures are reported in the result instead of being thrown.
        /// </summary>
        public DocumentOperationResult ImportTypesFromXml(PlcTypeGroup typeGroup, XmlImportPlanItem item, ImportOptions options, SWImportOptions swOptions)
        {
            try
            {
                var imported = typeGroup.Types.Import(item.File, options, swOptions);
                var result = XmlImportPlanner.CreateResult(item, DocumentOperationResult.StateSuccess, $"Imported into '{DisplayGroupPath(item.GroupPath)}'");
                result.Types = imported.Select(type => new PlcTypeInfo { Name = type.Name, Namespace = type.Namespace }).ToList();
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import PLC data types from {Path}", item.File.FullName);
                return XmlImportPlanner.CreateResult(item, DocumentOperationResult.StateFailure, ex.Message);
            }
        }

        /// <summary>
        /// Import the PLC data types of several SimaticML files, retrying failed files while another pass makes progress,
        /// so types that use each other can come in any order. Results are returned in the order of <paramref name="items"/>.
        /// </summary>
        public IList<DocumentOperationResult> ImportTypesFromXmlFiles(IList<(PlcTypeGroup Group, XmlImportPlanItem Item)> items, ImportOptions options, SWImportOptions swOptions)
        {
            var results = new DocumentOperationResult[items.Count];
            var pending = Enumerable.Range(0, items.Count).ToList();
            for (var pass = 1; pending.Count > 0; pass++)
            {
                var failed = new List<int>();
                foreach (var index in pending)
                {
                    results[index] = ImportTypesFromXml(items[index].Group, items[index].Item, options, swOptions);
                    if (results[index].IsFailure)
                    {
                        failed.Add(index);
                    }
                    else if (pass > 1)
                    {
                        results[index].Messages.Add($"Imported on pass {pass} after files it depends on");
                    }
                }

                if (failed.Count == pending.Count)
                {
                    break;
                }
                pending = failed;
            }
            return results;
        }

        /// <summary>
        /// Creates the target directory and applies the overwrite rule. Returns an error message, or null when the export can go ahead.
        /// </summary>
        private string? PrepareXmlExportTarget(FileInfo targetFile, bool overwrite)
        {
            try
            {
                targetFile.Directory?.Create();
                if (targetFile.Exists)
                {
                    if (!overwrite)
                    {
                        return $"File '{targetFile.FullName}' already exists. Pass overwrite=true to replace it";
                    }

                    targetFile.Delete();
                    targetFile.Refresh();
                }

                return null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Cannot prepare export target {Path}", targetFile.FullName);
                return $"Cannot write '{targetFile.FullName}': {ex.Message}";
            }
        }

        private void AddExportHints(DocumentOperationResult result, string objectKind, IEngineeringObject exportedObject)
        {
            try
            {
                if (exportedObject.GetAttribute("IsConsistent") is bool isConsistent && !isConsistent)
                {
                    result.Messages.Add($"{objectKind} is inconsistent; compile it and export again");
                }

                if (exportedObject.GetAttribute("IsKnowHowProtected") is bool isKnowHowProtected && isKnowHowProtected)
                {
                    result.Messages.Add($"{objectKind} is know-how protected");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not add export hints for {Name}", result.Name);
            }
        }

        private static DocumentOperationResult CreateXmlResult(string name, string groupPath, FileInfo file, string state, string? message)
        {
            var result = new DocumentOperationResult
            {
                Name = name,
                State = state,
                GroupPath = groupPath,
                Files = new List<string> { file.FullName }
            };
            if (message != null)
            {
                result.Messages.Add(message);
            }
            return result;
        }

        private static string DisplayGroupPath(string groupPath)
        {
            return string.IsNullOrEmpty(groupPath) ? "(root)" : groupPath;
        }

        #endregion

        #region Loadable Files

        /// <summary>
        /// Generate loadable file for blocks
        /// </summary>
        public ToolResponse<string> GenerateLoadableFile(PlcSoftware plcSoftware, FileInfo filePath, IEnumerable<PlcBlock> blocks, TargetOption targetOption, bool dryRun = false)
        {
            try
            {
                var provider = plcSoftware.GetService<LoadableProvider>();
                if (provider == null)
                {
                    return ToolResponse<string>.CreateError(ErrorCodes.TiaError, "Loadable provider not available for this PLC");
                }

                if (!dryRun)
                {
                    provider.GenerateLoadable(filePath, blocks, targetOption);
                }
                return ToolResponse<string>.CreateSuccess($"Loadable file would be generated at {filePath.FullName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate loadable file");
                return ToolResponse<string>.CreateError(ErrorCodes.TiaError, "Failed to generate loadable file", ex.Message);
            }
        }

        #endregion
    }

    /// <summary>
    /// ProDiag attributes model
    /// </summary>
    public class ProDiagAttributes
    {
        public string? ProDiagVersion { get; set; }
        public bool InitialValueAcquisition { get; set; }
        public bool UseCentralTimeStamp { get; set; }
    }
}