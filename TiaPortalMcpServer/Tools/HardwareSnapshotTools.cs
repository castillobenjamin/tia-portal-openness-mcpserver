using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Siemens.Engineering.HW;
using TiaPortalMcpServer.Models;
using TiaPortalMcpServer.Services;

namespace TiaPortalMcpServer
{
    /// <summary>
    /// Review snapshots of the hardware parameters (Openness attributes) for diffing in git; covers what the CAx
    /// (AutomationML) export leaves out, such as F-parameters, channel settings and PROFINET timing.
    /// </summary>
    [McpServerToolType]
    public class HardwareSnapshotTools
    {
        private const string SnapshotFolder = "Hardware";
        private const int MaxReportedUnreadable = 100;

        private readonly ILogger<HardwareSnapshotTools> _logger;
        private readonly TiaPortalSessionManager _sessionManager;

        public HardwareSnapshotTools(ILogger<HardwareSnapshotTools> logger, TiaPortalSessionManager sessionManager)
        {
            _logger = logger;
            _sessionManager = sessionManager;
        }

        [McpServerTool(Destructive = false, Idempotent = true, OpenWorld = true), Description("Write a review snapshot of the hardware parameters for diffing in git: one JSON file per device at <exportPath>\\Hardware\\<device folder path>\\<device>.json with every readable Openness attribute of the device and of each device item below it, including the network interface (nodes, IO connectors with update and watchdog times, IO controllers and their IO systems) and the channels (for example input delays). This covers what cax_export leaves out: F-parameters, channel parameters, PROFINET timing and device names, diagnostics and CPU settings. Parameters of GSD modules (record data) have no Openness attributes and are not included. Items are keyed by name, channels by '<IoType> <Type> <Number>'; enums are member names, multilingual texts { \"<culture>\": \"<text>\" }, references to other objects { \"@ref\": \"<path>\" }. Output is deterministic: sorted keys, UTF-8 without BOM, LF line endings; unchanged files are not rewritten. Attributes that cannot be read are listed under \"@unreadable\". Returns every file of this run; files of deleted or renamed devices are not removed. Snapshots cannot be imported. The project is not changed. Prerequisites: Project must be open, exportPath must exist.")]
        public CallToolResult hardware_snapshot(
            [Description("Directory to write the snapshot to; the files go to its Hardware subdirectory")] string exportPath,
            [Description("Device name. Leave empty for every device of the project")] string deviceName = "")
        {
            _logger.LogInformation("hardware_snapshot called with exportPath='{ExportPath}', deviceName='{DeviceName}'", exportPath, deviceName);
            try
            {
                return McpToolResults.From(Snapshot(exportPath, (deviceName ?? string.Empty).Trim()));
            }
            catch (COMException comEx)
            {
                _logger.LogError(comEx, "COM error in hardware_snapshot");
                return McpToolResults.From(ToolResponse<HardwareSnapshotResult>.CreateError(ErrorCodes.ComError, $"COM error: {comEx.Message}", comEx.ToString()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in hardware_snapshot");
                return McpToolResults.From(ToolResponse<HardwareSnapshotResult>.CreateError(ErrorCodes.TiaError, $"Error: {ex.Message}", ex.ToString()));
            }
        }

        private ToolResponse<HardwareSnapshotResult> Snapshot(string exportPath, string deviceName)
        {
            if (string.IsNullOrWhiteSpace(exportPath) || !Directory.Exists(exportPath))
            {
                return ToolResponse<HardwareSnapshotResult>.CreateError(ErrorCodes.InvalidParameter, $"Export directory '{exportPath}' does not exist");
            }

            var project = _sessionManager.CurrentProject;
            if (project == null)
            {
                return ToolResponse<HardwareSnapshotResult>.CreateError(ErrorCodes.NoProject, "No project is currently open. Use projects_open first.");
            }

            var devices = new List<(Device Device, string GroupPath)>();
            foreach (var entry in _sessionManager.PortalService.GetDevicesWithGroupPaths(project))
            {
                if (deviceName.Length == 0 || entry.Device.Name == deviceName)
                {
                    devices.Add(entry);
                }
            }

            if (devices.Count == 0)
            {
                return deviceName.Length > 0
                    ? ToolResponse<HardwareSnapshotResult>.CreateError(ErrorCodes.DeviceNotFound, $"Device '{deviceName}' not found")
                    : ToolResponse<HardwareSnapshotResult>.CreateError(ErrorCodes.DeviceNotFound, "The project has no devices");
            }

            var root = Path.Combine(Path.GetFullPath(exportPath), SnapshotFolder);
            var result = new HardwareSnapshotResult { SnapshotDirectory = root };
            var unreadable = new List<string>();
            foreach (var entry in devices)
            {
                var writer = new HardwareSnapshotWriter(HardwareSnapshotReader.ConvertValue);
                var snapshot = HardwareSnapshotReader.Read(entry.Device, writer, Trace);
                var path = Path.Combine(ExchangePaths.ToDirectory(root, entry.GroupPath), ExchangePaths.ToFileName(entry.Device.Name) + ".json");
                if (HmiSnapshotSerializer.WriteIfChanged(path, HmiSnapshotSerializer.ToJson(snapshot)))
                {
                    result.WrittenCount++;
                }
                else
                {
                    result.UnchangedCount++;
                }

                result.Files.Add(path);
                unreadable.AddRange(writer.Unreadable);
                result.DeviceCount++;
            }

            result.Files = result.Files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            result.UnreadableCount = unreadable.Count;
            if (unreadable.Count > 0)
            {
                result.Unreadable = unreadable.Take(MaxReportedUnreadable).ToList();
            }

            _logger.LogInformation("hardware_snapshot wrote {Written} and left {Unchanged} unchanged of {Devices} devices, {Unreadable} unreadable attributes",
                result.WrittenCount, result.UnchangedCount, result.DeviceCount, result.UnreadableCount);
            return ToolResponse<HardwareSnapshotResult>.CreateSuccess(result);
        }

        private void Trace(string path)
        {
            _logger.LogDebug("Hardware snapshot: reading {Path}", path);
        }
    }
}
