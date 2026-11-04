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

            var device = GetDevice(project, deviceName);
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

        private Device? GetDevice(Project project, string devicePath)
        {
            if (project == null)
            {
                return null;
            }

            // Simplified: assume devicePath is device name
            return project.Devices.FirstOrDefault(d => d.Name == devicePath);
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
            var block = blockGroup.Blocks.Find(blockName);
            if (block != null)
            {
                return block;
            }

            foreach (PlcBlockUserGroup userGroup in blockGroup.Groups)
            {
                block = FindBlock(userGroup, blockName);
                if (block != null)
                {
                    return block;
                }
            }

            return null;
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
