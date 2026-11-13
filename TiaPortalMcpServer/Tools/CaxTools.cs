using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Siemens.Engineering.Cax;
using Siemens.Engineering.HW;
using TiaPortalMcpServer.Models;
using TiaPortalMcpServer.Services;

namespace TiaPortalMcpServer
{
    /// <summary>
    /// CAx (AutomationML) export of the hardware configuration. There is deliberately no import tool:
    /// a CAx import creates objects and does not update an existing project.
    /// </summary>
    [McpServerToolType]
    public class CaxTools
    {
        private const string AmlExtension = ".aml";

        private readonly ILogger<CaxTools> _logger;
        private readonly TiaPortalSessionManager _sessionManager;

        public CaxTools(
            ILogger<CaxTools> logger,
            TiaPortalSessionManager sessionManager)
        {
            _logger = logger;
            _sessionManager = sessionManager;
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Export the hardware configuration of the whole project, or of one device, as an AutomationML (CAx) .aml file, and write TIA Portal's transfer messages to a .log file of the same name next to it. The export can succeed partially: the response gives the overall state (Success/Information/Warning/Error), error and warning counts, the error and warning messages (each where the problem starts, with the messages it is nested in as context) and TIA Portal's closing summary line separately. With normalize=true the values that change on every export (element IDs, write timestamps) are rewritten to stable ones and the log leaves out timestamps, so exporting an unchanged project again gives identical files. The AML does not hold every parameter shown in the TIA Portal properties. Prerequisites: Project must be open. There is no CAx import tool.")]
        public CallToolResult cax_export(
            [Description("Existing directory to write the .aml and .log files to")] string exportPath,
            [Description("Name of the .aml file to write, e.g. 'Hardware.aml'; no directories")] string fileName,
            [Description("Device name. Leave empty to export the whole project.")] string deviceName = "",
            [Description("Replace an existing .aml file. If false, an existing file makes the export fail")] bool overwrite = false,
            [Description("Rewrite element IDs to IDs derived from the element path and write timestamps to a fixed value, so repeated exports of an unchanged project are identical")] bool normalize = false)
        {
            _logger.LogInformation("cax_export called with exportPath='{ExportPath}', fileName='{FileName}', deviceName='{DeviceName}', overwrite={Overwrite}, normalize={Normalize}",
                exportPath, fileName, deviceName, overwrite, normalize);

            try
            {
                if (string.IsNullOrWhiteSpace(exportPath) || !Directory.Exists(exportPath))
                {
                    return McpToolResults.From(ToolResponse<CaxExportResult>.CreateError(ErrorCodes.InvalidParameter, $"Export directory '{exportPath}' does not exist"));
                }

                if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    return McpToolResults.From(ToolResponse<CaxExportResult>.CreateError(ErrorCodes.InvalidParameter, $"fileName '{fileName}' must be a plain file name without directories"));
                }

                if (!string.Equals(Path.GetExtension(fileName), AmlExtension, StringComparison.OrdinalIgnoreCase))
                {
                    return McpToolResults.From(ToolResponse<CaxExportResult>.CreateError(ErrorCodes.InvalidParameter, $"fileName '{fileName}' must end in {AmlExtension}"));
                }

                var target = new FileInfo(Path.Combine(Path.GetFullPath(exportPath), fileName));
                var logPath = Path.ChangeExtension(target.FullName, ".log");
                if (target.Exists && !overwrite)
                {
                    return McpToolResults.From(ToolResponse<CaxExportResult>.CreateError(ErrorCodes.InvalidParameter, $"File '{target.FullName}' already exists; pass overwrite=true to replace it"));
                }

                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return McpToolResults.From(ToolResponse<CaxExportResult>.CreateError(ErrorCodes.NoProject, "No project is currently open. Use projects_open first."));
                }

                Device? device = null;
                if (!string.IsNullOrWhiteSpace(deviceName))
                {
                    device = _sessionManager.PortalService.FindDevice(project, deviceName);
                    if (device == null)
                    {
                        return McpToolResults.From(ToolResponse<CaxExportResult>.CreateError(ErrorCodes.DeviceNotFound, $"Device '{deviceName}' not found"));
                    }
                }

                var provider = project.GetService<CaxProvider>();
                if (provider == null)
                {
                    return McpToolResults.From(ToolResponse<CaxExportResult>.CreateError(ErrorCodes.OperationNotSupported, "The CAx service is not available for this project"));
                }

                if (target.Exists)
                {
                    target.Delete();
                    target.Refresh();
                }

                var scope = device != null ? $"device '{device.Name}'" : $"project '{project.Name}'";
                _logger.LogInformation("Exporting {Scope} as AutomationML to {File}", scope, target.FullName);
                var transfer = device != null ? provider.Export(device, target) : provider.Export(project, target);

                var result = new CaxExportResult
                {
                    State = transfer.State.ToString(),
                    Scope = scope,
                    File = target.FullName,
                    LogFile = logPath,
                    ErrorCount = transfer.ErrorCount,
                    WarningCount = transfer.WarningCount
                };
                var messages = ConvertMessages(transfer.Messages);
                CaxLog.CollectProblems(messages, result);
                File.WriteAllText(logPath, CaxLog.Format(messages, includeTimestamps: !normalize), new UTF8Encoding(false));

                target.Refresh();
                if (!target.Exists || target.Length == 0)
                {
                    var reason = result.Errors.Count > 0 ? ": " + string.Join("; ", Head(result.Errors, 3)) : string.Empty;
                    return McpToolResults.From(ToolResponse<CaxExportResult>.CreateError(ErrorCodes.TiaError,
                        $"CAx export of {scope} wrote no file (state {result.State}, {result.ErrorCount} errors){reason}. See {logPath}"));
                }

                if (normalize)
                {
                    try
                    {
                        result.Normalization = AmlNormalizer.NormalizeFile(target.FullName);
                    }
                    catch (XmlException xmlEx)
                    {
                        _logger.LogError(xmlEx, "Cannot normalize {File}", target.FullName);
                        return McpToolResults.From(ToolResponse<CaxExportResult>.CreateError(ErrorCodes.InternalError,
                            $"CAx export wrote '{target.FullName}', but it could not be normalized: {xmlEx.Message}"));
                    }
                }

                _logger.LogInformation("CAx export of {Scope} finished with state {State}, {Errors} errors, {Warnings} warnings", scope, result.State, result.ErrorCount, result.WarningCount);
                return McpToolResults.From(ToolResponse<CaxExportResult>.CreateSuccess(result));
            }
            catch (COMException comEx)
            {
                _logger.LogError(comEx, "COM error during CAx export");
                return McpToolResults.From(ToolResponse<CaxExportResult>.CreateError(ErrorCodes.ComError, $"COM error during CAx export: {comEx.Message}", comEx.ToString()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during CAx export");
                return McpToolResults.From(ToolResponse<CaxExportResult>.CreateError(ErrorCodes.TiaError, $"CAx export failed: {ex.Message}", ex.ToString()));
            }
        }

        /// <summary>
        /// Copies the TIA Portal messages into plain objects. Plain loops on purpose: a closure over the
        /// Siemens TransferResultState enum would make this class fail to load without Openness.
        /// </summary>
        private static List<CaxMessage> ConvertMessages(TransferResultMessageComposition messages)
        {
            var result = new List<CaxMessage>();
            if (messages == null)
            {
                return result;
            }

            foreach (TransferResultMessage message in messages)
            {
                result.Add(new CaxMessage
                {
                    State = message.State.ToString(),
                    Message = message.Message ?? string.Empty,
                    DateTime = message.DateTime,
                    Children = ConvertMessages(message.Messages)
                });
            }
            return result;
        }

        private static IEnumerable<string> Head(IList<string> items, int count)
        {
            for (var i = 0; i < items.Count && i < count; i++)
            {
                yield return items[i];
            }
        }
    }
}
