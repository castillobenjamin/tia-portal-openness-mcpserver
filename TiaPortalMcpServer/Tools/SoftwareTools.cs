using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Units;
using TiaPortalMcpServer.Models;
using TiaPortalMcpServer.Services;

namespace TiaPortalMcpServer
{
    [McpServerToolType]
    public class SoftwareTools
    {
        private readonly ILogger<SoftwareTools> _logger;
        private readonly TiaPortalSessionManager _sessionManager;

        public SoftwareTools(
            ILogger<SoftwareTools> logger,
            TiaPortalSessionManager sessionManager)
        {
            _logger = logger;
            _sessionManager = sessionManager;
        }

        [McpServerTool(Destructive = true, OpenWorld = true, Name = "software_add_block"), Description("Create a new PLC block (FB, FC, DB, OB) in a device's PLC software with specified name or number. Supports Organization Blocks, Function Blocks, Functions, and Data Blocks. Returns block metadata. Prerequisites: Project must be open, device must have PLC software, block name must be unique. Use this for programmatic block creation during project generation or template instantiation.")]
        public CallToolResult software_add_block(
            [Description("Device name")] string deviceName,
            [Description("Block type (FB, FC, DB, OB)")] string blockType,
            [Description("Block name or number")] string blockName,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("software_add_block called with deviceName='{DeviceName}', blockType='{BlockType}', blockName='{BlockName}'", deviceName, blockType, blockName);

            try
            {
                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(
                        ToolResponse<object>.CreateError(
                            ErrorCodes.NoProject,
                            "No project is currently open. Use projects_open first."
                        )
                    );
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<object>.CreateError(scopeErrorCode, scopeError));
                }

                var blockGroup = plcScope.BlockGroup;
                if (blockGroup == null)
                {
                    return McpToolResults.From(
                        ToolResponse<object>.CreateError(
                            ErrorCodes.TiaError,
                            "Block group not accessible"
                        )
                    );
                }

                // Check if block already exists
                var existingBlock = blockGroup.Blocks.FirstOrDefault(b => b.Name == blockName);
                if (existingBlock != null)
                {
                    return McpToolResults.From(
                        ToolResponse<object>.CreateError(
                            ErrorCodes.BlockExists,
                            $"Block '{blockName}' already exists"
                        )
                    );
                }

                // Create the block based on type
                PlcBlock? newBlock = CreatePlcBlock(blockGroup.Blocks, blockType, blockName);
                if (newBlock == null)
                {
                    return McpToolResults.From(
                        ToolResponse<object>.CreateError(
                            ErrorCodes.InvalidParameter,
                            $"Unable to create block type '{blockType}'. Check TIA Portal API support for this block type."
                        )
                    );
                }

                _logger.LogInformation("Block '{BlockName}' of type '{BlockType}' created successfully", blockName, blockType);

                try
                {
                    _sessionManager.SaveCurrentProject();
                    _logger.LogInformation("Project saved after block creation");
                }
                catch (Exception saveEx)
                {
                    _logger.LogWarning(saveEx, "Failed to save project after block creation");
                }

                return McpToolResults.From(
                    ToolResponse<object>.CreateSuccess(new
                    {
                        blockName = newBlock?.Name,
                        blockType = blockType,
                        deviceName = deviceName,
                        message = $"Block '{blockName}' created successfully"
                    })
                );
            }
            catch (COMException comEx)
            {
                _logger.LogError(comEx, "COM error adding block '{BlockName}'", blockName);
                return McpToolResults.From(
                    ToolResponse<object>.CreateError(
                        ErrorCodes.ComError,
                        $"COM error adding block: {comEx.Message}",
                        comEx.ToString()
                    )
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding block '{BlockName}'", blockName);
                return McpToolResults.From(
                    ToolResponse<object>.CreateError(
                        ErrorCodes.TiaError,
                        $"Error adding block: {ex.Message}",
                        ex.ToString()
                    )
                );
            }
        }

