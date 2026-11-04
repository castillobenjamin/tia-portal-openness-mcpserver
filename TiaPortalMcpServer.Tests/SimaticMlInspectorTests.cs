using System;
using System.IO;
using System.Text;
using TiaPortalMcpServer.Services;
using Xunit;

namespace TiaPortalMcpServer.Tests
{
    [Trait("Category", "Unit")]
    public class SimaticMlInspectorTests
    {
        private static bool Inspect(string xml, out SimaticMlFileInfo info, out string error)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml)))
            {
                return SimaticMlInspector.TryInspect(stream, out info, out error);
            }
        }

        [Fact]
        public void TryInspect_BlockExport_ReturnsBlock()
        {
            Assert.True(Inspect(SimaticMlTestFiles.Block("Motor_FB"), out var info, out var error));
            Assert.Equal(SimaticMlInspector.KindBlock, info.Kind);
            Assert.Equal("SW.Blocks.FB", info.ElementName);
            Assert.Equal("Motor_FB", info.Name);
            Assert.Equal(string.Empty, error);
        }

        [Fact]
        public void TryInspect_TypeExport_ReturnsType()
        {
            Assert.True(Inspect(SimaticMlTestFiles.Type("Axis_UDT"), out var info, out _));
            Assert.Equal(SimaticMlInspector.KindType, info.Kind);
            Assert.Equal("SW.Types.PlcStruct", info.ElementName);
            Assert.Equal("Axis_UDT", info.Name);
        }

        [Fact]
        public void TryInspect_TagTableExport_ReturnsOther()
        {
            Assert.True(Inspect(SimaticMlTestFiles.Document("SW.Tags.PlcTagTable", "Default tag table"), out var info, out _));
            Assert.Equal(SimaticMlInspector.KindOther, info.Kind);
            Assert.Equal("Default tag table", info.Name);
        }

        [Fact]
        public void TryInspect_NoDocumentRoot_Fails()
        {
            Assert.False(Inspect("<Root><Child /></Root>", out _, out var error));
            Assert.Contains("no <Document> root element", error);
        }

        [Fact]
        public void TryInspect_EmptyDocument_Fails()
        {
            Assert.False(Inspect("<Document><Engineering version=\"V21\" /></Document>", out _, out var error));
            Assert.Contains("contains no exported object", error);
        }

        [Fact]
        public void TryInspect_MalformedXml_Fails()
        {
            Assert.False(Inspect("<Document><SW.Blocks.FB>", out _, out var error));
            Assert.StartsWith("Invalid XML", error);
        }

        [Fact]
        public void TryInspect_Dtd_IsRejected()
        {
            var xml = "<?xml version=\"1.0\"?><!DOCTYPE Document [<!ENTITY x \"Motor\">]>" +
                      "<Document><SW.Blocks.FB><AttributeList><Name>&x;</Name></AttributeList></SW.Blocks.FB></Document>";
            Assert.False(Inspect(xml, out _, out var error));
            Assert.StartsWith("Invalid XML", error);
        }

        [Fact]
        public void TryInspect_MissingFile_Fails()
        {
            var path = Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N") + ".xml");
            Assert.False(SimaticMlInspector.TryInspect(path, out _, out var error));
            Assert.Contains("not found", error);
        }
    }
}
