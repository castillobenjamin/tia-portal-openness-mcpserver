using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiConnections;
using Siemens.Engineering.HmiUnified.HmiTags;
using Siemens.Engineering.HmiUnified.Scripts;
using Siemens.Engineering.HmiUnified.TextGraphicList;
using Siemens.Engineering.HmiUnified.UI.Base;
using TiaPortalMcpServer.Models;
using TiaPortalMcpServer.Services;

namespace TiaPortalMcpServer
{
    /// <summary>
    /// WinCC Unified HMI tools: screens (including review snapshots), screen items, HMI tags, connections, text lists, graphic lists, system text lists, script modules, and review snapshots of alarms, logs, connections and runtime settings.
    /// </summary>
    /// <remarks>
    /// Write tools change the open project in memory; call projects_save to persist the changes.
    /// </remarks>
    [McpServerToolType]
    public class HmiUnifiedTools
    {
        private const string ExportFileNameDescription = "Base name of the export; TIA Portal adds its own extension, .hmi.yml for graphic and system text lists (a trailing .hmi.yml given here is dropped),";
        private const string ImportFileNameDescription = "Name passed to the export, or the name of the written .hmi.yml file,";
        private const string DeviceDescription = "Name of the device with WinCC Unified HMI software (panel or PC runtime), as listed by hmi_unified_software_list";
        private const string AttributesDescription = "JSON object of attribute names and values, e.g. {\"Left\": 20, \"Top\": 40, \"Width\": 160, \"Height\": 40, \"Text\": \"Start\"}. Numbers, booleans, enum member names, colors ('#RRGGBB', '#AARRGGBB', 'R,G,B', 'A,R,G,B' or a name such as 'Red') and strings are converted to the attribute's type. For multilingual texts such as Text, a plain value sets every language and 'de-DE:Start' sets one";
        private const int MaxReportedUnreadable = 100;
        private const string SaveNote = " Changes are made in the open project; call projects_save to persist them.";

        private readonly ILogger<HmiUnifiedTools> _logger;
        private readonly TiaPortalSessionManager _sessionManager;

        public HmiUnifiedTools(ILogger<HmiUnifiedTools> logger, TiaPortalSessionManager sessionManager)
        {
            _logger = logger;
            _sessionManager = sessionManager;
        }

        #region Software

        [McpServerTool(ReadOnly = true, Idempotent = true), Description("List the WinCC Unified HMI software in the open project (Unified Comfort Panels and PC runtimes), including devices in device folders, with the number of screens, HMI tags, tag tables, connections and text lists. Prerequisites: Project must be open. Use the returned deviceName with the other hmi_unified tools.")]
        public CallToolResult hmi_unified_software_list()
        {
            _logger.LogInformation("hmi_unified_software_list called");
            return Run<object>("hmi_unified_software_list", () =>
            {
                var project = _sessionManager.CurrentProject;
                if (project == null)
                {
                    return ToolResponse<object>.CreateError(ErrorCodes.NoProject, "No project is currently open. Use projects_open first.");
                }

                var items = new List<HmiUnifiedSoftwareInfo>();
                foreach (var entry in _sessionManager.PortalService.GetDevicesWithGroupPaths(project))
                {
                    var software = HmiUnifiedService.GetHmiSoftware(entry.Device);
                    if (software == null)
                    {
                        continue;
                    }

                    items.Add(new HmiUnifiedSoftwareInfo
                    {
                        DeviceName = entry.Device.Name,
                        SoftwareName = software.Name,
                        DeviceGroupPath = entry.GroupPath,
                        ScreenCount = HmiUnifiedService.GetScreensWithGroupPaths(software).Count,
                        TagCount = software.Tags.Count,
                        TagTableCount = HmiUnifiedService.GetTagTablesWithGroupPaths(software).Count,
                        ConnectionCount = software.Connections.Count,
                        TextListCount = software.HmiTextLists.Count,
                        GraphicListCount = software.HmiGraphicLists.Count,
                        SystemTextListCount = software.HmiSystemTextLists.Count
                    });
                }

                return ToolResponse<object>.CreateSuccess(new { count = items.Count, items });
            });
        }

        #endregion

        #region Screens

        [McpServerTool(ReadOnly = true, Idempotent = true), Description("List the screens of a WinCC Unified HMI, including screens in screen groups, with group path, screen number, size and number of screen items. Prerequisites: Project must be open, device must have WinCC Unified HMI software.")]
        public CallToolResult hmi_unified_screens_list(
            [Description(DeviceDescription)] string deviceName)
        {
            _logger.LogInformation("hmi_unified_screens_list called with deviceName='{DeviceName}'", deviceName);
            return Run<object>("hmi_unified_screens_list", () =>
            {
                var software = ResolveSoftware(deviceName, out var error);
                if (software == null)
                {
                    return ToolResponse<object>.CreateError(error.Code, error.Message);
                }

                var screens = new List<HmiScreenInfo>();
                foreach (var entry in HmiUnifiedService.GetScreensWithGroupPaths(software))
                {
                    screens.Add(HmiUnifiedService.CreateScreenInfo(entry.Screen, entry.GroupPath));
                }

                return ToolResponse<object>.CreateSuccess(new { deviceName, count = screens.Count, screens });
            });
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Create a screen in a WinCC Unified HMI, at the top level or in a screen group (missing groups are created). Prerequisites: Project must be open, device must have WinCC Unified HMI software, the screen name must not exist yet. Use dryRun=true to check without creating." + SaveNote)]
        public CallToolResult hmi_unified_screen_create(
            [Description(DeviceDescription)] string deviceName,
            [Description("Name of the new screen")] string screenName,
            [Description("Screen group path such as 'Overview/Lines'. Leave empty for the top level")] string groupPath = "",
            [Description("If true, only validate without creating")] bool dryRun = false)
        {
            _logger.LogInformation("hmi_unified_screen_create called with deviceName='{DeviceName}', screenName='{ScreenName}', groupPath='{GroupPath}', dryRun={DryRun}", deviceName, screenName, groupPath, dryRun);
            return Run<HmiOperationResult>("hmi_unified_screen_create", () =>
            {
                if (string.IsNullOrWhiteSpace(screenName))
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, "screenName is required");
                }

                var software = ResolveSoftware(deviceName, out var error);
                if (software == null)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(error.Code, error.Message);
                }

                var existing = HmiUnifiedService.FindScreen(software, screenName);
                if (existing.Screen != null)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, $"Screen '{screenName}' already exists in '{DisplayPath(existing.GroupPath)}'");
                }

                var path = ExchangePaths.CombineGroupPath(groupPath);
                var result = new HmiOperationResult { DeviceName = deviceName, Target = $"screen '{screenName}'", DryRun = dryRun };
                if (path.Length > 0 && HmiUnifiedService.FindScreenGroup(software, path) == null)
                {
                    result.Changes.Add($"Create screen group '{path}'");
                }

                result.Changes.Add($"Create screen '{screenName}' in '{DisplayPath(path)}'");
                if (!dryRun)
                {
                    HmiUnifiedService.CreateScreen(software, screenName, path);
                }