        [McpServerTool(ReadOnly = true, Idempotent = true, Name = "blocks_list"), Description("Enumerate all PLC blocks (OB, FB, FC, DB) in a specific device's software. Returns block list with names, types, programming languages, and modification timestamps. Prerequisites: Project must be open, device must exist. Use this to discover existing blocks before editing, for block inventory, or to verify block structure. Named blocks_list for backward compatibility; consider using software_blocks_list pattern.")]
        public CallToolResult blocks_list(
            [Description("Device name")] string deviceName,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("blocks_list called with deviceName='{DeviceName}', unitName='{UnitName}'", deviceName, unitName);

            try
            {
                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(
                        ToolResponse<object>.CreateError(
                            ErrorCodes.NoProject,
                            "No project is currently open. Use projects_open first."
                        )
                    );
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<object>.CreateError(scopeErrorCode, scopeError));
                }

                var blocks = _sessionManager.PortalService.GetBlocks(plcScope)
                    .Select(block => new
                    {
                        name = block.Name,
                        type = block.GetType().Name,
                        programmingLanguage = (block as IEngineeringObject)?.GetAttribute("ProgrammingLanguage")?.ToString(),
                        autoNumber = block.AutoNumber,
                        creationDate = block.CreationDate,
                        modifiedDate = block.ModifiedDate,
                        codeModifiedDate = block.CodeModifiedDate,
                        compileDate = block.CompileDate,
                    })
                    .Cast<object>()
                    .ToList();

                _logger.LogInformation("Found {Count} blocks in '{Scope}'", blocks?.Count ?? 0, plcScope.DisplayName);

