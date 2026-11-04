using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;

namespace TiaPortalMcpServer.Services
{
    /// <summary>
    /// The PLC software a tool works on: either the device's main PLC software or one of its Software Units.
    /// Exposes the same group members as <see cref="PlcSoftware"/>, so tools use it the same way for both.
    /// </summary>
    public sealed class PlcScope
    {
        public PlcScope(string deviceName, PlcSoftware software, PlcUnitBase? unit)
        {
            DeviceName = deviceName;
            Software = software;
            Unit = unit;
        }

        public string DeviceName { get; }

        /// <summary>
        /// The device's PLC software. Holds CPU-wide objects such as system data types, even when <see cref="Unit"/> is set.
        /// </summary>
        public PlcSoftware Software { get; }

        /// <summary>
        /// The Software Unit (standard or safety) to work on, or null for the main PLC software.
        /// </summary>
        public PlcUnitBase? Unit { get; }

        /// <summary>
        /// "Device" or "Device/Unit", for messages and logs.
        /// </summary>
        public string DisplayName => Unit == null ? DeviceName : $"{DeviceName}/{Unit.Name}";

        public PlcBlockSystemGroup BlockGroup => Unit != null ? Unit.BlockGroup : Software.BlockGroup;

        public PlcTypeSystemGroup TypeGroup => Unit != null ? Unit.TypeGroup : Software.TypeGroup;

        public PlcTagTableSystemGroup TagTableGroup => Unit != null ? Unit.TagTableGroup : Software.TagTableGroup;

        public PlcExternalSourceSystemGroup ExternalSourceGroup => Unit != null ? Unit.ExternalSourceGroup : Software.ExternalSourceGroup;
    }
}
