using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TiaPortalMcpServer.Services;
using Xunit;

namespace TiaPortalMcpServer.Tests
{
    [Trait("Category", "Unit")]
    public class HardwareSnapshotWriterTests
    {
        private enum FakeRtClass
        {
            RT,
            IRT
        }

        private sealed class FakeReference
        {
            public string Path { get; set; } = string.Empty;
        }

        private sealed class FakeCertificate
        {
        }

        private sealed class FakeNonRecoverableException : Exception
        {
        }

        private static JToken ConvertReference(object value)
        {
            return value is FakeReference reference ? new JObject { ["@ref"] = reference.Path } : null;
        }

        private static Func<string, object> Reader(Dictionary<string, object> values)
        {
            return name => values[name];
        }

        [Fact]
        public void WriteAttributes_WritesEachKindOfValue()
        {
            var writer = new HardwareSnapshotWriter(ConvertReference);
            var values = new Dictionary<string, object>
            {
                ["PnSendClock"] = 1000000L,
                ["Failsafe_FMonitoringtime"] = 150,
                ["Failsafe_InputDelay"] = 4.2,
                ["IsPlugged"] = true,
                ["RtClass"] = FakeRtClass.RT,
                ["InstallationDate"] = new DateTime(2025, 11, 26, 6, 36, 16, DateTimeKind.Utc),
                ["PnDeviceName"] = "rio_et200",
                ["ConnectedSubnet"] = new FakeReference { Path = "PN/IE_3" },
                ["ConnectedIoDevices"] = new List<object> { new FakeReference { Path = "RIO_DWS1/X1/(IoConnector)" } },
                ["PlcCommunicationCertificate"] = new FakeCertificate(),
                ["CableName"] = null
            };
            var node = new JObject();

            writer.WriteAttributes(node, values.Keys, Reader(values), "MSPLC_M2");

            var attributes = (JObject)node["attributes"];
            Assert.Equal(1000000L, (long)attributes["PnSendClock"]);
            Assert.Equal(150, (int)attributes["Failsafe_FMonitoringtime"]);
            Assert.Equal(4.2, (double)attributes["Failsafe_InputDelay"]);
            Assert.True((bool)attributes["IsPlugged"]);
            Assert.Equal("RT", (string)attributes["RtClass"]);
            Assert.Equal("2025-11-26T06:36:16.0000000Z", (string)attributes["InstallationDate"]);
            Assert.Equal("rio_et200", (string)attributes["PnDeviceName"]);
            Assert.Equal("PN/IE_3", (string)attributes["ConnectedSubnet"]["@ref"]);
            Assert.Equal("RIO_DWS1/X1/(IoConnector)", (string)attributes["ConnectedIoDevices"][0]["@ref"]);
            Assert.Equal("FakeCertificate", (string)attributes["PlcCommunicationCertificate"]["@type"]);
            Assert.Equal(JTokenType.Null, attributes["CableName"].Type);
            Assert.Null(node["@unreadable"]);
        }

        [Fact]
        public void WriteAttributes_LeavesOutTreeAttributesAndDuplicates()
        {
            var writer = new HardwareSnapshotWriter();
            var values = new Dictionary<string, object>
            {
                ["Name"] = "Rack_0",
                ["Items"] = new List<object> { "x" },
                ["Container"] = "parent",
                ["UnpluggedItems"] = new List<object>(),
                ["Ports"] = new List<object>()
            };
            var node = new JObject();

            writer.WriteAttributes(node, new[] { "Name", "Items", "Container", "UnpluggedItems", "Ports", "Name" }, Reader(values), "Rack_0");

            Assert.Equal(new[] { "Name" }, ((JObject)node["attributes"]).Properties().Select(p => p.Name).ToArray());
        }

        [Fact]
        public void WriteAttributes_RecordsUnreadableAttributes()
        {
            var writer = new HardwareSnapshotWriter();
            writer.BeginDevice("ET 200SP station_1");
            var node = new JObject();

            writer.WriteAttributes(node, new[] { "TypeName", "Failsafe_FSIL", "Address" }, name =>
            {
                if (name == "TypeName")
                {
                    return "IM 155-6 PN ST";
                }

                throw new InvalidOperationException("not supported");
            }, "RIO_ET200");

            Assert.Equal("IM 155-6 PN ST", (string)node["attributes"]["TypeName"]);
            Assert.Equal(new[] { "Address", "Failsafe_FSIL" }, node["@unreadable"].ToObject<string[]>());
            Assert.Equal("ET 200SP station_1: RIO_ET200: Failsafe_FSIL: not supported", writer.Unreadable[0]);
        }

        [Fact]
        public void WriteAttributes_FailureThatEndsTheSnapshot_NamesTheAttribute()
        {
            var writer = new HardwareSnapshotWriter();
            writer.BeginDevice("RIO_DWS1");

            var ex = Assert.Throws<HmiSnapshotReadException>(() =>
                writer.WriteAttributes(new JObject(), new[] { "PnUpdateTime" }, name => throw new FakeNonRecoverableException(), "RIO_DWS1/X1#network/ioConnector[0]"));

            Assert.Equal("RIO_DWS1: RIO_DWS1/X1#network/ioConnector[0]: PnUpdateTime", ex.Location);
            Assert.IsType<FakeNonRecoverableException>(ex.InnerException);
        }

        [Fact]
        public void AddChild_KeysChildrenByNameWithSuffixForDuplicates()
        {
            var parent = new JObject();

            var first = HardwareSnapshotWriter.AddChild(parent, "items", "Port_1", new JObject());
            var second = HardwareSnapshotWriter.AddChild(parent, "items", "Port_1", new JObject());
            var unnamed = HardwareSnapshotWriter.AddChild(parent, "items", string.Empty, new JObject());

            Assert.Equal(new[] { "Port_1", "Port_1~2", "#" }, new[] { first, second, unnamed });
            Assert.Equal(3, ((JObject)parent["items"]).Count);
        }

        [Fact]
        public void Snapshot_IsDeterministicWhateverTheReadOrder()
        {
            string Build(IEnumerable<string> names, IEnumerable<string> items)
            {
                var writer = new HardwareSnapshotWriter();
                var root = writer.BeginDevice("Station");
                var values = new Dictionary<string, object> { ["B"] = 2, ["A"] = 1 };
                writer.WriteAttributes(root, names, Reader(values), "(device)");
                foreach (var item in items)
                {
                    var node = new JObject();
                    writer.WriteAttributes(node, names, Reader(values), item);
                    HardwareSnapshotWriter.AddChild(root, "items", item, node);
                }

                return HmiSnapshotSerializer.ToJson(root);
            }

            var json = Build(new[] { "A", "B" }, new[] { "CPU", "Rack_0" });

            Assert.Equal(json, Build(new[] { "B", "A" }, new[] { "Rack_0", "CPU" }));
            Assert.Contains("\"@format\": \"" + HardwareSnapshotWriter.Format + "\"", json);
            Assert.DoesNotContain("\r", json);
        }
    }
}
