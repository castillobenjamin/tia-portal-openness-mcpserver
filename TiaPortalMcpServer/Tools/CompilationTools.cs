using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using TiaPortalMcpServer.Models;
using TiaPortalMcpServer.Services;

namespace TiaPortalMcpServer
{
    [McpServerToolType]
    public class CompilationTools
    {
        private const string MessageFilterDescription = "Which compiler messages to return: 'Errors', 'ErrorsAndWarnings' (default) or 'All' (also information and success messages)";
        private const string MaxMessagesDescription = "Maximum number of messages to return; 0 for no limit. matchedMessageCount and truncated tell whether more matched";
        private const int DefaultMaxMessages = 200;

        private readonly ILogger<CompilationTools> _logger;

        private readonly TiaPortalSessionManager _sessionManager;

        public CompilationTools(
            ILogger<CompilationTools> logger,
            TiaPortalSessionManager sessionManager)
        {
            _logger = logger;
            _sessionManager = sessionManager;
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Compile the entire TIA Portal project including all devices, PLCs, and HMI targets. Returns the overall state (Success/Information/Warning/Error), error and warning counts, and each compiler message with the path of the object it belongs to (e.g. 'MSPLC_M2/Program blocks/Motion/Axis_FB'), its location (e.g. 'Network 2') and description. Prerequisites: Project must be open. Note: Compilation can be time-consuming for large projects; prefer compilation_software or compilation_block while iterating.")]
        public CallToolResult compilation_project(
            [Description(MessageFilterDescription)] string messageFilter = CompileReport.FilterErrorsAndWarnings,
            [Description(MaxMessagesDescription)] int maxMessages = DefaultMaxMessages)
        {
            _logger.LogInformation("compilation_project called with messageFilter='{MessageFilter}', maxMessages={MaxMessages}", messageFilter, maxMessages);

            try
            {
                if (!TryValidateOptions(messageFilter, maxMessages, out var filter, out var optionError))
                {
                    return McpToolResults.From(ToolResponse<CompileReport>.CreateError(ErrorCodes.InvalidParameter, optionError));
                }

                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<CompileReport>.CreateError(ErrorCodes.NoProject, "No project is currently open. Use projects_open first."));
                }

                _logger.LogInformation("Compiling project '{ProjectName}'", project.Name);
                return Compile(project.GetService<ICompilable>(), $"project '{project.Name}'", filter, maxMessages,
                    "Compile service not available for the project");
            }
            catch (COMException comEx)
            {
                _logger.LogError(comEx, "COM error compiling project");
                return McpToolResults.From(ToolResponse<CompileReport>.CreateError(ErrorCodes.ComError, $"COM error compiling project: {comEx.Message}", comEx.ToString()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error compiling project");
                return McpToolResults.From(ToolResponse<CompileReport>.CreateError(ErrorCodes.CompilationError, $"Error compiling project: {ex.Message}", ex.ToString()));
            }
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Compile the PLC software of a device, or a single Software Unit when unitName is given. Returns the overall state, error and warning counts, and each compiler message with the path of the object it belongs to (e.g. 'MSPLC_M2/Program blocks/Motion/Axis_FB'), its location (e.g. 'Network 2') and description. Prerequisites: Project must be open, device must have PLC software. Faster than full project compilation for iterative development.")]
        public CallToolResult compilation_software(
            [Description("Device name")] string deviceName,
            [Description("Software Unit name. Leave empty to compile the device's whole PLC software.")] string unitName = "",
            [Description(MessageFilterDescription)] string messageFilter = CompileReport.FilterErrorsAndWarnings,
            [Description(MaxMessagesDescription)] int maxMessages = DefaultMaxMessages)
        {
            _logger.LogInformation("compilation_software called with deviceName='{DeviceName}', unitName='{UnitName}', messageFilter='{MessageFilter}'", deviceName, unitName, messageFilter);

            try
            {
                if (!TryValidateOptions(messageFilter, maxMessages, out var filter, out var optionError))
                {
                    return McpToolResults.From(ToolResponse<CompileReport>.CreateError(ErrorCodes.InvalidParameter, optionError));
                }

                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<CompileReport>.CreateError(ErrorCodes.NoProject, "No project is currently open. Use projects_open first."));
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<CompileReport>.CreateError(scopeErrorCode, scopeError));
                }

                _logger.LogInformation("Compiling PLC software '{Scope}'", plcScope.DisplayName);
                var compilable = plcScope.Unit != null
                    ? plcScope.Unit.GetService<ICompilable>()
                    : plcScope.Software.GetService<ICompilable>();
                var unavailable = plcScope.Unit != null
                    ? $"Software Unit '{plcScope.Unit.Name}' cannot be compiled on its own; compile the device's whole PLC software instead"
                    : $"Compile service not available for the PLC software of '{deviceName}'";
                return Compile(compilable, plcScope.DisplayName, filter, maxMessages, unavailable);
            }
            catch (COMException comEx)
            {
                _logger.LogError(comEx, "COM error compiling software for device '{DeviceName}'", deviceName);
                return McpToolResults.From(ToolResponse<CompileReport>.CreateError(ErrorCodes.ComError, $"COM error compiling software: {comEx.Message}", comEx.ToString()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error compiling software for device '{DeviceName}'", deviceName);
                return McpToolResults.From(ToolResponse<CompileReport>.CreateError(ErrorCodes.CompilationError, $"Error compiling software: {ex.Message}", ex.ToString()));
            }
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Compile a single PLC block. The block is searched in all block groups. Returns the overall state, error and warning counts, and each compiler message with its object path, location (e.g. 'Network 2') and description. Prerequisites: Project must be open, device must have PLC software, block must exist. Use this for the fastest feedback after changing or importing one block.")]
        public CallToolResult compilation_block(
            [Description("Device name")] string deviceName,
            [Description("Block name")] string blockName,
            [Description("Software Unit name. Leave empty for the device's main PLC software.")] string unitName = "",
            [Description(MessageFilterDescription)] string messageFilter = CompileReport.FilterErrorsAndWarnings,
            [Description(MaxMessagesDescription)] int maxMessages = DefaultMaxMessages)
        {
            _logger.LogInformation("compilation_block called with deviceName='{DeviceName}', blockName='{BlockName}', unitName='{UnitName}'", deviceName, blockName, unitName);

            try
            {
                if (!TryValidateOptions(messageFilter, maxMessages, out var filter, out var optionError))
                {
                    return McpToolResults.From(ToolResponse<CompileReport>.CreateError(ErrorCodes.InvalidParameter, optionError));
                }

                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<CompileReport>.CreateError(ErrorCodes.NoProject, "No project is currently open. Use projects_open first."));
                }

                var plcScope = _sessionManager.PortalService.ResolvePlcScope(project, deviceName, unitName, out var scopeErrorCode, out var scopeError);
                if (plcScope == null)
                {
                    return McpToolResults.From(ToolResponse<CompileReport>.CreateError(scopeErrorCode, scopeError));
                }

                var found = _sessionManager.PortalService.FindBlockWithGroupPath(plcScope.BlockGroup, blockName);
                if (found.Block == null)
                {
                    return McpToolResults.From(ToolResponse<CompileReport>.CreateError(ErrorCodes.BlockNotFound, $"Block '{blockName}' not found in '{plcScope.DisplayName}'"));
                }

                var blockPath = ExchangePaths.CombineGroupPath(found.GroupPath, found.Block.Name);
                return Compile(found.Block.GetService<ICompilable>(), $"block '{blockPath}' in {plcScope.DisplayName}", filter, maxMessages,
                    $"Block '{blockName}' cannot be compiled on its own");
            }
            catch (COMException comEx)
            {
                _logger.LogError(comEx, "COM error compiling block '{BlockName}'", blockName);
                return McpToolResults.From(ToolResponse<CompileReport>.CreateError(ErrorCodes.ComError, $"COM error compiling block: {comEx.Message}", comEx.ToString()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error compiling block '{BlockName}'", blockName);
                return McpToolResults.From(ToolResponse<CompileReport>.CreateError(ErrorCodes.CompilationError, $"Error compiling block: {ex.Message}", ex.ToString()));
            }
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Compile the HMI software of a device, such as a WinCC Unified panel or PC runtime. Returns the overall state, error and warning counts, and each compiler message with its object path and description. Prerequisites: Project must be open, device must have HMI software.")]
        public CallToolResult compilation_hmi(
            [Description("HMI device name")] string deviceName,
            [Description(MessageFilterDescription)] string messageFilter = CompileReport.FilterErrorsAndWarnings,
            [Description(MaxMessagesDescription)] int maxMessages = DefaultMaxMessages)
        {
            _logger.LogInformation("compilation_hmi called with deviceName='{DeviceName}'", deviceName);

            try
            {
                if (!TryValidateOptions(messageFilter, maxMessages, out var filter, out var optionError))
                {
                    return McpToolResults.From(ToolResponse<CompileReport>.CreateError(ErrorCodes.InvalidParameter, optionError));
                }

                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<CompileReport>.CreateError(ErrorCodes.NoProject, "No project is currently open. Use projects_open first."));
                }

                var device = _sessionManager.PortalService.FindDevice(project, deviceName);
                if (device == null)
                {
                    return McpToolResults.From(ToolResponse<CompileReport>.CreateError(ErrorCodes.DeviceNotFound, $"Device '{deviceName}' not found"));
                }

                var found = HmiUnifiedService.FindSoftwareItem(device, s => !(s is Siemens.Engineering.SW.PlcSoftware));
                if (found == null)
                {
                    return McpToolResults.From(ToolResponse<CompileReport>.CreateError(ErrorCodes.TiaError, $"Device '{deviceName}' does not have HMI software"));
                }

                // HMI software types differ in where they offer compilation: on the software itself or on the hosting device item
                var compilable = (found.Value.Software as IEngineeringServiceProvider)?.GetService<ICompilable>()
                    ?? found.Value.Item.GetService<ICompilable>();
                return Compile(compilable, $"HMI software of '{deviceName}'", filter, maxMessages,
                    $"Compile service not available for the HMI software of '{deviceName}'");
            }
            catch (COMException comEx)
            {
                _logger.LogError(comEx, "COM error compiling HMI software of '{DeviceName}'", deviceName);
                return McpToolResults.From(ToolResponse<CompileReport>.CreateError(ErrorCodes.ComError, $"COM error compiling HMI software: {comEx.Message}", comEx.ToString()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error compiling HMI software of '{DeviceName}'", deviceName);
                return McpToolResults.From(ToolResponse<CompileReport>.CreateError(ErrorCodes.CompilationError, $"Error compiling HMI software: {ex.Message}", ex.ToString()));
            }
        }

        private CallToolResult Compile(ICompilable? compilable, string target, string filter, int maxMessages, string unavailableMessage)
        {
            if (compilable == null)
            {
                return McpToolResults.From(ToolResponse<CompileReport>.CreateError(ErrorCodes.CompilationError, unavailableMessage));
            }

            var result = compilable.Compile();
            var report = CompileResultReader.ToReport(target, result, filter, maxMessages);
            _logger.LogInformation("Compiled {Target}: {State}, {ErrorCount} errors, {WarningCount} warnings", target, report.State, report.ErrorCount, report.WarningCount);
            return McpToolResults.From(ToolResponse<CompileReport>.CreateSuccess(report));
        }

        private static bool TryValidateOptions(string messageFilter, int maxMessages, out string filter, out string errorMessage)
        {
            filter = CompileReport.NormalizeFilter(messageFilter) ?? string.Empty;
            errorMessage = string.Empty;

            if (filter.Length == 0)
            {
                errorMessage = $"Invalid messageFilter '{messageFilter}'. Must be '{CompileReport.FilterErrors}', '{CompileReport.FilterErrorsAndWarnings}' or '{CompileReport.FilterAll}'";
                return false;
            }

            if (maxMessages < 0)
            {
                errorMessage = "maxMessages must be 0 (no limit) or greater";
                return false;
            }

            return true;
        }
    }
}
