using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;
using TiaPortalMcpServer.Models;

namespace TiaPortalMcpServer.Services
{
    /// <summary>
    /// Manages the lifecycle of a singleton TIA Portal instance
    /// </summary>
    public class TiaPortalService
    {
        private readonly ILogger<TiaPortalService> _logger;
        private TiaPortal? _tiaPortal;
        private readonly object _lock = new object();
        private bool _disposed = false;

        public TiaPortalService(ILogger<TiaPortalService> logger)
        {
            _logger = logger;
        }

        public bool IsPortalActive => _tiaPortal != null;

        public TiaPortal? CurrentPortal => _tiaPortal;

        public TiaPortal? GetOrCreatePortalInstance()
        {
            lock (_lock)
            {
                if (_tiaPortal == null)
                {
                    _logger.LogInformation("Creating new TIA Portal instance...");
                    try
                    {
                        var processes = TiaPortal.GetProcesses();
                        TiaPortalProcess firstProcess;
                        if (processes.Any())
                        {
                            firstProcess = processes.First();
                            _tiaPortal = firstProcess.Attach();
                        }
                        else
                        {
                            _tiaPortal = new TiaPortal(TiaPortalMode.WithoutUserInterface);
                        }
                    }
                    catch (COMException comEx)
                    {
                        _logger.LogError(comEx, "COM error creating TIA Portal instance");
                        throw new InvalidOperationException(
                            $"Failed to create TIA Portal instance: {comEx.Message}",
                            comEx
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error creating TIA Portal instance");
                        throw;
                    }
                }
                return _tiaPortal;
            }
        }

        public Project OpenProject(string projectPath)
        {
            lock (_lock)
            {
                var portal = GetOrCreatePortalInstance();

                if (!File.Exists(projectPath))
                {
                    throw new FileNotFoundException($"Project file not found: {projectPath}");
                }

                _logger.LogInformation("Opening project: {ProjectPath}", projectPath);

                try
                {
                    var fileInfo = new FileInfo(projectPath);
                    var project = portal.Projects.Open(fileInfo);
                    _logger.LogInformation("Project opened successfully: {ProjectName}", project.Name);
                    return project;
                }
                catch (COMException comEx)
                {
                    _logger.LogError(comEx, "COM error opening project: {ProjectPath}", projectPath);
                    throw new InvalidOperationException(
                        $"Failed to open project: {comEx.Message}",
                        comEx
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error opening project: {ProjectPath}", projectPath);
                    throw;
                }
            }
        }

        public Project OpenProjectWithUpgrade(string projectPath)
        {
            lock (_lock)
            {
                var portal = GetOrCreatePortalInstance();

                if (!File.Exists(projectPath))
                {
                    throw new FileNotFoundException($"Project file not found: {projectPath}");
                }

                _logger.LogInformation("Opening project with upgrade: {ProjectPath}", projectPath);

                try
                {
                    var fileInfo = new FileInfo(projectPath);
                    var project = portal.Projects.OpenWithUpgrade(fileInfo);
                    _logger.LogInformation("Project opened with upgrade successfully: {ProjectName}", project.Name);
                    return project;
                }
                catch (COMException comEx)
                {
                    _logger.LogError(comEx, "COM error opening project with upgrade: {ProjectPath}", projectPath);
                    throw new InvalidOperationException(
                        $"Failed to open project with upgrade: {comEx.Message}",
                        comEx
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error opening project with upgrade: {ProjectPath}", projectPath);
                    throw;
                }
            }
        }

        public void ArchiveProject(Project project, string targetDirectory, string targetName, Siemens.Engineering.ProjectArchivationMode mode = Siemens.Engineering.ProjectArchivationMode.None)
        {
            lock (_lock)
            {
                if (project == null)
                {
                    throw new ArgumentNullException(nameof(project));
                }

                if (!Directory.Exists(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                _logger.LogInformation("Archiving project '{ProjectName}' to '{TargetDirectory}' with name '{TargetName}'", project.Name, targetDirectory, targetName);

                try
                {
                    project.Archive(new DirectoryInfo(targetDirectory), targetName, mode);
                    _logger.LogInformation("Project archived successfully");
                }
                catch (COMException comEx)
                {
                    _logger.LogError(comEx, "COM error archiving project");
                    throw new InvalidOperationException(
                        $"Failed to archive project: {comEx.Message}",
                        comEx
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error archiving project");
                    throw;
                }
            }
        }

        public Project RetrieveProject(string sourcePath, string targetDirectory, bool withUpgrade = false)
        {
            lock (_lock)
            {
                var portal = GetOrCreatePortalInstance();

                if (!File.Exists(sourcePath))
                {
                    throw new FileNotFoundException($"Archive file not found: {sourcePath}");
                }

                if (!Directory.Exists(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                _logger.LogInformation("Retrieving project from '{SourcePath}' to '{TargetDirectory}'", sourcePath, targetDirectory);

                try
                {
                    Project project;
                    if (withUpgrade)
                    {
                        project = portal.Projects.RetrieveWithUpgrade(new FileInfo(sourcePath), new DirectoryInfo(targetDirectory));
                    }
                    else
                    {
                        project = portal.Projects.Retrieve(new FileInfo(sourcePath), new DirectoryInfo(targetDirectory));
                    }
                    _logger.LogInformation("Project retrieved successfully: {ProjectName}", project.Name);
                    return project;
                }
                catch (COMException comEx)
                {
                    _logger.LogError(comEx, "COM error retrieving project");
                    throw new InvalidOperationException(
                        $"Failed to retrieve project: {comEx.Message}",
                        comEx
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving project");
                    throw;
                }
            }
        }

        public Project? GetCurrentProject()
        {
            if (_tiaPortal != null)
            {
                var projects = _tiaPortal.Projects;
                return projects.FirstOrDefault();
            }
            return null;
        }

        public PlcSoftware? GetPlcSoftware(Device device)
        {
            if (device == null)
            {
                return null;
            }

            DeviceItemComposition deviceItemComposition = device.DeviceItems;
            foreach (DeviceItem deviceItem in deviceItemComposition)
            {
                SoftwareContainer softwareContainer = deviceItem.GetService<SoftwareContainer>();
                if (softwareContainer != null)
                {
                    Software softwareBase = softwareContainer.Software;
                    PlcSoftware? plcSoftware = softwareBase as PlcSoftware;
                    return plcSoftware;
                }
            }
            return null;
        }

        /// <summary>
        /// Returns all blocks in the scope's block group and its user subgroups that pass the name filter.
        /// </summary>
        public List<PlcBlock> GetBlocks(PlcScope scope, string regexName = "")
        {
            var blocks = new List<PlcBlock>();
            GetRecursiveBlocks(scope.BlockGroup, blocks, regexName);
            return blocks;
        }

        /// <summary>
        /// Resolves the PLC software a tool works on: the device's main PLC software, or the named Software Unit
        /// when <paramref name="unitName"/> is not empty. On failure returns null and sets an error code and message.
        /// </summary>
        public PlcScope? ResolvePlcScope(Project project, string deviceName, string? unitName, out string errorCode, out string errorMessage)
        {
            errorCode = string.Empty;
            errorMessage = string.Empty;

            var device = FindDevice(project, deviceName);
            if (device == null)
            {
                errorCode = ErrorCodes.DeviceNotFound;
                errorMessage = $"Device '{deviceName}' not found";
                return null;
            }

            var plcSoftware = GetPlcSoftware(device);
            if (plcSoftware == null)
            {
                errorCode = ErrorCodes.TiaError;
                errorMessage = $"Device '{deviceName}' does not have PLC software";
                return null;
            }

            if (string.IsNullOrWhiteSpace(unitName))
            {
                return new PlcScope(deviceName, plcSoftware, null);
            }

            var unit = FindSoftwareUnit(plcSoftware, unitName!.Trim());
            if (unit == null)
            {
                var available = (GetSoftwareUnits(plcSoftware) ?? new List<PlcUnitBase>()).Select(u => u.Name).ToList();
                errorCode = ErrorCodes.SoftwareUnitNotFound;
                errorMessage = $"Software Unit '{unitName}' not found on device '{deviceName}'. Available units: " +
                    (available.Count > 0 ? string.Join(", ", available) : "none");
                return null;
            }

            return new PlcScope(deviceName, plcSoftware, unit);
        }

        /// <summary>
        /// Lists the standard and safety Software Units of the PLC software, or returns null when the CPU does not support Software Units.
        /// </summary>
        public IList<PlcUnitBase>? GetSoftwareUnits(PlcSoftware plcSoftware)
        {
            var unitGroup = plcSoftware.GetService<PlcUnitProvider>()?.UnitGroup;
            if (unitGroup == null)
            {
                return null;
            }

            var units = new List<PlcUnitBase>();
            foreach (PlcUnit unit in unitGroup.Units)
            {
                units.Add(unit);
            }

            foreach (PlcSafetyUnit safetyUnit in unitGroup.SafetyUnits)
            {
                units.Add(safetyUnit);
            }

            return units;
        }

        /// <summary>
        /// Finds a standard or safety Software Unit by exact name.
        /// </summary>
        public PlcUnitBase? FindSoftwareUnit(PlcSoftware plcSoftware, string unitName)
        {
            var unitGroup = plcSoftware.GetService<PlcUnitProvider>()?.UnitGroup;
            if (unitGroup == null)
            {
                return null;
            }

            return (PlcUnitBase?)unitGroup.Units.Find(unitName) ?? unitGroup.SafetyUnits.Find(unitName);
        }

        /// <summary>
        /// Finds a device by exact name at the top level, among the ungrouped devices or in any device folder.
        /// </summary>
        public Device? FindDevice(Project project, string deviceName)
        {
            if (project == null)
            {
                return null;
            }

            foreach (var entry in GetDevicesWithGroupPaths(project))
            {
                if (entry.Device.Name == deviceName)
                {
                    return entry.Device;
                }
            }

            return null;
        }

        /// <summary>
        /// Every device of the project with the device folder it is in ("Line 1/Cell A"; empty at the top level).
        /// Covers top-level devices, ungrouped devices and devices in user folders.
        /// </summary>
        public List<(Device Device, string GroupPath)> GetDevicesWithGroupPaths(Project project)
        {
            var devices = new List<(Device Device, string GroupPath)>();
            var seen = new HashSet<Device>();

            foreach (Device device in project.Devices)
            {
                if (seen.Add(device))
                {
                    devices.Add((device, string.Empty));
                }
            }

            var ungrouped = project.UngroupedDevicesGroup;
            if (ungrouped != null)
            {
                foreach (Device device in ungrouped.Devices)
                {
                    if (seen.Add(device))
                    {
                        devices.Add((device, ungrouped.Name));
                    }
                }
            }

            foreach (DeviceUserGroup group in project.DeviceGroups)
            {
                CollectDevicesInGroup(group, group.Name, devices, seen);
            }

            return devices;
        }

        /// <summary>
        /// Every PLC software in the project, followed by its standard and safety Software Units.
        /// </summary>
        public List<PlcSoftwarePathInfo> GetPlcSoftwarePaths(Project project)
        {
            var paths = new List<PlcSoftwarePathInfo>();
            foreach (var entry in GetDevicesWithGroupPaths(project))
            {
                var plcSoftware = GetPlcSoftware(entry.Device);
                if (plcSoftware == null)
                {
                    continue;
                }

                var units = GetSoftwareUnits(plcSoftware);
                paths.Add(new PlcSoftwarePathInfo
                {
                    Path = PlcSoftwarePathInfo.FormatPath(entry.Device.Name, null),
                    DeviceName = entry.Device.Name,
                    Kind = PlcSoftwarePathInfo.KindPlcSoftware,
                    SoftwareName = plcSoftware.Name,
                    DeviceGroupPath = entry.GroupPath,
                    UnitsSupported = units != null
                });

                if (units != null)
                {
                    paths.AddRange(GetUnitPaths(entry.Device.Name, entry.GroupPath, plcSoftware, units));
                }
            }

            return paths;
        }

        /// <summary>
        /// Path entries for the given Software Units of a device's PLC software.
        /// </summary>
        public List<PlcSoftwarePathInfo> GetUnitPaths(string deviceName, string deviceGroupPath, PlcSoftware plcSoftware, IEnumerable<PlcUnitBase> units)
        {
            var paths = new List<PlcSoftwarePathInfo>();
            foreach (var unit in units)
            {
                paths.Add(new PlcSoftwarePathInfo
                {
                    Path = PlcSoftwarePathInfo.FormatPath(deviceName, unit.Name),
                    DeviceName = deviceName,
                    UnitName = unit.Name,
                    Kind = unit is PlcSafetyUnit ? PlcSoftwarePathInfo.KindSafetyUnit : PlcSoftwarePathInfo.KindSoftwareUnit,
                    SoftwareName = plcSoftware.Name,
                    DeviceGroupPath = deviceGroupPath
                });
            }

            return paths;
        }

        private static void CollectDevicesInGroup(DeviceUserGroup group, string groupPath, List<(Device Device, string GroupPath)> devices, HashSet<Device> seen)
        {
            foreach (Device device in group.Devices)
            {
                if (seen.Add(device))
                {
                    devices.Add((device, groupPath));
                }
            }

            foreach (DeviceUserGroup child in group.Groups)
            {
                CollectDevicesInGroup(child, ExchangePaths.CombineGroupPath(groupPath, child.Name), devices, seen);
            }
        }

        private void GetRecursiveBlocks(PlcBlockGroup blockGroup, List<PlcBlock> blocks, string regexName)
        {
            foreach (PlcBlock block in blockGroup.Blocks)
            {
                if (NameFilter.IsMatch(block.Name, regexName))
                {
                    blocks.Add(block);
                }
            }

            foreach (PlcBlockUserGroup userGroup in blockGroup.Groups)
            {
                GetRecursiveBlocksInUserGroup(userGroup, blocks, regexName);
            }
        }

        /// <summary>
        /// Finds a block by exact name in the given group or any of its user subgroups.
        /// </summary>
        public PlcBlock? FindBlock(PlcBlockGroup blockGroup, string blockName)
        {
            return FindBlockWithGroupPath(blockGroup, blockName).Block;
        }

        /// <summary>
        /// Resolves a block group path such as "Motion/Axis" relative to the given root group.
        /// An empty path returns the root group; a single segment is a direct child of the root.
        /// </summary>
        public PlcBlockGroup? FindBlockGroup(PlcBlockGroup rootGroup, string? groupPath)
        {
            if (string.IsNullOrWhiteSpace(groupPath))
            {
                return rootGroup;
            }

            PlcBlockGroup? current = rootGroup;
            foreach (var segment in groupPath!.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = current.Groups.Find(segment.Trim());
                if (current == null)
                {
                    return null;
                }
            }

            return current;
        }

        /// <summary>
        /// Resolves a block group path relative to the root group, creating missing user groups on the way.
        /// </summary>
        public PlcBlockGroup GetOrCreateBlockGroup(PlcBlockGroup rootGroup, string? groupPath)
        {
            var current = rootGroup;
            foreach (var segment in SplitGroupPath(groupPath))
            {
                current = (PlcBlockGroup?)current.Groups.Find(segment) ?? current.Groups.Create(segment);
            }

            return current;
        }

        /// <summary>
        /// Returns every block in the group and its user subgroups that passes the name filter, with its group path
        /// relative to <paramref name="blockGroup"/> (empty for blocks directly in it).
        /// </summary>
        public List<(PlcBlock Block, string GroupPath)> GetBlocksWithGroupPaths(PlcBlockGroup blockGroup, string regexName = "")
        {
            var blocks = new List<(PlcBlock Block, string GroupPath)>();
            CollectBlocksWithGroupPaths(blockGroup, string.Empty, regexName, blocks);
            return blocks;
        }

        /// <summary>
        /// Finds a block by exact name in the group or its user subgroups, with its group path relative to <paramref name="blockGroup"/>.
        /// </summary>
        public (PlcBlock? Block, string GroupPath) FindBlockWithGroupPath(PlcBlockGroup blockGroup, string blockName, string groupPath = "")
        {
            var block = blockGroup.Blocks.Find(blockName);
            if (block != null)
            {
                return (block, groupPath);
            }

            foreach (PlcBlockUserGroup userGroup in blockGroup.Groups)
            {
                var found = FindBlockWithGroupPath(userGroup, blockName, ExchangePaths.CombineGroupPath(groupPath, userGroup.Name));
                if (found.Block != null)
                {
                    return found;
                }
            }

            return (null, string.Empty);
        }

        /// <summary>
        /// Resolves a PLC data type group path such as "Motion/Axis" relative to the root type group. Empty returns the root.
        /// </summary>
        public PlcTypeGroup? FindTypeGroup(PlcTypeGroup rootGroup, string? groupPath)
        {
            PlcTypeGroup? current = rootGroup;
            foreach (var segment in SplitGroupPath(groupPath))
            {
                current = current.Groups.Find(segment);
                if (current == null)
                {
                    return null;
                }
            }

            return current;
        }

        /// <summary>
        /// Resolves a PLC data type group path relative to the root type group, creating missing user groups on the way.
        /// </summary>
        public PlcTypeGroup GetOrCreateTypeGroup(PlcTypeGroup rootGroup, string? groupPath)
        {
            var current = rootGroup;
            foreach (var segment in SplitGroupPath(groupPath))
            {
                current = (PlcTypeGroup?)current.Groups.Find(segment) ?? current.Groups.Create(segment);
            }

            return current;
        }

        /// <summary>
        /// Returns every PLC data type in the group and its user subgroups that passes the name filter, with its group path.
        /// </summary>
        public List<(PlcType Type, string GroupPath)> GetTypesWithGroupPaths(PlcTypeGroup typeGroup, string regexName = "")
        {
            var types = new List<(PlcType Type, string GroupPath)>();
            CollectTypesWithGroupPaths(typeGroup, string.Empty, regexName, types);
            return types;
        }

        /// <summary>
        /// Finds a PLC data type by exact name in the group or its user subgroups, with its group path.
        /// </summary>
        public (PlcType? Type, string GroupPath) FindTypeWithGroupPath(PlcTypeGroup typeGroup, string typeName, string groupPath = "")
        {
            var type = typeGroup.Types.Find(typeName);
            if (type != null)
            {
                return (type, groupPath);
            }

            foreach (PlcTypeUserGroup userGroup in typeGroup.Groups)
            {
                var found = FindTypeWithGroupPath(userGroup, typeName, ExchangePaths.CombineGroupPath(groupPath, userGroup.Name));
                if (found.Type != null)
                {
                    return found;
                }
            }

            return (null, string.Empty);
        }

        /// <summary>
        /// Resolves a PLC tag table group path such as "IO/Inputs" relative to the root tag table group. Empty returns the root.
        /// </summary>
        public PlcTagTableGroup? FindTagTableGroup(PlcTagTableGroup rootGroup, string? groupPath)
        {
            PlcTagTableGroup? current = rootGroup;
            foreach (var segment in SplitGroupPath(groupPath))
            {
                current = current.Groups.Find(segment);
                if (current == null)
                {
                    return null;
                }
            }

            return current;
        }

        /// <summary>
        /// Resolves a PLC tag table group path relative to the root tag table group, creating missing user groups on the way.
        /// </summary>
        public PlcTagTableGroup GetOrCreateTagTableGroup(PlcTagTableGroup rootGroup, string? groupPath)
        {
            var current = rootGroup;
            foreach (var segment in SplitGroupPath(groupPath))
            {
                current = (PlcTagTableGroup?)current.Groups.Find(segment) ?? current.Groups.Create(segment);
            }

            return current;
        }

        /// <summary>
        /// Returns every PLC tag table in the group and its user subgroups that passes the name filter, with its group path
        /// relative to <paramref name="tagTableGroup"/> (empty for tables directly in it, such as the default tag table).
        /// </summary>
        public List<(PlcTagTable Table, string GroupPath)> GetTagTablesWithGroupPaths(PlcTagTableGroup tagTableGroup, string regexName = "")
        {
            var tables = new List<(PlcTagTable Table, string GroupPath)>();
            CollectTagTablesWithGroupPaths(tagTableGroup, string.Empty, regexName, tables);
            return tables;
        }

        /// <summary>
        /// Finds a PLC tag table by exact name in the group or its user subgroups, with its group path.
        /// </summary>
        public (PlcTagTable? Table, string GroupPath) FindTagTableWithGroupPath(PlcTagTableGroup tagTableGroup, string tableName, string groupPath = "")
        {
            var table = tagTableGroup.TagTables.Find(tableName);
            if (table != null)
            {
                return (table, groupPath);
            }

            foreach (PlcTagTableUserGroup userGroup in tagTableGroup.Groups)
            {
                var found = FindTagTableWithGroupPath(userGroup, tableName, ExchangePaths.CombineGroupPath(groupPath, userGroup.Name));
                if (found.Table != null)
                {
                    return found;
                }
            }

            return (null, string.Empty);
        }

        private static void CollectTagTablesWithGroupPaths(PlcTagTableGroup tagTableGroup, string groupPath, string regexName, List<(PlcTagTable Table, string GroupPath)> tables)
        {
            foreach (PlcTagTable table in tagTableGroup.TagTables)
            {
                if (NameFilter.IsMatch(table.Name, regexName))
                {
                    tables.Add((table, groupPath));
                }
            }

            foreach (PlcTagTableUserGroup userGroup in tagTableGroup.Groups)
            {
                CollectTagTablesWithGroupPaths(userGroup, ExchangePaths.CombineGroupPath(groupPath, userGroup.Name), regexName, tables);
            }
        }

        private static IEnumerable<string> SplitGroupPath(string? groupPath)
        {
            return string.IsNullOrWhiteSpace(groupPath)
                ? Enumerable.Empty<string>()
                : groupPath!.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0);
        }

        private static void CollectBlocksWithGroupPaths(PlcBlockGroup blockGroup, string groupPath, string regexName, List<(PlcBlock Block, string GroupPath)> blocks)
        {
            foreach (PlcBlock block in blockGroup.Blocks)
            {
                if (NameFilter.IsMatch(block.Name, regexName))
                {
                    blocks.Add((block, groupPath));
                }
            }

            foreach (PlcBlockUserGroup userGroup in blockGroup.Groups)
            {
                CollectBlocksWithGroupPaths(userGroup, ExchangePaths.CombineGroupPath(groupPath, userGroup.Name), regexName, blocks);
            }
        }

        private static void CollectTypesWithGroupPaths(PlcTypeGroup typeGroup, string groupPath, string regexName, List<(PlcType Type, string GroupPath)> types)
        {
            foreach (PlcType type in typeGroup.Types)
            {
                if (NameFilter.IsMatch(type.Name, regexName))
                {
                    types.Add((type, groupPath));
                }
            }

            foreach (PlcTypeUserGroup userGroup in typeGroup.Groups)
            {
                CollectTypesWithGroupPaths(userGroup, ExchangePaths.CombineGroupPath(groupPath, userGroup.Name), regexName, types);
            }
        }

        private void GetRecursiveBlocksInUserGroup(PlcBlockUserGroup userGroup, List<PlcBlock> blocks, string regexName)
        {
            foreach (PlcBlock block in userGroup.Blocks)
            {
                if (NameFilter.IsMatch(block.Name, regexName))
                {
                    blocks.Add(block);
                }
            }

            foreach (PlcBlockUserGroup subGroup in userGroup.Groups)
            {
                GetRecursiveBlocksInUserGroup(subGroup, blocks, regexName);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            lock (_lock)
            {
                if (_tiaPortal != null)
                {
                    _logger.LogInformation("Disposing TIA Portal instance...");
                    try
                    {
                        _tiaPortal.Dispose();
                        _logger.LogInformation("TIA Portal instance disposed successfully");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error disposing TIA Portal instance");
                    }
                    finally
                    {
                        _tiaPortal = null;
                    }
                }

                _disposed = true;
            }
        }
    }
}