                return McpToolResults.From(
                    ToolResponse<object>.CreateSuccess(new
                    {
                        deviceName = deviceName,
                        unitName = plcScope.Unit?.Name,
                        blockCount = blocks?.Count ?? 0,
                        blocks = blocks ?? new List<object>(),
                    })
                );
            }
            catch (COMException comEx)
            {
                _logger.LogError(comEx, "COM error listing blocks for device '{DeviceName}'", deviceName);
                return McpToolResults.From(
                    ToolResponse<object>.CreateError(
                        ErrorCodes.ComError,
                        $"COM error listing blocks: {comEx.Message}",
                        comEx.ToString()
                    )
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing blocks for device '{DeviceName}'", deviceName);
                return McpToolResults.From(
                    ToolResponse<object>.CreateError(
                        ErrorCodes.TiaError,
                        $"Error listing blocks: {ex.Message}",
                        ex.ToString()
                    )
                );
            }
        }

        [McpServerTool(ReadOnly = true, Idempotent = true, Name = "software_get_block_hierarchy"), Description("Retrieve hierarchical structure of block groups and blocks within a device's PLC software including user-defined folders and system blocks. Returns nested group structure with optional block details. Prerequisites: Project must be open, device must have PLC software. Set includeBlocks=false for faster group-only enumeration. Use this for project documentation, navigation UIs, or block organization analysis.")]
        public CallToolResult software_get_block_hierarchy(
            [Description("Device name")] string deviceName,
            [Description("Include blocks in response (default: true)")] bool includeBlocks = true,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "")
        {
            _logger.LogInformation("software_get_block_hierarchy called with deviceName='{DeviceName}', includeBlocks={IncludeBlocks}", deviceName, includeBlocks);

            try
            {
                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(
                        ToolResponse<object>.CreateError(
                            ErrorCodes.NoProject,
                            "No project is currently open. Use projects_open first."
                        )
                    );
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<object>.CreateError(scopeErrorCode, scopeError));
                }

                var blockGroup = plcScope.BlockGroup;
                if (blockGroup == null)
                {
                    return McpToolResults.From(
                        ToolResponse<object>.CreateError(
                            ErrorCodes.TiaError,
                            "Block group not accessible"
                        )
                    );
                }

                // Get all blocks at root level
                var rootBlocks = includeBlocks
                    ? EnumerateBlocks(blockGroup.Blocks)
                    : new System.Collections.Generic.List<object>();

                // Get all user-defined groups recursively
                var userGroups = new System.Collections.Generic.List<object>();
                foreach (PlcBlockUserGroup blockUserGroup in blockGroup.Groups)
                {
                    userGroups.Add(EnumerateBlockUserGroup(blockUserGroup, includeBlocks));
                }

                var totalBlockCount = CountAllBlocks(blockGroup);

                _logger.LogInformation("Found {GroupCount} user groups and {BlockCount} total blocks in device '{DeviceName}'",
                    userGroups.Count, totalBlockCount, deviceName);

                return McpToolResults.From(
                    ToolResponse<object>.CreateSuccess(new
                    {
                        deviceName = deviceName,
                        unitName = plcScope.Unit?.Name,
                        totalBlockCount = totalBlockCount,
                        rootBlocks = rootBlocks,
                        userGroups = userGroups,
                        message = $"Retrieved block hierarchy with {userGroups.Count} user groups and {totalBlockCount} blocks"
                    })
                );
            }
            catch (COMException comEx)
            {
                _logger.LogError(comEx, "COM error getting block hierarchy for device '{DeviceName}'", deviceName);
                return McpToolResults.From(
                    ToolResponse<object>.CreateError(
                        ErrorCodes.ComError,
                        $"COM error getting block hierarchy: {comEx.Message}",
                        comEx.ToString()
                    )
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting block hierarchy for device '{DeviceName}'", deviceName);
                return McpToolResults.From(
                    ToolResponse<object>.CreateError(
                        ErrorCodes.TiaError,
                        $"Error getting block hierarchy: {ex.Message}",
                        ex.ToString()
                    )
                );
            }
        }

        [McpServerTool(ReadOnly = true, Idempotent = true, Name = "software_units_list"), Description("List the Software Units (standard and safety) of a device's PLC software, with namespace, author and unit relations. Returns unitsSupported=false for CPUs without Software Unit support. Prerequisites: Project must be open, device must have PLC software. Pass a returned unit name as unitName to blocks, tags and software tools to work inside that unit.")]
        public CallToolResult software_units_list(
            [Description("Device name")] string deviceName)
        {
            _logger.LogInformation("software_units_list called with deviceName='{DeviceName}'", deviceName);

            try
            {
                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(
                        ToolResponse<object>.CreateError(
                            ErrorCodes.NoProject,
                            "No project is currently open. Use projects_open first."
                        )
                    );
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, null, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<object>.CreateError(scopeErrorCode, scopeError));
                }

                var units = _sessionManager.PortalService.GetSoftwareUnits(plcScope.Software);
                var unitInfos = (units ?? new List<PlcUnitBase>())
                    .Select(unit => new
                    {
                        name = unit.Name,
                        path = PlcSoftwarePathInfo.FormatPath(deviceName, unit.Name),
                        kind = unit is PlcSafetyUnit ? "Safety" : "Standard",
                        namespacePreset = unit.NamespacePreset,
                        author = unit.Author,
                        relations = unit.Relations
                            .Select(relation => new
                            {
                                relatedObject = relation.RelatedObject,
                                relationType = relation.RelationType.ToString()
                            })
                            .Cast<object>()
                            .ToList()
                    })
                    .Cast<object>()
                    .ToList();

                _logger.LogInformation("Found {Count} Software Units in device '{DeviceName}'", unitInfos.Count, deviceName);

                return McpToolResults.From(
                    ToolResponse<object>.CreateSuccess(new
                    {
                        deviceName = deviceName,
                        unitsSupported = units != null,
                        unitCount = unitInfos.Count,
                        units = unitInfos
                    })
                );
            }
            catch (COMException comEx)
            {
                _logger.LogError(comEx, "COM error listing Software Units for device '{DeviceName}'", deviceName);
                return McpToolResults.From(
                    ToolResponse<object>.CreateError(
                        ErrorCodes.ComError,
                        $"COM error listing Software Units: {comEx.Message}",
                        comEx.ToString()
                    )
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing Software Units for device '{DeviceName}'", deviceName);
                return McpToolResults.From(
                    ToolResponse<object>.CreateError(
                        ErrorCodes.TiaError,
                        $"Error listing Software Units: {ex.Message}",
                        ex.ToString()
                    )
                );
            }
        }

        [McpServerTool(ReadOnly = true, Idempotent = true, Name = "software_paths_list"), Description("List every PLC software in the open project, including devices in device folders, each followed by its Software Units (standard and safety). Each entry has a path ('Device' or 'Device/Unit'), the deviceName and unitName to pass to blocks, tags, software and exchange tools, the kind ('PlcSoftware', 'SoftwareUnit', 'SafetyUnit'), the PLC software name and the device folder. Prerequisites: Project must be open. Use this first to find out which PLCs and units a project has.")]
        public CallToolResult software_paths_list()
        {
            _logger.LogInformation("software_paths_list called");

            try
            {
                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<object>.CreateError(ErrorCodes.NoProject, "No project is currently open. Use projects_open first."));
                }

                var paths = _sessionManager.PortalService.GetPlcSoftwarePaths(project);
                _logger.LogInformation("Found {Count} PLC software paths", paths.Count);

                return McpToolResults.From(ToolResponse<object>.CreateSuccess(new
                {
                    count = paths.Count,
                    items = paths
                }));
            }
            catch (COMException comEx)
            {
                _logger.LogError(comEx, "COM error listing PLC software paths");
                return McpToolResults.From(ToolResponse<object>.CreateError(ErrorCodes.ComError, $"COM error listing PLC software paths: {comEx.Message}", comEx.ToString()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing PLC software paths");
                return McpToolResults.From(ToolResponse<object>.CreateError(ErrorCodes.TiaError, $"Error listing PLC software paths: {ex.Message}", ex.ToString()));
            }
        }

        [McpServerTool(ReadOnly = true, Idempotent = true, Name = "software_tree_get"), Description("Get the project tree of a PLC software or Software Unit: program blocks, PLC data types, PLC tag tables and external sources, each as nested groups with their objects (name, kind such as FB/FC/OB/GlobalDB/PlcStruct, number, programming language). Every group has its path (e.g. 'Motion/Axis', the groupName the exchange tools take) and the total number of objects below it. For the main PLC software of a CPU with Software Units, also lists the units. Prerequisites: Project must be open, device must have PLC software. Set includeItems=false for a smaller, groups-only answer on large projects.")]
        public CallToolResult software_tree_get(
            [Description("Device name")] string deviceName,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "",
            [Description("Include the program blocks section")] bool includeBlocks = true,
            [Description("Include the PLC data types section")] bool includeTypes = true,
            [Description("Include the PLC tags section (tag tables)")] bool includeTagTables = true,
            [Description("Include the external sources section")] bool includeExternalSources = true,
            [Description("Include the objects in each group. If false, only groups with their paths and counts are returned")] bool includeItems = true)
        {
            _logger.LogInformation("software_tree_get called with deviceName='{DeviceName}', unitName='{UnitName}', includeItems={IncludeItems}", deviceName, unitName, includeItems);

            try
            {
                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<SoftwareTree>.CreateError(ErrorCodes.NoProject, "No project is currently open. Use projects_open first."));
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<SoftwareTree>.CreateError(scopeErrorCode, scopeError));
                }

                var tree = SoftwareTreeBuilder.Build(plcScope, includeBlocks, includeTypes, includeTagTables, includeExternalSources, includeItems);
                if (plcScope.Unit == null)
                {
                    var units = _sessionManager.PortalService.GetSoftwareUnits(plcScope.Software);
                    if (units != null && units.Count > 0)
                    {
                        var deviceGroupPath = _sessionManager.PortalService.GetDevicesWithGroupPaths(project)
                            .Where(d => d.Device.Name == deviceName)
                            .Select(d => d.GroupPath)
                            .FirstOrDefault() ?? string.Empty;
                        tree.Units = _sessionManager.PortalService.GetUnitPaths(deviceName, deviceGroupPath, plcScope.Software, units);
                    }
                }

                return McpToolResults.From(ToolResponse<SoftwareTree>.CreateSuccess(tree));
            }
            catch (COMException comEx)
            {
                _logger.LogError(comEx, "COM error getting the software tree of '{DeviceName}'", deviceName);
                return McpToolResults.From(ToolResponse<SoftwareTree>.CreateError(ErrorCodes.ComError, $"COM error getting the software tree: {comEx.Message}", comEx.ToString()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting the software tree of '{DeviceName}'", deviceName);
                return McpToolResults.From(ToolResponse<SoftwareTree>.CreateError(ErrorCodes.TiaError, $"Error getting the software tree: {ex.Message}", ex.ToString()));
            }
        }

        /// <summary>
        /// Creates a PLC block with the specified type and name using reflection.
        /// This approach is necessary due to varying TIA Portal API signatures across versions.
        /// </summary>
        private static PlcBlock? CreatePlcBlock(PlcBlockComposition blockComposition, string blockType, string blockName)
        {
            if (blockComposition == null)
            {
                return null;
            }

            var methodName = blockType.ToUpper() switch
            {
                "FB" => "CreateFB",
                "FC" => "CreateFC",
                "DB" => "CreateDB",
                "OB" => "CreateOB",
                _ => string.Empty
            };

            if (string.IsNullOrEmpty(methodName))
            {
                return null;
            }

            var methods = blockComposition.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == methodName)
                .ToList();

            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                try
                {
                    // Try different parameter combinations that were working before
                    if (parameters.Length == 1)
                    {
                        // Simple case: CreateX(name)
                        return method.Invoke(blockComposition, new object[] { blockName }) as PlcBlock;
                    }

                    if (parameters.Length == 2 && int.TryParse(blockName, out var number))
                    {
                        // CreateX(name, number)
                        return method.Invoke(blockComposition, new object[] { blockName, number }) as PlcBlock;
                    }

                    if (parameters.Length >= 3)
                    {
                        // Try to provide minimal defaults for required parameters
                        var args = new object?[parameters.Length];
                        args[0] = blockName;

                        // Fill in default values for other parameters
                        for (var i = 1; i < parameters.Length; i++)
                        {
                            var paramType = parameters[i].ParameterType;
                            if (paramType == typeof(bool))
                            {
                                args[i] = false; // isAutoNumbered = false
                            }
                            else if (paramType == typeof(int))
                            {
                                args[i] = 0; // number = 0 or default
                            }
                            else if (paramType.IsEnum)
                            {
                                // For enums like ProgrammingLanguage, use first/default value
                                args[i] = Enum.GetValues(paramType).GetValue(0);
                            }
                            else if (paramType.IsValueType)
                            {
                                args[i] = Activator.CreateInstance(paramType);
                            }
                            else
                            {
                                args[i] = null;
                            }
                        }

                        return method.Invoke(blockComposition, args) as PlcBlock;
                    }
                }
                catch (Exception)
                {
                    // If this method signature fails, try the next one
                    continue;
                }
            }

            return null;
        }

        /// <summary>
        /// Recursively enumerates a block user group and its subgroups
        /// </summary>
        private static object EnumerateBlockUserGroup(PlcBlockUserGroup blockUserGroup, bool includeBlocks)
        {
            var subGroups = new System.Collections.Generic.List<object>();
            foreach (PlcBlockUserGroup subBlockUserGroup in blockUserGroup.Groups)
            {
                subGroups.Add(EnumerateBlockUserGroup(subBlockUserGroup, includeBlocks));
            }

            var blocks = includeBlocks
                ? EnumerateBlocks(blockUserGroup.Blocks)
                : new System.Collections.Generic.List<object>();

            return new
            {
                name = blockUserGroup.Name,
                blockCount = blockUserGroup.Blocks.Count,
                subGroupCount = blockUserGroup.Groups.Count,
                blocks = blocks,
                subGroups = subGroups
            };
        }

        /// <summary>
        /// Enumerates all blocks in a block composition
        /// </summary>
        private static System.Collections.Generic.List<object> EnumerateBlocks(PlcBlockComposition blockComposition)
        {
            var blocks = new System.Collections.Generic.List<object>();
            foreach (PlcBlock block in blockComposition)
            {
                blocks.Add(new
                {
                    name = block.Name,
                    type = block.GetType().Name,
                    programmingLanguage = (block as IEngineeringObject)?.GetAttribute("ProgrammingLanguage")?.ToString()
                });
            }
            return blocks;
        }

        /// <summary>
        /// Counts all blocks in a block group including subgroups
        /// </summary>
        private static int CountAllBlocks(PlcBlockGroup blockGroup)
        {
            var count = blockGroup.Blocks.Count;
            foreach (PlcBlockUserGroup userGroup in blockGroup.Groups)
            {
                count += CountBlocksInUserGroup(userGroup);
            }
            return count;
        }

        /// <summary>
        /// Recursively counts blocks in a user group and its subgroups
        /// </summary>
        private static int CountBlocksInUserGroup(PlcBlockUserGroup userGroup)
        {
            var count = userGroup.Blocks.Count;
            foreach (PlcBlockUserGroup subGroup in userGroup.Groups)
            {
                count += CountBlocksInUserGroup(subGroup);
            }
            return count;
        }
    }
}
