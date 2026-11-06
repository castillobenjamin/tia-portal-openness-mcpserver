using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiConnections;
using Siemens.Engineering.HmiUnified.HmiTags;
using Siemens.Engineering.HmiUnified.TextGraphicList;
using Siemens.Engineering.HmiUnified.UI.Base;
using TiaPortalMcpServer.Models;
using TiaPortalMcpServer.Services;

namespace TiaPortalMcpServer
{
    /// <summary>
    /// WinCC Unified HMI tools: screens, screen items, HMI tags, connections and text lists.
    /// </summary>
    /// <remarks>
    /// Write tools change the open project in memory; call projects_save to persist the changes.
    /// </remarks>
    [McpServerToolType]
    public class HmiUnifiedTools
    {
        private const string DeviceDescription = "Name of the device with WinCC Unified HMI software (panel or PC runtime), as listed by hmi_unified_software_list";
        private const string AttributesDescription = "JSON object of attribute names and values, e.g. {\"Left\": 20, \"Top\": 40, \"Width\": 160, \"Height\": 40, \"Text\": \"Start\"}. Numbers, booleans, enum member names, colors ('#RRGGBB', '#AARRGGBB', 'R,G,B', 'A,R,G,B' or a name such as 'Red') and strings are converted to the attribute's type. For multilingual texts such as Text, a plain value sets every language and 'de-DE:Start' sets one";
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
                        TextListCount = software.HmiTextLists.Count
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

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Export all text lists of a WinCC Unified HMI, with their entries, to a file in exportPath (TIA Portal's text list exchange format). Returns the written files. Prerequisites: Project must be open, device must have WinCC Unified HMI software, exportPath must exist. Use hmi_unified_textlists_import to reverse this operation.")]
        public CallToolResult hmi_unified_textlists_export(
            [Description(DeviceDescription)] string deviceName,
            [Description("Directory to write the export file to")] string exportPath,
            [Description("Name of the export file, e.g. 'TextLists.xlsx'")] string fileName)
        {
            _logger.LogInformation("hmi_unified_textlists_export called with deviceName='{DeviceName}', exportPath='{ExportPath}', fileName='{FileName}'", deviceName, exportPath, fileName);
            return Run<HmiOperationResult>("hmi_unified_textlists_export", () =>
            {
                if (!TryValidateFileName(fileName, out var fileError))
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, fileError);
                }

                if (!Directory.Exists(exportPath))
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, $"Export directory '{exportPath}' does not exist");
                }

                var software = ResolveSoftware(deviceName, out var error);
                if (software == null)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(error.Code, error.Message);
                }