                return ToolResponse<HmiOperationResult>.CreateSuccess(result);
            });
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Delete a screen of a WinCC Unified HMI, searched in all screen groups. Prerequisites: Project must be open, device must have WinCC Unified HMI software, screen must exist. Use dryRun=true to check without deleting." + SaveNote)]
        public CallToolResult hmi_unified_screen_delete(
            [Description(DeviceDescription)] string deviceName,
            [Description("Screen name")] string screenName,
            [Description("If true, only validate without deleting")] bool dryRun = false)
        {
            _logger.LogInformation("hmi_unified_screen_delete called with deviceName='{DeviceName}', screenName='{ScreenName}', dryRun={DryRun}", deviceName, screenName, dryRun);
            return Run<HmiOperationResult>("hmi_unified_screen_delete", () =>
            {
                var software = ResolveSoftware(deviceName, out var error);
                if (software == null)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(error.Code, error.Message);
                }

                var found = HmiUnifiedService.FindScreen(software, screenName);
                if (found.Screen == null)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, $"Screen '{screenName}' not found");
                }

                var result = new HmiOperationResult { DeviceName = deviceName, Target = $"screen '{screenName}'", DryRun = dryRun };
                result.Changes.Add($"Delete screen '{screenName}' from '{DisplayPath(found.GroupPath)}' with {found.Screen.ScreenItems.Count} screen items");
                if (!dryRun)
                {
                    found.Screen.Delete();
                }

                return ToolResponse<HmiOperationResult>.CreateSuccess(result);
            });
        }

        [McpServerTool(Destructive = false, Idempotent = true, OpenWorld = true), Description("Write a review snapshot of WinCC Unified screens for diffing in git: one JSON file per screen at <exportPath>\\Screens\\<screen group path>\\<screen>.json with every readable property of the screen and of its items (in z-order), including parts such as fonts, dynamizations keyed by property, event handlers keyed by event and faceplate interfaces. Colors are #AARRGGBB, enums member names, multilingual texts { \"<culture>\": \"<text>\" }. Script code goes to sidecar files in <screen>.scripts\\ (<item>.<event>.js, <item>.<property>.dyn.js, and .global.js for global definition areas) referenced as { \"file\": ... }. Output is deterministic: sorted keys, UTF-8 without BOM, LF line endings; unchanged files are not rewritten. Properties that cannot be read are listed under \"@unreadable\". Returns every file of this run; files of deleted or renamed screens and scripts are not removed. Snapshots cannot be imported. The project is not changed. Prerequisites: Project must be open, device must have WinCC Unified HMI software, exportPath must exist.")]
        public CallToolResult hmi_unified_screens_snapshot(
            [Description(DeviceDescription)] string deviceName,
            [Description("Directory to write the snapshot to; the files go to its Screens subdirectory")] string exportPath,
            [Description("Screen name filter: plain text matches screen names containing it (case-insensitive), otherwise a regular expression. Leave empty for all screens")] string screenName = "")
        {
            _logger.LogInformation("hmi_unified_screens_snapshot called with deviceName='{DeviceName}', exportPath='{ExportPath}', screenName='{ScreenName}'", deviceName, exportPath, screenName);
            return Run<HmiScreenSnapshotResult>("hmi_unified_screens_snapshot", () =>
            {
                if (string.IsNullOrWhiteSpace(exportPath) || !Directory.Exists(exportPath))
                {
                    return ToolResponse<HmiScreenSnapshotResult>.CreateError(ErrorCodes.InvalidParameter, $"Export directory '{exportPath}' does not exist");
                }

                if (!NameFilter.TryValidate(screenName, out var filterError))
                {
                    return ToolResponse<HmiScreenSnapshotResult>.CreateError(ErrorCodes.InvalidParameter, filterError);
                }

                var software = ResolveSoftware(deviceName, out var error);
                if (software == null)
                {
                    return ToolResponse<HmiScreenSnapshotResult>.CreateError(error.Code, error.Message);
                }

                var result = new HmiScreenSnapshotResult
                {
                    DeviceName = deviceName,
                    SnapshotDirectory = Path.Combine(Path.GetFullPath(exportPath), "Screens")
                };
                var unreadable = new List<string>();

                foreach (var entry in HmiUnifiedService.GetScreensWithGroupPaths(software))
                {
                    if (!NameFilter.IsMatch(entry.Screen.Name, screenName))
                    {
                        continue;
                    }

                    var serializer = CreateSnapshotSerializer(entry.Screen.Name);
                    var snapshot = serializer.Serialize(entry.Screen, entry.Screen.Name);
                    WriteSnapshot(ExchangePaths.ToDirectory(result.SnapshotDirectory, entry.GroupPath), entry.Screen.Name, snapshot, serializer, result, unreadable);
                    result.ScreenCount++;
                }

                CompleteSnapshot(result, unreadable);
                return ToolResponse<HmiScreenSnapshotResult>.CreateSuccess(result);
            });
        }

        #endregion

        #region Screen items

        [McpServerTool(ReadOnly = true, Idempotent = true), Description("List the screen items of a WinCC Unified screen with type, position, size, visibility and enabled state. Set includeAttributes=true to also return every other attribute with a simple value (numbers, booleans, enums, strings), e.g. to find attribute names for hmi_unified_screen_item_set_attributes. Prerequisites: Project must be open, device must have WinCC Unified HMI software, screen must exist.")]
        public CallToolResult hmi_unified_screen_items_list(
            [Description(DeviceDescription)] string deviceName,
            [Description("Screen name")] string screenName,
            [Description("Include all simple attributes of each item")] bool includeAttributes = false)
        {
            _logger.LogInformation("hmi_unified_screen_items_list called with deviceName='{DeviceName}', screenName='{ScreenName}'", deviceName, screenName);
            return Run<object>("hmi_unified_screen_items_list", () =>
            {
                var software = ResolveSoftware(deviceName, out var error);
                if (software == null)
                {
                    return ToolResponse<object>.CreateError(error.Code, error.Message);
                }

                var found = HmiUnifiedService.FindScreen(software, screenName);
                if (found.Screen == null)
                {
                    return ToolResponse<object>.CreateError(ErrorCodes.InvalidParameter, $"Screen '{screenName}' not found");
                }

                var items = new List<HmiScreenItemInfo>();
                foreach (HmiScreenItemBase item in found.Screen.ScreenItems)
                {
                    items.Add(HmiUnifiedService.CreateScreenItemInfo(item, includeAttributes));
                }

                return ToolResponse<object>.CreateSuccess(new { deviceName, screenName, count = items.Count, items });
            });
        }

        [McpServerTool(ReadOnly = true, Idempotent = true), Description("List the WinCC Unified screen item types that hmi_unified_screen_item_create accepts, such as Button, IOField, Text, Rectangle, Circle, Line, Bar, Gauge, Slider, ToggleSwitch, CheckBoxGroup or ListBox. Needs no open project.")]
        public CallToolResult hmi_unified_screen_item_types_list()
        {
            _logger.LogInformation("hmi_unified_screen_item_types_list called");
            return Run<object>("hmi_unified_screen_item_types_list", () =>
            {
                var types = HmiUnifiedService.GetScreenItemTypeNames();
                return ToolResponse<object>.CreateSuccess(new { count = types.Count, types });
            });
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Create a screen item on a WinCC Unified screen and optionally set its attributes (position, size, text, ...). Prerequisites: Project must be open, device must have WinCC Unified HMI software, screen must exist, the item name must be unique on the screen. Use hmi_unified_screen_item_types_list for valid itemType values. Use dryRun=true to check the type and attribute values without creating." + SaveNote)]
        public CallToolResult hmi_unified_screen_item_create(
            [Description(DeviceDescription)] string deviceName,
            [Description("Screen name")] string screenName,
            [Description("Screen item type, e.g. 'Button', 'IOField', 'Text', 'Rectangle'")] string itemType,
            [Description("Name of the new screen item")] string itemName,
            [Description(AttributesDescription)] string attributes = "",
            [Description("If true, only validate without creating")] bool dryRun = false)
        {
            _logger.LogInformation("hmi_unified_screen_item_create called with deviceName='{DeviceName}', screenName='{ScreenName}', itemType='{ItemType}', itemName='{ItemName}', dryRun={DryRun}", deviceName, screenName, itemType, itemName, dryRun);
            return Run<HmiOperationResult>("hmi_unified_screen_item_create", () =>
            {
                if (string.IsNullOrWhiteSpace(itemName))
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, "itemName is required");
                }

                if (!AttributeValueConverter.TryParseAttributeMap(attributes, out var attributeMap, out var attributeError))
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, attributeError);
                }

                var type = HmiUnifiedService.ResolveScreenItemType(itemType);
                if (type == null)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter,
                        $"Unknown screen item type '{itemType}'. Valid types: {string.Join(", ", HmiUnifiedService.GetScreenItemTypeNames())}");
                }

                var software = ResolveSoftware(deviceName, out var error);
                if (software == null)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(error.Code, error.Message);
                }

                var found = HmiUnifiedService.FindScreen(software, screenName);
                if (found.Screen == null)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, $"Screen '{screenName}' not found");
                }

                if (found.Screen.ScreenItems.Find(itemName) != null)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, $"Screen '{screenName}' already has an item named '{itemName}'");
                }

                var result = new HmiOperationResult { DeviceName = deviceName, Target = $"item '{itemName}' on screen '{screenName}'", DryRun = dryRun };
                result.Changes.Add($"Create {HmiUnifiedService.ToTypeName(type)} '{itemName}'");
                if (dryRun)
                {
                    foreach (var pair in attributeMap)
                    {
                        result.Changes.Add($"Set {pair.Key} to '{pair.Value}'");
                    }

                    return ToolResponse<HmiOperationResult>.CreateSuccess(result);
                }

                var item = HmiUnifiedService.CreateScreenItem(found.Screen, type, itemName);
                if (!HmiUnifiedService.TrySetAttributes(item, attributeMap, false, result.Changes, out var setError))
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter,
                        $"Screen item '{itemName}' was created, but setting its attributes failed: {setError}");
                }

                return ToolResponse<HmiOperationResult>.CreateSuccess(result);
            });
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Set attributes of a WinCC Unified screen item, e.g. position, size, visibility or text. Every value is checked and converted before any attribute is changed. Returns each change with its old and new value. Prerequisites: Project must be open, device must have WinCC Unified HMI software, screen and item must exist. Use hmi_unified_screen_items_list with includeAttributes=true to see attribute names. Use dryRun=true to preview the changes." + SaveNote)]
        public CallToolResult hmi_unified_screen_item_set_attributes(
            [Description(DeviceDescription)] string deviceName,
            [Description("Screen name")] string screenName,
            [Description("Screen item name")] string itemName,
            [Description(AttributesDescription)] string attributes,
            [Description("If true, only validate and show the changes without applying them")] bool dryRun = false)
        {
            _logger.LogInformation("hmi_unified_screen_item_set_attributes called with deviceName='{DeviceName}', screenName='{ScreenName}', itemName='{ItemName}', dryRun={DryRun}", deviceName, screenName, itemName, dryRun);
            return Run<HmiOperationResult>("hmi_unified_screen_item_set_attributes", () =>
            {
                if (!AttributeValueConverter.TryParseAttributeMap(attributes, out var attributeMap, out var attributeError))
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, attributeError);
                }

                if (attributeMap.Count == 0)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, "attributes must name at least one attribute");
                }

                var item = ResolveScreenItem(deviceName, screenName, itemName, out var error);
                if (item == null)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(error.Code, error.Message);
                }

                var result = new HmiOperationResult { DeviceName = deviceName, Target = $"item '{itemName}' on screen '{screenName}'", DryRun = dryRun };

                // Validate everything first so a bad value does not leave the item half changed
                if (!HmiUnifiedService.TrySetAttributes(item, attributeMap, true, new List<string>(), out var validationError))
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, validationError);
                }

                if (!HmiUnifiedService.TrySetAttributes(item, attributeMap, dryRun, result.Changes, out var setError))
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.TiaError, setError);
                }

                return ToolResponse<HmiOperationResult>.CreateSuccess(result);
            });
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Delete a screen item from a WinCC Unified screen. Prerequisites: Project must be open, device must have WinCC Unified HMI software, screen and item must exist. Use dryRun=true to check without deleting." + SaveNote)]
        public CallToolResult hmi_unified_screen_item_delete(
            [Description(DeviceDescription)] string deviceName,
            [Description("Screen name")] string screenName,
            [Description("Screen item name")] string itemName,
            [Description("If true, only validate without deleting")] bool dryRun = false)
        {
            _logger.LogInformation("hmi_unified_screen_item_delete called with deviceName='{DeviceName}', screenName='{ScreenName}', itemName='{ItemName}', dryRun={DryRun}", deviceName, screenName, itemName, dryRun);
            return Run<HmiOperationResult>("hmi_unified_screen_item_delete", () =>
            {
                var item = ResolveScreenItem(deviceName, screenName, itemName, out var error);
                if (item == null)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(error.Code, error.Message);
                }

                var result = new HmiOperationResult { DeviceName = deviceName, Target = $"item '{itemName}' on screen '{screenName}'", DryRun = dryRun };
                result.Changes.Add($"Delete {HmiUnifiedService.ToTypeName(item.GetType())} '{itemName}'");
                if (!dryRun)
                {
                    item.Delete();
                }

                return ToolResponse<HmiOperationResult>.CreateSuccess(result);
            });
        }

        #endregion

        #region Tags

        [McpServerTool(ReadOnly = true, Idempotent = true), Description("List the HMI tag tables of a WinCC Unified HMI, including tables in tag table groups, with group path and number of tags. Prerequisites: Project must be open, device must have WinCC Unified HMI software.")]
        public CallToolResult hmi_unified_tag_tables_list(
            [Description(DeviceDescription)] string deviceName)
        {
            _logger.LogInformation("hmi_unified_tag_tables_list called with deviceName='{DeviceName}'", deviceName);
            return Run<object>("hmi_unified_tag_tables_list", () =>
            {
                var software = ResolveSoftware(deviceName, out var error);
                if (software == null)
                {
                    return ToolResponse<object>.CreateError(error.Code, error.Message);
                }

                var tables = new List<HmiTagTableInfo>();
                foreach (var entry in HmiUnifiedService.GetTagTablesWithGroupPaths(software))
                {
                    tables.Add(new HmiTagTableInfo { Name = entry.Table.Name, GroupPath = entry.GroupPath, TagCount = entry.Table.Tags.Count });
                }

                return ToolResponse<object>.CreateSuccess(new { deviceName, count = tables.Count, tagTables = tables });
            });
        }

        [McpServerTool(ReadOnly = true, Idempotent = true), Description("List the HMI tags of a WinCC Unified HMI with tag table, data type, connection, PLC tag, address and acquisition cycle, optionally filtered by tag table and by name/regex. Prerequisites: Project must be open, device must have WinCC Unified HMI software.")]
        public CallToolResult hmi_unified_tags_list(
            [Description(DeviceDescription)] string deviceName,
            [Description("Only tags of this tag table. Leave empty for all tags")] string tagTableName = "",
            [Description("Case-insensitive name fragment or regular expression to filter tags. Empty string lists all")] string regexName = "")
        {
            _logger.LogInformation("hmi_unified_tags_list called with deviceName='{DeviceName}', tagTableName='{TagTableName}', regexName='{RegexName}'", deviceName, tagTableName, regexName);
            return Run<object>("hmi_unified_tags_list", () =>
            {
                if (!NameFilter.TryValidate(regexName, out var filterError))
                {
                    return ToolResponse<object>.CreateError(ErrorCodes.InvalidParameter, filterError);
                }

                var software = ResolveSoftware(deviceName, out var error);
                if (software == null)
                {
                    return ToolResponse<object>.CreateError(error.Code, error.Message);
                }

                var tags = new List<HmiUnifiedTagInfo>();
                foreach (HmiTag tag in software.Tags)
                {
                    if (!NameFilter.IsMatch(tag.Name, regexName))
                    {
                        continue;
                    }

                    var info = CreateTagInfo(tag);
                    if (!string.IsNullOrWhiteSpace(tagTableName) && !string.Equals(info.TagTableName, tagTableName.Trim(), StringComparison.Ordinal))
                    {
                        continue;
                    }

                    tags.Add(info);
                }

                return ToolResponse<object>.CreateSuccess(new { deviceName, count = tags.Count, tags });
            });
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Create an HMI tag in a WinCC Unified HMI. For an external tag, give connection and plcTag (e.g. '\"Motor_DB\".Speed'); without a connection the tag is internal and needs a dataType (e.g. 'Int', 'Bool', 'Real', 'WString'). Prerequisites: Project must be open, device must have WinCC Unified HMI software, the tag name must not exist yet, the tag table and connection must exist. Use dryRun=true to check without creating." + SaveNote)]
        public CallToolResult hmi_unified_tag_create(
            [Description(DeviceDescription)] string deviceName,
            [Description("Name of the new HMI tag")] string tagName,
            [Description("Tag table to create the tag in. Leave empty for the default tag table")] string tagTableName = "",
            [Description("Data type, e.g. 'Int'. For external tags it usually follows from plcTag and can be left empty")] string dataType = "",
            [Description("HMI connection name for an external tag. Leave empty for an internal tag")] string connection = "",
            [Description("PLC tag the HMI tag is bound to, e.g. '\"Motor_DB\".Speed'")] string plcTag = "",
            [Description("Acquisition cycle such as 'T1s' or '100 ms'. Leave empty for the default")] string acquisitionCycle = "",
            [Description("If true, only validate without creating")] bool dryRun = false)
        {
            _logger.LogInformation("hmi_unified_tag_create called with deviceName='{DeviceName}', tagName='{TagName}', tagTableName='{TagTableName}', connection='{Connection}', dryRun={DryRun}", deviceName, tagName, tagTableName, connection, dryRun);
            return Run<HmiOperationResult>("hmi_unified_tag_create", () =>
            {
                if (string.IsNullOrWhiteSpace(tagName))
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, "tagName is required");
                }

                var external = !string.IsNullOrWhiteSpace(connection);
                if (!external && string.IsNullOrWhiteSpace(dataType))
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, "An internal tag (no connection) needs a dataType");
                }

                if (external && string.IsNullOrWhiteSpace(plcTag))
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, "An external tag needs the plcTag it is bound to");
                }

                var software = ResolveSoftware(deviceName, out var error);
                if (software == null)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(error.Code, error.Message);
                }

                if (software.Tags.Find(tagName) != null)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, $"HMI tag '{tagName}' already exists");
                }

                if (!string.IsNullOrWhiteSpace(tagTableName) && !TagTableExists(software, tagTableName.Trim()))
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, $"HMI tag table '{tagTableName}' not found");
                }

                if (external && software.Connections.Find(connection.Trim()) == null)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, $"HMI connection '{connection}' not found");
                }

                var result = new HmiOperationResult { DeviceName = deviceName, Target = $"HMI tag '{tagName}'", DryRun = dryRun };
                result.Changes.Add(string.IsNullOrWhiteSpace(tagTableName)
                    ? $"Create {(external ? "external" : "internal")} tag '{tagName}' in the default tag table"
                    : $"Create {(external ? "external" : "internal")} tag '{tagName}' in tag table '{tagTableName.Trim()}'");

                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                if (external)
                {
                    values["Connection"] = connection.Trim();
                    values["PlcTag"] = plcTag.Trim();
                }

                if (!string.IsNullOrWhiteSpace(dataType))
                {
                    values["DataType"] = dataType.Trim();
                }

                if (!string.IsNullOrWhiteSpace(acquisitionCycle))
                {
                    values["AcquisitionCycle"] = acquisitionCycle.Trim();
                }

                if (dryRun)
                {
                    foreach (var pair in values)
                    {
                        result.Changes.Add($"Set {pair.Key} to '{pair.Value}'");
                    }

                    return ToolResponse<HmiOperationResult>.CreateSuccess(result);
                }

                var tag = string.IsNullOrWhiteSpace(tagTableName)
                    ? software.Tags.Create(tagName)
                    : software.Tags.Create(tagName, tagTableName.Trim());

                // Connection before PlcTag: the PLC tag is resolved through the connection
                foreach (var name in new[] { "Connection", "PlcTag", "DataType", "AcquisitionCycle" })
                {
                    if (values.TryGetValue(name, out var value))
                    {
                        tag.SetAttribute(name, value);
                        result.Changes.Add($"Set {name} to '{value}'");
                    }
                }

                return ToolResponse<HmiOperationResult>.CreateSuccess(result);
            });
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Delete an HMI tag of a WinCC Unified HMI. Prerequisites: Project must be open, device must have WinCC Unified HMI software, tag must exist. Screen items using the tag keep a broken reference. Use dryRun=true to check without deleting." + SaveNote)]
        public CallToolResult hmi_unified_tag_delete(
            [Description(DeviceDescription)] string deviceName,
            [Description("HMI tag name")] string tagName,
            [Description("If true, only validate without deleting")] bool dryRun = false)
        {
            _logger.LogInformation("hmi_unified_tag_delete called with deviceName='{DeviceName}', tagName='{TagName}', dryRun={DryRun}", deviceName, tagName, dryRun);
            return Run<HmiOperationResult>("hmi_unified_tag_delete", () =>
            {
                var software = ResolveSoftware(deviceName, out var error);
                if (software == null)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(error.Code, error.Message);
                }

                var tag = software.Tags.Find(tagName);
                if (tag == null)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, $"HMI tag '{tagName}' not found");
                }

                var result = new HmiOperationResult { DeviceName = deviceName, Target = $"HMI tag '{tagName}'", DryRun = dryRun };
                result.Changes.Add($"Delete HMI tag '{tagName}' from tag table '{CreateTagInfo(tag).TagTableName}'");
                if (!dryRun)
                {
                    tag.Delete();
                }

                return ToolResponse<HmiOperationResult>.CreateSuccess(result);
            });
        }

        #endregion

        #region Connections

        [McpServerTool(ReadOnly = true, Idempotent = true), Description("List the HMI connections of a WinCC Unified HMI with communication driver, partner, station, node, address and comment. Prerequisites: Project must be open, device must have WinCC Unified HMI software. Use a returned name as connection in hmi_unified_tag_create.")]
        public CallToolResult hmi_unified_connections_list(
            [Description(DeviceDescription)] string deviceName)
        {
            _logger.LogInformation("hmi_unified_connections_list called with deviceName='{DeviceName}'", deviceName);
            return Run<object>("hmi_unified_connections_list", () =>
            {
                var software = ResolveSoftware(deviceName, out var error);
                if (software == null)
                {
                    return ToolResponse<object>.CreateError(error.Code, error.Message);
                }

                var connections = new List<HmiConnectionInfo>();
                foreach (HmiConnection connection in software.Connections)
                {
                    connections.Add(new HmiConnectionInfo
                    {
                        Name = connection.Name,
                        CommunicationDriver = ReadString(connection, "CommunicationDriver"),
                        Partner = ReadString(connection, "Partner"),
                        Station = ReadString(connection, "Station"),
                        Node = ReadString(connection, "Node"),
                        InitialAddress = ReadString(connection, "InitialAddress"),
                        Comment = ReadString(connection, "Comment")
                    });
                }

                return ToolResponse<object>.CreateSuccess(new { deviceName, count = connections.Count, connections });
            });
        }

        #endregion

        #region Text lists

        [McpServerTool(ReadOnly = true, Idempotent = true), Description("List the text lists of a WinCC Unified HMI by name. The entries of text lists are read and written with hmi_unified_textlists_export and hmi_unified_textlists_import. Prerequisites: Project must be open, device must have WinCC Unified HMI software.")]
        public CallToolResult hmi_unified_textlists_list(
            [Description(DeviceDescription)] string deviceName)
        {
            _logger.LogInformation("hmi_unified_textlists_list called with deviceName='{DeviceName}'", deviceName);
            return Run<object>("hmi_unified_textlists_list", () =>
            {
                var software = ResolveSoftware(deviceName, out var error);
                if (software == null)
                {
                    return ToolResponse<object>.CreateError(error.Code, error.Message);
                }

                var names = new List<string>();
                foreach (HmiTextList textList in software.HmiTextLists)
                {
                    names.Add(textList.Name);
                }

                return ToolResponse<object>.CreateSuccess(new { deviceName, count = names.Count, textLists = names });
            });
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Export all text lists of a WinCC Unified HMI, with their entries, to a file in exportPath (TIA Portal's text list exchange format; TIA Portal names the file after fileName). Returns the written files. Prerequisites: Project must be open, device must have WinCC Unified HMI software, exportPath must exist. Use hmi_unified_textlists_import to reverse this operation.")]
        public CallToolResult hmi_unified_textlists_export(
            [Description(DeviceDescription)] string deviceName,
            [Description("Directory to write the export file to")] string exportPath,
            [Description(ExportFileNameDescription + " e.g. 'TextLists'")] string fileName)
        {
            _logger.LogInformation("hmi_unified_textlists_export called with deviceName='{DeviceName}', exportPath='{ExportPath}', fileName='{FileName}'", deviceName, exportPath, fileName);
            return Run<HmiOperationResult>("hmi_unified_textlists_export", () =>
                ExportListFile(deviceName, exportPath, fileName, "text lists",
                    (software, directory, name) => software.HmiTextLists.Export(directory, name),
                    software => software.HmiTextLists.Count));
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Import text lists with their entries into a WinCC Unified HMI from a file written by hmi_unified_textlists_export (or edited from one). Existing text lists of the same name are updated. Prerequisites: Project must be open, device must have WinCC Unified HMI software, the file must exist in importPath. Use dryRun=true to check the file without importing." + SaveNote)]
        public CallToolResult hmi_unified_textlists_import(
            [Description(DeviceDescription)] string deviceName,
            [Description("Directory containing the import file")] string importPath,
            [Description(ImportFileNameDescription + " e.g. 'TextLists'")] string fileName,
            [Description("If true, only validate without importing")] bool dryRun = false)
        {
            _logger.LogInformation("hmi_unified_textlists_import called with deviceName='{DeviceName}', importPath='{ImportPath}', fileName='{FileName}', dryRun={DryRun}", deviceName, importPath, fileName, dryRun);
            return Run<HmiOperationResult>("hmi_unified_textlists_import", () =>
                ImportListFile(deviceName, importPath, fileName, dryRun, "text lists",
                    (software, directory, name) => software.HmiTextLists.Import(directory, name),
                    software => software.HmiTextLists.Count));
        }

        [McpServerTool(ReadOnly = true, Idempotent = true), Description("List the graphic lists of a WinCC Unified HMI by name. The entries of graphic lists are read and written with hmi_unified_graphiclists_export and hmi_unified_graphiclists_import. Prerequisites: Project must be open, device must have WinCC Unified HMI software.")]
        public CallToolResult hmi_unified_graphiclists_list(
            [Description(DeviceDescription)] string deviceName)
        {
            _logger.LogInformation("hmi_unified_graphiclists_list called with deviceName='{DeviceName}'", deviceName);
            return Run<object>("hmi_unified_graphiclists_list", () =>
            {
                var software = ResolveSoftware(deviceName, out var error);
                if (software == null)
                {
                    return ToolResponse<object>.CreateError(error.Code, error.Message);
                }

                var names = new List<string>();
                foreach (HmiGraphicList graphicList in software.HmiGraphicLists)
                {
                    names.Add(graphicList.Name);
                }

                return ToolResponse<object>.CreateSuccess(new { deviceName, count = names.Count, graphicLists = names });
            });
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Export all graphic lists of a WinCC Unified HMI, with their entries, to a file in exportPath (TIA Portal's graphic list exchange format, YAML: <fileName>.hmi.yml). Returns the written files. Prerequisites: Project must be open, device must have WinCC Unified HMI software, exportPath must exist. Use hmi_unified_graphiclists_import to reverse this operation.")]
        public CallToolResult hmi_unified_graphiclists_export(
            [Description(DeviceDescription)] string deviceName,
            [Description("Directory to write the export file to")] string exportPath,
            [Description(ExportFileNameDescription + " e.g. 'GraphicLists'")] string fileName)
        {
            _logger.LogInformation("hmi_unified_graphiclists_export called with deviceName='{DeviceName}', exportPath='{ExportPath}', fileName='{FileName}'", deviceName, exportPath, fileName);
            return Run<HmiOperationResult>("hmi_unified_graphiclists_export", () =>
                ExportListFile(deviceName, exportPath, fileName, "graphic lists",
                    (software, directory, name) => software.HmiGraphicLists.Export(directory, name),
                    software => software.HmiGraphicLists.Count));
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Import graphic lists with their entries into a WinCC Unified HMI from a file written by hmi_unified_graphiclists_export (or edited from one). Existing graphic lists of the same name are updated. Prerequisites: Project must be open, device must have WinCC Unified HMI software, the file must exist in importPath. Use dryRun=true to check the file without importing." + SaveNote)]
        public CallToolResult hmi_unified_graphiclists_import(
            [Description(DeviceDescription)] string deviceName,
            [Description("Directory containing the import file")] string importPath,
            [Description(ImportFileNameDescription + " e.g. 'GraphicLists'")] string fileName,
            [Description("If true, only validate without importing")] bool dryRun = false)
        {
            _logger.LogInformation("hmi_unified_graphiclists_import called with deviceName='{DeviceName}', importPath='{ImportPath}', fileName='{FileName}', dryRun={DryRun}", deviceName, importPath, fileName, dryRun);
            return Run<HmiOperationResult>("hmi_unified_graphiclists_import", () =>
                ImportListFile(deviceName, importPath, fileName, dryRun, "graphic lists",
                    (software, directory, name) => software.HmiGraphicLists.Import(directory, name),
                    software => software.HmiGraphicLists.Count));
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Export all system text lists of a WinCC Unified HMI (the text lists TIA Portal provides itself, not the user text lists of hmi_unified_textlists_*), with their entries, to files in exportPath (YAML: <fileName>.hmi.yml and <fileName>.TextLibrary.hmi.yml). Returns the written files. Prerequisites: Project must be open, device must have WinCC Unified HMI software, exportPath must exist. Use hmi_unified_systemtextlists_import to reverse this operation.")]
        public CallToolResult hmi_unified_systemtextlists_export(
            [Description(DeviceDescription)] string deviceName,
            [Description("Directory to write the export file to")] string exportPath,
            [Description(ExportFileNameDescription + " e.g. 'SystemTextLists'")] string fileName)
        {
            _logger.LogInformation("hmi_unified_systemtextlists_export called with deviceName='{DeviceName}', exportPath='{ExportPath}', fileName='{FileName}'", deviceName, exportPath, fileName);
            return Run<HmiOperationResult>("hmi_unified_systemtextlists_export", () =>
                ExportListFile(deviceName, exportPath, fileName, "system text lists",
                    (software, directory, name) => software.HmiSystemTextLists.Export(directory, name),
                    software => software.HmiSystemTextLists.Count));
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Import system text lists into a WinCC Unified HMI from a file written by hmi_unified_systemtextlists_export (or edited from one), updating their texts. Caution: in TIA Portal V21 this import was seen to double the '@@' escapes in texts such as '@@5%Y-%m-%d %H:%M:%S@@'; export again and compare before saving. Single '@' placeholders such as '@3%s@' are not affected, so do not edit the file to compensate. The result's warnings list every line of the file and of its <name>.TextLibrary.hmi.yml that holds '@@', also with dryRun=true. Prerequisites: Project must be open, device must have WinCC Unified HMI software, the file must exist in importPath. Use dryRun=true to check the file without importing." + SaveNote)]
        public CallToolResult hmi_unified_systemtextlists_import(
            [Description(DeviceDescription)] string deviceName,
            [Description("Directory containing the import file")] string importPath,
            [Description(ImportFileNameDescription + " e.g. 'SystemTextLists'")] string fileName,
            [Description("If true, only validate without importing")] bool dryRun = false)
        {
            _logger.LogInformation("hmi_unified_systemtextlists_import called with deviceName='{DeviceName}', importPath='{ImportPath}', fileName='{FileName}', dryRun={DryRun}", deviceName, importPath, fileName, dryRun);
            return Run<HmiOperationResult>("hmi_unified_systemtextlists_import", () =>
                ImportListFile(deviceName, importPath, fileName, dryRun, "system text lists",
                    (software, directory, name) => software.HmiSystemTextLists.Import(directory, name),
                    software => software.HmiSystemTextLists.Count,
                    file => HmiExchangeFiles.DoubleEscapeWarnings(HmiExchangeFiles.SystemTextListFiles(file))));
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Export the HMI tags of a WinCC Unified HMI per tag table, into one directory per table: exportPath/<tag table group path>/<table name>/ (TIA Portal V21 cannot export all tags of a device at once). Exports all tables, one table (tagTableName) or one tag (tagName). A table that fails does not stop the others. Returns the written files and one line per table. Prerequisites: Project must be open, device must have WinCC Unified HMI software, exportPath must exist. Use hmi_unified_tags_import to reverse this operation.")]
        public CallToolResult hmi_unified_tags_export(
            [Description(DeviceDescription)] string deviceName,
            [Description("Existing directory to write the tag table directories to")] string exportPath,
            [Description("Name of a single HMI tag to export. Leave empty to export all tags")] string tagName = "",
            [Description("Name of a single tag table to export. Leave empty for all tag tables")] string tagTableName = "")
        {
            _logger.LogInformation("hmi_unified_tags_export called with deviceName='{DeviceName}', exportPath='{ExportPath}', tagName='{TagName}', tagTableName='{TagTableName}'", deviceName, exportPath, tagName, tagTableName);
            return Run<HmiOperationResult>("hmi_unified_tags_export", () =>
            {
                if (string.IsNullOrWhiteSpace(exportPath) || !Directory.Exists(exportPath))
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, $"Export directory '{exportPath}' does not exist");
                }

                var software = ResolveSoftware(deviceName, out var error);
                if (software == null)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(error.Code, error.Message);
                }

                var root = Path.GetFullPath(exportPath);
                var name = (tagName ?? string.Empty).Trim();
                var tableFilter = (tagTableName ?? string.Empty).Trim();
                var selected = new List<(HmiTagTable Table, string GroupPath)>();
                foreach (var entry in HmiUnifiedService.GetTagTablesWithGroupPaths(software))
                {
                    if ((tableFilter.Length == 0 || string.Equals(entry.Table.Name, tableFilter, StringComparison.Ordinal)) &&
                        (name.Length == 0 || entry.Table.Tags.Find(name) != null))
                    {
                        selected.Add(entry);
                    }
                }

                if (selected.Count == 0 && (name.Length > 0 || tableFilter.Length > 0))
                {
                    var missing = name.Length > 0 ? $"HMI tag '{name}'" : $"Tag table '{tableFilter}'";
                    var scope = name.Length > 0 && tableFilter.Length > 0 ? $" in tag table '{tableFilter}'" : string.Empty;
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, $"{missing} not found{scope} on '{deviceName}'");
                }

                var files = new List<string>();
                var result = new HmiOperationResult
                {
                    DeviceName = deviceName,
                    Target = name.Length > 0 ? name : tableFilter.Length > 0 ? tableFilter : "HMI tags",
                    Files = files
                };

                var failed = 0;
                foreach (var entry in selected)
                {
                    var label = ExchangePaths.CombineGroupPath(entry.GroupPath, entry.Table.Name);
                    var directory = new DirectoryInfo(HmiExchangeFiles.TagTableDirectory(root, entry.GroupPath, entry.Table.Name));
                    try
                    {
                        directory.Create();
                        var written = name.Length == 0 ? entry.Table.Tags.Export(directory) : entry.Table.Tags.Export(directory, name);
                        var count = 0;
                        foreach (var file in written ?? Enumerable.Empty<FileInfo>())
                        {
                            files.Add(file.FullName);
                            count++;
                        }
                        result.Changes.Add(name.Length == 0
                            ? $"Exported tag table '{label}' ({entry.Table.Tags.Count} tags) to {count} files"
                            : $"Exported HMI tag '{name}' of tag table '{label}' to {count} files");
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        _logger.LogError(ex, "Failed to export HMI tag table {TagTable}", label);
                        result.Changes.Add($"Failed to export tag table '{label}': {ex.Message}");
                    }
                }

                if (selected.Count > 0 && failed == selected.Count)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.TiaError, $"No tag table could be exported. {result.Changes[0]}");
                }

                if (selected.Count == 0)
                {
                    result.Changes.Add($"'{deviceName}' has no tag tables");
                }
                return ToolResponse<HmiOperationResult>.CreateSuccess(result);
            });
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Import HMI tags into a WinCC Unified HMI from the tag table directories written by hmi_unified_tags_export: each directory below importPath that holds files is imported into the tag table of the same name. An existing table of that name is used wherever it is; otherwise the table is created in the tag table group of the directory's parent path, creating missing groups. Imports all tables, one table (tagTableName) or one tag (tagName, which needs tagTableName when importPath holds several tables). Existing tags of the same name are updated. A table that fails does not stop the others. Prerequisites: Project must be open, device must have WinCC Unified HMI software, importPath must exist. Use dryRun=true to list the tables and files without importing." + SaveNote)]
        public CallToolResult hmi_unified_tags_import(
            [Description(DeviceDescription)] string deviceName,
            [Description("Directory written by hmi_unified_tags_export, holding one directory per tag table")] string importPath,
            [Description("Name of a single HMI tag to import. Leave empty to import all tags")] string tagName = "",
            [Description("If true, only list the tables and files without importing")] bool dryRun = false,
            [Description("Name of a single tag table to import. Leave empty for all tag table directories")] string tagTableName = "")
        {
            _logger.LogInformation("hmi_unified_tags_import called with deviceName='{DeviceName}', importPath='{ImportPath}', tagName='{TagName}', dryRun={DryRun}, tagTableName='{TagTableName}'", deviceName, importPath, tagName, dryRun, tagTableName);
            return Run<HmiOperationResult>("hmi_unified_tags_import", () =>
            {
                if (string.IsNullOrWhiteSpace(importPath) || !Directory.Exists(importPath))
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, $"Import directory '{importPath}' does not exist");
                }

                var root = new DirectoryInfo(Path.GetFullPath(importPath));
                var name = (tagName ?? string.Empty).Trim();
                var tableFilter = (tagTableName ?? string.Empty).Trim();
                var selected = new List<(DirectoryInfo Directory, string GroupPath, string TableName)>();
                foreach (var entry in HmiExchangeFiles.FindTagTableDirectories(root))
                {
                    if (tableFilter.Length == 0 || string.Equals(entry.TableName, ExchangePaths.ToFileName(tableFilter), StringComparison.Ordinal))
                    {
                        selected.Add(entry);
                    }
                }

                if (selected.Count == 0)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, tableFilter.Length > 0
                        ? $"No directory for tag table '{tableFilter}' below '{root.FullName}'"
                        : $"No tag table directories below '{root.FullName}'; expected <group path>/<table name>/ as written by hmi_unified_tags_export");
                }

                if (name.Length > 0 && selected.Count > 1)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter,
                        $"'{root.FullName}' holds {selected.Count} tag table directories; pass tagTableName to import a single tag");
                }

                var software = ResolveSoftware(deviceName, out var error);
                if (software == null)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(error.Code, error.Message);
                }

                var files = new List<string>();
                foreach (var entry in selected)
                {
                    foreach (var file in entry.Directory.GetFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        files.Add(file.FullName);
                    }
                }

                var result = new HmiOperationResult
                {
                    DeviceName = deviceName,
                    Target = name.Length > 0 ? name : tableFilter.Length > 0 ? tableFilter : "HMI tags",
                    DryRun = dryRun,
                    Files = files
                };

                var failed = 0;
                foreach (var entry in selected)
                {
                    var label = ExchangePaths.CombineGroupPath(entry.GroupPath, entry.TableName);
                    try
                    {
                        var table = FindTagTable(software, entry.TableName);
                        if (dryRun)
                        {
                            result.Changes.Add(table != null
                                ? $"Import '{entry.Directory.FullName}' into tag table '{entry.TableName}'"
                                : $"Create tag table '{label}' and import '{entry.Directory.FullName}' into it");
                            continue;
                        }

                        if (table == null)
                        {
                            table = CreateTagTable(software, entry.GroupPath, entry.TableName);
                            result.Changes.Add($"Created tag table '{label}'");
                        }

                        var before = table.Tags.Count;
                        var imported = name.Length == 0 ? table.Tags.Import(entry.Directory) : table.Tags.Import(entry.Directory, name);
                        if (!imported)
                        {
                            failed++;
                            result.Changes.Add($"TIA Portal reported that importing '{entry.Directory.FullName}' into tag table '{entry.TableName}' failed");
                            continue;
                        }

                        result.Changes.Add($"Imported '{entry.Directory.FullName}' into tag table '{entry.TableName}': {table.Tags.Count} tags ({before} before)");
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        _logger.LogError(ex, "Failed to import HMI tag table {TagTable}", label);
                        result.Changes.Add($"Failed to import tag table '{label}': {ex.Message}");
                    }
                }

                if (failed == selected.Count)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.TiaError, $"No tag table could be imported. {result.Changes[result.Changes.Count - 1]}");
                }
                return ToolResponse<HmiOperationResult>.CreateSuccess(result);
            });
        }

        /// <summary>
        /// The tag table of this name in any tag table group; names are unique per HMI.
        /// </summary>
        private static HmiTagTable? FindTagTable(HmiSoftware software, string tableName)
        {
            foreach (var entry in HmiUnifiedService.GetTagTablesWithGroupPaths(software))
            {
                if (string.Equals(entry.Table.Name, tableName, StringComparison.Ordinal))
                {
                    return entry.Table;
                }
            }
            return null;
        }

        /// <summary>
        /// Creates a tag table in the tag table group of the given path, creating missing groups.
        /// </summary>
        private static HmiTagTable CreateTagTable(HmiSoftware software, string groupPath, string tableName)
        {
            var segments = ExchangePaths.CombineGroupPath(groupPath).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return software.TagTables.Create(tableName);
            }

            var group = software.TagTableGroups.Find(segments[0]) ?? software.TagTableGroups.Create(segments[0]);
            for (var i = 1; i < segments.Length; i++)
            {
                group = group.Groups.Find(segments[i]) ?? group.Groups.Create(segments[i]);
            }
            return group.TagTables.Create(tableName);
        }

        #endregion

        #region Script modules

        [McpServerTool(ReadOnly = true, Idempotent = true), Description("List the global script modules of a WinCC Unified HMI by name. Their code is read and written with hmi_unified_scripts_export and hmi_unified_scripts_import. Prerequisites: Project must be open, device must have WinCC Unified HMI software.")]
        public CallToolResult hmi_unified_scripts_list(
            [Description(DeviceDescription)] string deviceName)
        {
            _logger.LogInformation("hmi_unified_scripts_list called with deviceName='{DeviceName}'", deviceName);
            return Run<object>("hmi_unified_scripts_list", () =>
            {
                var software = ResolveSoftware(deviceName, out var error);
                if (software == null)
                {
                    return ToolResponse<object>.CreateError(error.Code, error.Message);
                }

                var names = new List<string>();
                foreach (HmiScriptModule module in software.Scripts)
                {
                    names.Add(module.Name);
                }

                return ToolResponse<object>.CreateSuccess(new { deviceName, count = names.Count, scriptModules = names });
            });
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Export the global script modules of a WinCC Unified HMI to files in exportPath: all modules, or a single module by name. Returns the written files. Prerequisites: Project must be open, device must have WinCC Unified HMI software, exportPath must exist. Use hmi_unified_scripts_import to reverse this operation.")]
        public CallToolResult hmi_unified_scripts_export(
            [Description(DeviceDescription)] string deviceName,
            [Description("Directory to write the export files to")] string exportPath,
            [Description("Name of a single script module to export, as listed by hmi_unified_scripts_list. Leave empty to export all modules")] string moduleName = "",
            [Description("File name for a single exported module. Leave empty for TIA Portal's default name; only valid together with moduleName")] string fileName = "")
        {
            _logger.LogInformation("hmi_unified_scripts_export called with deviceName='{DeviceName}', exportPath='{ExportPath}', moduleName='{ModuleName}', fileName='{FileName}'", deviceName, exportPath, moduleName, fileName);
            return Run<HmiOperationResult>("hmi_unified_scripts_export", () =>
            {
                var name = (moduleName ?? string.Empty).Trim();
                var file = (fileName ?? string.Empty).Trim();
                if (file.Length > 0)
                {
                    if (name.Length == 0)
                    {
                        return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, "fileName can only be given together with moduleName");
                    }

                    if (!TryValidateFileName(file, out var fileError))
                    {
                        return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, fileError);
                    }
                }

                if (string.IsNullOrWhiteSpace(exportPath) || !Directory.Exists(exportPath))
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, $"Export directory '{exportPath}' does not exist");
                }

                var software = ResolveSoftware(deviceName, out var error);
                if (software == null)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(error.Code, error.Message);
                }

                var directory = new DirectoryInfo(exportPath);
                IEnumerable<FileInfo>? files;
                string summary;
                if (name.Length == 0)
                {
                    files = software.Scripts.Export(directory);
                    summary = $"Exported {software.Scripts.Count} script modules";
                }
                else
                {
                    var module = FindScriptModule(software, name);
                    if (module == null)
                    {
                        return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, $"Script module '{name}' not found on '{deviceName}'");
                    }

                    files = file.Length == 0 ? module.Export(directory) : module.Export(directory, file);
                    summary = $"Exported script module '{name}'";
                }

                var result = new HmiOperationResult
                {
                    DeviceName = deviceName,
                    Target = name.Length == 0 ? "script modules" : $"script module '{name}'",
                    Files = files?.Select(f => f.FullName).ToList() ?? new List<string>()
                };
                result.Changes.Add(summary);
                return ToolResponse<HmiOperationResult>.CreateSuccess(result);
            });
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Import global script modules into a WinCC Unified HMI from files written by hmi_unified_scripts_export (or edited from them) in importPath: all of them, or a single file by name. Existing modules of the same name are updated. Prerequisites: Project must be open, device must have WinCC Unified HMI software, importPath must exist. Use dryRun=true to see the files without importing." + SaveNote)]
        public CallToolResult hmi_unified_scripts_import(
            [Description(DeviceDescription)] string deviceName,
            [Description("Directory containing the files written by hmi_unified_scripts_export")] string importPath,
            [Description("Name of a single file in importPath to import, as returned by hmi_unified_scripts_export. Leave empty to import all files in the directory")] string fileName = "",
            [Description("If true, only validate without importing")] bool dryRun = false)
        {
            _logger.LogInformation("hmi_unified_scripts_import called with deviceName='{DeviceName}', importPath='{ImportPath}', fileName='{FileName}', dryRun={DryRun}", deviceName, importPath, fileName, dryRun);
            return Run<HmiOperationResult>("hmi_unified_scripts_import", () =>
            {
                if (string.IsNullOrWhiteSpace(importPath) || !Directory.Exists(importPath))
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, $"Import directory '{importPath}' does not exist");
                }

                var directory = new DirectoryInfo(importPath);
                var file = (fileName ?? string.Empty).Trim();
                List<string> files;
                if (file.Length == 0)
                {
                    files = directory.GetFiles().Select(f => f.FullName).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
                    if (files.Count == 0)
                    {
                        return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, $"Import directory '{importPath}' contains no files");
                    }
                }
                else
                {
                    if (!TryValidateFileName(file, out var fileError))
                    {
                        return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, fileError);
                    }

                    var single = new FileInfo(Path.Combine(directory.FullName, file));
                    if (!single.Exists)
                    {
                        return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, $"File '{single.FullName}' not found");
                    }

                    files = new List<string> { single.FullName };
                }

                var software = ResolveSoftware(deviceName, out var error);
                if (software == null)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(error.Code, error.Message);
                }

                var result = new HmiOperationResult
                {
                    DeviceName = deviceName,
                    Target = "script modules",
                    DryRun = dryRun,
                    Files = files
                };
                result.Changes.Add(file.Length == 0
                    ? $"Import script modules from '{directory.FullName}'"
                    : $"Import script modules from '{files[0]}'");
                if (dryRun)
                {
                    return ToolResponse<HmiOperationResult>.CreateSuccess(result);
                }

                var before = software.Scripts.Count;
                var imported = file.Length == 0 ? software.Scripts.Import(directory) : software.Scripts.Import(directory, file);
                if (!imported)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.TiaError, $"TIA Portal reported that importing script modules from '{directory.FullName}' failed");
                }

                result.Changes.Add($"{software.Scripts.Count} script modules after the import ({before} before)");
                return ToolResponse<HmiOperationResult>.CreateSuccess(result);
            });
        }

        #endregion

        #region Alarm and settings snapshots

        [McpServerTool(Destructive = false, Idempotent = true, OpenWorld = true), Description("Write a review snapshot of the alarm configuration of a WinCC Unified HMI for diffing in git: <exportPath>\\Alarms\\DiscreteAlarms.json, AnalogAlarms.json and AlarmClasses.json, each { \"items\": { \"<name>\": { ... } } } sorted by name, with every readable property: trigger tag and bit, acknowledgment tags and bits, alarm class, priority, alarm parameter tags and multilingual alarm texts as { \"<culture>\": \"<text>\" }. Same format as hmi_unified_screens_snapshot: sorted keys, UTF-8 without BOM, LF line endings, unchanged files not rewritten, unreadable properties under \"@unreadable\". Snapshots cannot be imported; the project is not changed. Prerequisites: Project must be open, device must have WinCC Unified HMI software, exportPath must exist.")]
        public CallToolResult hmi_unified_alarms_snapshot(
            [Description(DeviceDescription)] string deviceName,
            [Description("Directory to write the snapshot to; the files go to its Alarms subdirectory")] string exportPath)
        {
            _logger.LogInformation("hmi_unified_alarms_snapshot called with deviceName='{DeviceName}', exportPath='{ExportPath}'", deviceName, exportPath);
            return Run<HmiCollectionSnapshotResult>("hmi_unified_alarms_snapshot", () =>
                SnapshotCollections(deviceName, exportPath, "Alarms", software => new List<(string Name, Func<object?> Read, bool Single)>
                {
                    ("DiscreteAlarms", () => software.DiscreteAlarms, false),
                    ("AnalogAlarms", () => software.AnalogAlarms, false),
                    ("AlarmClasses", () => software.AlarmClasses, false)
                }));
        }

        [McpServerTool(Destructive = false, Idempotent = true, OpenWorld = true), Description("Write a review snapshot of the connections, logs and runtime settings of a WinCC Unified HMI for diffing in git: <exportPath>\\Settings\\Connections.json (with driver properties), DataLogs.json, AlarmLogs.json and AuditTrails.json, each { \"items\": { \"<name>\": { ... } } } sorted by name, and RuntimeSettings.json with the runtime settings object. Same format as hmi_unified_screens_snapshot: sorted keys, UTF-8 without BOM, LF line endings, unchanged files not rewritten, unreadable properties under \"@unreadable\"; collections the device does not support are reported under unreadable. Snapshots cannot be imported; the project is not changed. Prerequisites: Project must be open, device must have WinCC Unified HMI software, exportPath must exist.")]
        public CallToolResult hmi_unified_settings_snapshot(
            [Description(DeviceDescription)] string deviceName,
            [Description("Directory to write the snapshot to; the files go to its Settings subdirectory")] string exportPath)
        {
            _logger.LogInformation("hmi_unified_settings_snapshot called with deviceName='{DeviceName}', exportPath='{ExportPath}'", deviceName, exportPath);
            return Run<HmiCollectionSnapshotResult>("hmi_unified_settings_snapshot", () =>
                SnapshotCollections(deviceName, exportPath, "Settings", software => new List<(string Name, Func<object?> Read, bool Single)>
                {
                    ("Connections", () => software.Connections, false),
                    ("DataLogs", () => software.DataLogs, false),
                    ("AlarmLogs", () => software.AlarmLogs, false),
                    ("AuditTrails", () => software.AuditTrails, false),
                    ("RuntimeSettings", () => software.RuntimeSettings, true)
                }));
        }

        /// <summary>
        /// Shared body of the alarm and settings snapshots: one file per source in exportPath/folder, either a collection of
        /// named objects or (Single) one object. A source the device does not support is reported as unreadable and skipped.
        /// </summary>
        private ToolResponse<HmiCollectionSnapshotResult> SnapshotCollections(
            string deviceName,
            string exportPath,
            string folder,
            Func<HmiSoftware, List<(string Name, Func<object?> Read, bool Single)>> sources)
        {
            if (string.IsNullOrWhiteSpace(exportPath) || !Directory.Exists(exportPath))
            {
                return ToolResponse<HmiCollectionSnapshotResult>.CreateError(ErrorCodes.InvalidParameter, $"Export directory '{exportPath}' does not exist");
            }

            var software = ResolveSoftware(deviceName, out var error);
            if (software == null)
            {
                return ToolResponse<HmiCollectionSnapshotResult>.CreateError(error.Code, error.Message);
            }

            var result = new HmiCollectionSnapshotResult
            {
                DeviceName = deviceName,
                SnapshotDirectory = Path.Combine(Path.GetFullPath(exportPath), folder)
            };
            var unreadable = new List<string>();

            foreach (var source in sources(software))
            {
                try
                {
                    var value = source.Read();
                    if (value == null)
                    {
                        unreadable.Add($"{source.Name}: not available on '{deviceName}'");
                        continue;
                    }

                    var serializer = CreateSnapshotSerializer(source.Name);
                    JObject snapshot;
                    if (source.Single)
                    {
                        snapshot = serializer.Serialize(value, source.Name, HmiSnapshotSerializer.ObjectFormat);
                        result.Counts[source.Name] = 1;
                    }
                    else
                    {
                        snapshot = serializer.SerializeNamed((System.Collections.IEnumerable)value, source.Name);
                        result.Counts[source.Name] = ((JObject)snapshot["items"]!).Count;
                    }

                    WriteSnapshot(result.SnapshotDirectory, source.Name, snapshot, serializer, result, unreadable);
                }
                catch (EngineeringException ex)
                {
                    unreadable.Add($"{source.Name}: {ex.Message}");
                }
            }

            CompleteSnapshot(result, unreadable);
            return ToolResponse<HmiCollectionSnapshotResult>.CreateSuccess(result);
        }

        #endregion

        #region Helpers

        private CallToolResult Run<T>(string toolName, Func<ToolResponse<T>> action)
        {
            try
            {
                return McpToolResults.From(action());
            }
            catch (COMException comEx)
            {
                _logger.LogError(comEx, "COM error in {ToolName}", toolName);
                return McpToolResults.From(ToolResponse<T>.CreateError(ErrorCodes.ComError, $"COM error: {comEx.Message}", comEx.ToString()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in {ToolName}", toolName);
                return McpToolResults.From(ToolResponse<T>.CreateError(ErrorCodes.TiaError, $"Error: {ex.Message}", ex.ToString()));
            }
        }

        private HmiSoftware? ResolveSoftware(string deviceName, out (string Code, string Message) error)
        {
            error = (string.Empty, string.Empty);
            var project = _sessionManager.CurrentProject;
            if (project == null)
            {
                error = (ErrorCodes.NoProject, "No project is currently open. Use projects_open first.");
                return null;
            }

            var device = _sessionManager.PortalService.FindDevice(project, deviceName);
            if (device == null)
            {
                error = (ErrorCodes.DeviceNotFound, $"Device '{deviceName}' not found");
                return null;
            }

            var software = HmiUnifiedService.GetHmiSoftware(device);
            if (software == null)
            {
                error = (ErrorCodes.TiaError, $"Device '{deviceName}' does not have WinCC Unified HMI software. Use hmi_unified_software_list to find WinCC Unified devices");
                return null;
            }

            return software;
        }

        private HmiScreenItemBase? ResolveScreenItem(string deviceName, string screenName, string itemName, out (string Code, string Message) error)
        {
            var software = ResolveSoftware(deviceName, out error);
            if (software == null)
            {
                return null;
            }

            var found = HmiUnifiedService.FindScreen(software, screenName);
            if (found.Screen == null)
            {
                error = (ErrorCodes.InvalidParameter, $"Screen '{screenName}' not found");
                return null;
            }

            var item = found.Screen.ScreenItems.Find(itemName);
            if (item == null)
            {
                error = (ErrorCodes.InvalidParameter, $"Screen '{screenName}' has no item named '{itemName}'");
                return null;
            }

            return item;
        }

        private static bool TagTableExists(HmiSoftware software, string tagTableName)
        {
            foreach (var entry in HmiUnifiedService.GetTagTablesWithGroupPaths(software))
            {
                if (entry.Table.Name == tagTableName)
                {
                    return true;
                }
            }

            return false;
        }

        private static HmiUnifiedTagInfo CreateTagInfo(HmiTag tag)
        {
            return new HmiUnifiedTagInfo
            {
                Name = tag.Name,
                TagTableName = ReadString(tag, "TagTableName"),
                DataType = ReadString(tag, "DataType"),
                HmiDataType = ReadString(tag, "HmiDataType"),
                Connection = ReadString(tag, "Connection"),
                PlcTag = ReadString(tag, "PlcTag"),
                Address = ReadString(tag, "Address"),
                AcquisitionCycle = ReadString(tag, "AcquisitionCycle"),
                AcquisitionMode = ReadString(tag, "AcquisitionMode")
            };
        }

        /// <summary>
        /// Reads an attribute as text; null when it is empty or not readable on this object.
        /// </summary>
        private static string? ReadString(IEngineeringObject target, string attributeName)
        {
            try
            {
                var value = target.GetAttribute(attributeName)?.ToString();
                return string.IsNullOrEmpty(value) ? null : value;
            }
            catch (EngineeringException)
            {
                return null;
            }
        }

        /// <summary>
        /// Shared body of the text list, graphic list and system text list export tools: one file holding every list of that kind.
        /// </summary>
        private ToolResponse<HmiOperationResult> ExportListFile(
            string deviceName,
            string exportPath,
            string fileName,
            string listKind,
            Func<HmiSoftware, DirectoryInfo, string, IEnumerable<FileInfo>?> export,
            Func<HmiSoftware, int> count)
        {
            if (!TryValidateFileName(fileName, out var fileError))
            {
                return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, fileError);
            }

            if (string.IsNullOrWhiteSpace(exportPath) || !Directory.Exists(exportPath))
            {
                return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, $"Export directory '{exportPath}' does not exist");
            }

            var software = ResolveSoftware(deviceName, out var error);
            if (software == null)
            {
                return ToolResponse<HmiOperationResult>.CreateError(error.Code, error.Message);
            }

            var files = export(software, new DirectoryInfo(exportPath), HmiExchangeFiles.ExportBaseName(fileName));
            var result = new HmiOperationResult
            {
                DeviceName = deviceName,
                Target = listKind,
                Files = files?.Select(f => f.FullName).ToList() ?? new List<string>()
            };
            result.Changes.Add($"Exported {count(software)} {listKind}");
            return ToolResponse<HmiOperationResult>.CreateSuccess(result);
        }

        /// <summary>
        /// Shared body of the text list, graphic list and system text list import tools.
        /// </summary>
        private ToolResponse<HmiOperationResult> ImportListFile(
            string deviceName,
            string importPath,
            string fileName,
            bool dryRun,
            string listKind,
            Func<HmiSoftware, DirectoryInfo, string, bool> import,
            Func<HmiSoftware, int> count,
            Func<FileInfo, List<string>>? checkFile = null)
        {
            if (!TryValidateFileName(fileName, out var fileError))
            {
                return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, fileError);
            }

            if (string.IsNullOrWhiteSpace(importPath))
            {
                return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, "importPath is required");
            }

            var file = HmiExchangeFiles.FindImportFile(importPath, fileName);
            if (file == null)
            {
                var expected = Path.GetFullPath(Path.Combine(importPath, fileName.Trim()));
                return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter,
                    $"Neither '{expected}' nor '{expected}{HmiExchangeFiles.YamlExtension}' exists");
            }

            var software = ResolveSoftware(deviceName, out var error);
            if (software == null)
            {
                return ToolResponse<HmiOperationResult>.CreateError(error.Code, error.Message);
            }

            var result = new HmiOperationResult { DeviceName = deviceName, Target = listKind, DryRun = dryRun, Files = new List<string> { file.FullName } };
            result.Changes.Add($"Import {listKind} from '{file.FullName}'");
            var warnings = checkFile?.Invoke(file);
            if (warnings != null && warnings.Count > 0)
            {
                result.Warnings = warnings;
            }

            if (dryRun)
            {
                return ToolResponse<HmiOperationResult>.CreateSuccess(result);
            }

            if (!import(software, file.Directory!, file.Name))
            {
                return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.TiaError, $"TIA Portal reported that importing '{file.FullName}' failed");
            }

            result.Changes.Add($"{count(software)} {listKind} after the import");
            return ToolResponse<HmiOperationResult>.CreateSuccess(result);
        }

        private static HmiScriptModule? FindScriptModule(HmiSoftware software, string moduleName)
        {
            foreach (HmiScriptModule module in software.Scripts)
            {
                if (string.Equals(module.Name, moduleName, StringComparison.Ordinal))
                {
                    return module;
                }
            }

            return null;
        }

        /// <summary>
        /// A serializer whose script sidecars go to "&lt;name&gt;.scripts" next to "&lt;name&gt;.json".
        /// </summary>
        private HmiSnapshotSerializer CreateSnapshotSerializer(string name)
        {
            // Only objects owned by the object being read are read; references to other engineering objects are written
            // as { "@type", "@ref" }. Reading every reachable object crashed TIA Portal V21 on a screen snapshot.
            return new HmiSnapshotSerializer(
                ExchangePaths.ToFileName(name) + ".scripts",
                HmiUnifiedService.ConvertSnapshotValue,
                HmiUnifiedService.IsSnapshotChild,
                path => _logger.LogDebug("Snapshot: reading {Path}", path));
        }

        /// <summary>
        /// Writes "&lt;name&gt;.json" and its script sidecars to <paramref name="directory"/>, recording files and unreadable properties.
        /// </summary>
        private static void WriteSnapshot(string directory, string name, JObject snapshot, HmiSnapshotSerializer serializer, HmiSnapshotResult result, List<string> unreadable)
        {
            WriteSnapshotFile(Path.Combine(directory, ExchangePaths.ToFileName(name) + ".json"), HmiSnapshotSerializer.ToJson(snapshot), result);
            foreach (var sidecar in serializer.Sidecars)
            {
                WriteSnapshotFile(Path.Combine(directory, sidecar.RelativePath.Replace('/', Path.DirectorySeparatorChar)), sidecar.Content, result);
            }

            unreadable.AddRange(serializer.Unreadable);
        }

        private static void CompleteSnapshot(HmiSnapshotResult result, List<string> unreadable)
        {
            result.Files = result.Files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            result.UnreadableCount = unreadable.Count;
            if (unreadable.Count > 0)
            {
                result.Unreadable = unreadable.Take(MaxReportedUnreadable).ToList();
            }
        }

        private static void WriteSnapshotFile(string path, string content, HmiSnapshotResult result)
        {
            if (HmiSnapshotSerializer.WriteIfChanged(path, content))
            {
                result.WrittenCount++;
            }
            else
            {
                result.UnchangedCount++;
            }

            result.Files.Add(path);
        }

        private static bool TryValidateFileName(string fileName, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                errorMessage = $"fileName '{fileName}' must be a plain file name without directories";
                return false;
            }

            return true;
        }

        private static string DisplayPath(string groupPath)
        {
            return string.IsNullOrEmpty(groupPath) ? "(top level)" : groupPath;
        }

        #endregion
    }
}
