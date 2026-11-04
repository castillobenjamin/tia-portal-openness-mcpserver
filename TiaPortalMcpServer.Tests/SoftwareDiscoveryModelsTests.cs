using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TiaPortalMcpServer.Models;
using Xunit;

namespace TiaPortalMcpServer.Tests
{
    [Trait("Category", "Unit")]
    public class SoftwareDiscoveryModelsTests
    {
        [Theory]
        [InlineData("MSPLC_M2", null, "MSPLC_M2")]
        [InlineData("MSPLC_M2", "", "MSPLC_M2")]
        [InlineData("MSPLC_M2", "SWS_Main", "MSPLC_M2/SWS_Main")]
        public void FormatPath_JoinsDeviceAndUnit(string deviceName, string unitName, string expected)
        {
            Assert.Equal(expected, PlcSoftwarePathInfo.FormatPath(deviceName, unitName));
        }

        [Fact]
        public void ComputeTotalCount_AddsSubgroupTotals()
        {
            var axis = new SoftwareTreeNode { Name = "Axis", Path = "Motion/Axis" };
            axis.ComputeTotalCount(3);
            var motion = new SoftwareTreeNode { Name = "Motion", Path = "Motion", Groups = new List<SoftwareTreeNode> { axis } };
            motion.ComputeTotalCount(2);
            var root = new SoftwareTreeNode { Name = "Program blocks", Groups = new List<SoftwareTreeNode> { motion } };

            Assert.Equal(6, root.ComputeTotalCount(1));
            Assert.Equal(5, motion.TotalCount);
            Assert.Equal(3, axis.TotalCount);
        }

        [Fact]
        public void SoftwareTree_OmitsSectionsLeftOut()
        {
            var tree = new SoftwareTree
            {
                Path = "MSPLC_M2/SWS_Main",
                DeviceName = "MSPLC_M2",
                UnitName = "SWS_Main",
                ProgramBlocks = new SoftwareTreeNode { Name = "Program blocks" }
            };

            var json = JObject.Parse(JsonConvert.SerializeObject(tree));

            Assert.Equal("MSPLC_M2/SWS_Main", (string)json["path"]);
            Assert.NotNull(json["programBlocks"]);
            Assert.Null(json["programBlocks"]["items"]);
            Assert.Null(json["plcDataTypes"]);
            Assert.Null(json["units"]);
        }

        [Fact]
        public void PlcSoftwarePathInfo_UnitOmitsUnitsSupported()
        {
            var unit = new PlcSoftwarePathInfo
            {
                Path = "MSPLC_M2/SWS_Inbound",
                DeviceName = "MSPLC_M2",
                UnitName = "SWS_Inbound",
                Kind = PlcSoftwarePathInfo.KindSoftwareUnit
            };

            var json = JObject.Parse(JsonConvert.SerializeObject(unit));

            Assert.Equal("SWS_Inbound", (string)json["unitName"]);
            Assert.Equal("SoftwareUnit", (string)json["kind"]);
            Assert.Null(json["unitsSupported"]);
        }
    }
}