                var files = software.HmiTextLists.Export(new DirectoryInfo(exportPath), fileName.Trim());
                var result = new HmiOperationResult
                {
                    DeviceName = deviceName,
                    Target = "text lists",
                    Files = files?.Select(f => f.FullName).ToList() ?? new List<string>()
                };
                result.Changes.Add($"Exported {software.HmiTextLists.Count} text lists");
                return ToolResponse<HmiOperationResult>.CreateSuccess(result);
            });
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Import text lists with their entries into a WinCC Unified HMI from a file written by hmi_unified_textlists_export (or edited from one). Existing text lists of the same name are updated. Prerequisites: Project must be open, device must have WinCC Unified HMI software, the file must exist in importPath. Use dryRun=true to check the file without importing." + SaveNote)]
        public CallToolResult hmi_unified_textlists_import(
            [Description(DeviceDescription)] string deviceName,
            [Description("Directory containing the import file")] string importPath,
            [Description("Name of the import file, e.g. 'TextLists.xlsx'")] string fileName,
            [Description("If true, only validate without importing")] bool dryRun = false)
        {
            _logger.LogInformation("hmi_unified_textlists_import called with deviceName='{DeviceName}', importPath='{ImportPath}', fileName='{FileName}', dryRun={DryRun}", deviceName, importPath, fileName, dryRun);
            return Run<HmiOperationResult>("hmi_unified_textlists_import", () =>
            {
                if (!TryValidateFileName(fileName, out var fileError))
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, fileError);
                }

                var file = new FileInfo(Path.Combine(importPath, fileName.Trim()));
                if (!file.Exists)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, $"File '{file.FullName}' not found");
                }

                var software = ResolveSoftware(deviceName, out var error);
                if (software == null)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(error.Code, error.Message);
                }

                var result = new HmiOperationResult { DeviceName = deviceName, Target = "text lists", DryRun = dryRun, Files = new List<string> { file.FullName } };
                result.Changes.Add($"Import text lists from '{file.FullName}'");
                if (dryRun)
                {
                    return ToolResponse<HmiOperationResult>.CreateSuccess(result);
                }

                if (!software.HmiTextLists.Import(file.Directory!, file.Name))
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.TiaError, $"TIA Portal reported that importing '{file.FullName}' failed");
                }

                result.Changes.Add($"{software.HmiTextLists.Count} text lists after the import");
                return ToolResponse<HmiOperationResult>.CreateSuccess(result);
            });
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Export HMI tags of a WinCC Unified HMI to files in exportPath (TIA Portal's HMI tag exchange format): all tags, or a single tag by name. Returns the written files. Prerequisites: Project must be open, device must have WinCC Unified HMI software, exportPath must exist. Use hmi_unified_tags_import to reverse this operation.")]
        public CallToolResult hmi_unified_tags_export(
            [Description(DeviceDescription)] string deviceName,
            [Description("Directory to write the export files to")] string exportPath,
            [Description("Name of a single HMI tag to export. Leave empty to export all tags")] string tagName = "")
        {
            _logger.LogInformation("hmi_unified_tags_export called with deviceName='{DeviceName}', exportPath='{ExportPath}', tagName='{TagName}'", deviceName, exportPath, tagName);
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

                var directory = new DirectoryInfo(exportPath);
                var name = (tagName ?? string.Empty).Trim();
                IEnumerable<FileInfo>? files;
                string summary;
                if (name.Length == 0)
                {
                    files = software.Tags.Export(directory);
                    summary = $"Exported {software.Tags.Count} HMI tags";
                }
                else
                {
                    if (software.Tags.Find(name) == null)
                    {
                        return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, $"HMI tag '{name}' not found on '{deviceName}'");
                    }

                    files = software.Tags.Export(directory, name);
                    summary = $"Exported HMI tag '{name}'";
                }

                var result = new HmiOperationResult
                {
                    DeviceName = deviceName,
                    Target = name.Length == 0 ? "HMI tags" : name,
                    Files = files?.Select(f => f.FullName).ToList() ?? new List<string>()
                };
                result.Changes.Add(summary);
                return ToolResponse<HmiOperationResult>.CreateSuccess(result);
            });
        }

        [McpServerTool(Destructive = true, OpenWorld = true), Description("Import HMI tags into a WinCC Unified HMI from files written by hmi_unified_tags_export (or edited from them) in importPath: all of them, or a single tag by name. Existing tags of the same name are updated. Prerequisites: Project must be open, device must have WinCC Unified HMI software, importPath must exist. Use dryRun=true to see the files without importing." + SaveNote)]
        public CallToolResult hmi_unified_tags_import(
            [Description(DeviceDescription)] string deviceName,
            [Description("Directory containing the files written by hmi_unified_tags_export")] string importPath,
            [Description("Name of a single HMI tag to import. Leave empty to import all tags in the directory")] string tagName = "",
            [Description("If true, only validate without importing")] bool dryRun = false)
        {
            _logger.LogInformation("hmi_unified_tags_import called with deviceName='{DeviceName}', importPath='{ImportPath}', tagName='{TagName}', dryRun={DryRun}", deviceName, importPath, tagName, dryRun);
            return Run<HmiOperationResult>("hmi_unified_tags_import", () =>
            {
                if (string.IsNullOrWhiteSpace(importPath) || !Directory.Exists(importPath))
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, $"Import directory '{importPath}' does not exist");
                }

                var directory = new DirectoryInfo(importPath);
                var files = directory.GetFiles().Select(f => f.FullName).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
                if (files.Count == 0)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.InvalidParameter, $"Import directory '{importPath}' contains no files");
                }

                var software = ResolveSoftware(deviceName, out var error);
                if (software == null)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(error.Code, error.Message);
                }

                var name = (tagName ?? string.Empty).Trim();
                var result = new HmiOperationResult
                {
                    DeviceName = deviceName,
                    Target = name.Length == 0 ? "HMI tags" : name,
                    DryRun = dryRun,
                    Files = files
                };
                result.Changes.Add(name.Length == 0
                    ? $"Import HMI tags from '{directory.FullName}'"
                    : $"Import HMI tag '{name}' from '{directory.FullName}'");
                if (dryRun)
                {
                    return ToolResponse<HmiOperationResult>.CreateSuccess(result);
                }

                var before = software.Tags.Count;
                var imported = name.Length == 0 ? software.Tags.Import(directory) : software.Tags.Import(directory, name);
                if (!imported)
                {
                    return ToolResponse<HmiOperationResult>.CreateError(ErrorCodes.TiaError, $"TIA Portal reported that importing HMI tags from '{directory.FullName}' failed");
                }

                result.Changes.Add($"{software.Tags.Count} HMI tags after the import ({before} before)");
                return ToolResponse<HmiOperationResult>.CreateSuccess(result);
            });
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
