using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using TiaPortalMcpServer.Models;
using TiaPortalMcpServer.Services;
using Xunit;

namespace TiaPortalMcpServer.Tests
{
    [Trait("Category", "Unit")]
    public class CaxExportTests
    {
        private static readonly XNamespace Caex = "http://www.dke.de/CAEX";

        private static string Aml(string plc, string plcPort, string io, string ioPort, string timestamp, string extra = "")
        {
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                   "<CAEXFile FileName=\"Hardware.aml\" SchemaVersion=\"3.0\" xmlns=\"http://www.dke.de/CAEX\">\r\n" +
                   "  <SourceDocumentInformation OriginName=\"Totally Integrated Automation Portal\" OriginVersion=\"V21\" LastWritingDateTime=\"" + timestamp + "\" />\r\n" +
                   "  <AdditionalInformation>\r\n" +
                   "    <WriterHeader>\r\n" +
                   "      <LastWritingDateTime>" + timestamp + "</LastWritingDateTime>\r\n" +
                   "    </WriterHeader>\r\n" +
                   "  </AdditionalInformation>\r\n" +
                   "  <InstanceHierarchy Name=\"M2\">\r\n" +
                   "    <InternalElement ID=\"" + plc + "\" Name=\"PLC_1\">\r\n" +
                   "      <ExternalInterface ID=\"" + plcPort + "\" Name=\"X1\" />\r\n" +
                   "    </InternalElement>\r\n" +
                   "    <InternalElement ID=\"" + io + "\" Name=\"IO_1\">\r\n" +
                   "      <ExternalInterface ID=\"" + ioPort + "\" Name=\"X1\" />\r\n" +
                   "    </InternalElement>\r\n" +
                   "    <InternalLink Name=\"Link\" RefPartnerSideA=\"" + plc + ":X1\" RefPartnerSideB=\"" + ioPort + "\" />\r\n" +
                   extra +
                   "  </InstanceHierarchy>\r\n" +
                   "</CAEXFile>\r\n";
        }

        private static string RandomAml(string timestamp)
        {
            return Aml("{" + Guid.NewGuid() + "}", "{" + Guid.NewGuid() + "}", Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), timestamp);
        }

        private static XDocument Parse(string xml)
        {
            return XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        }

        private static XElement Element(XDocument document, string name)
        {
            return document.Descendants(Caex + "InternalElement").Single(e => (string)e.Attribute("Name") == name);
        }

        [Fact]
        public void Normalize_RepeatedExportsWithNewIdsAndTimestamps_GiveIdenticalDocuments()
        {
            var first = Parse(RandomAml("2026-11-09T16:13:00"));
            var second = Parse(RandomAml("2026-11-09T16:20:41.123"));

            AmlNormalizer.Normalize(first);
            AmlNormalizer.Normalize(second);

            Assert.Equal(first.ToString(), second.ToString());
        }

        [Fact]
        public void Normalize_RewritesReferencesToTheNewIds()
        {
            var document = Parse(RandomAml("2026-11-09T16:13:00"));

            var result = AmlNormalizer.Normalize(document);

            var plcId = (string)Element(document, "PLC_1").Attribute("ID");
            var ioPortId = (string)Element(document, "IO_1").Element(Caex + "ExternalInterface").Attribute("ID");
            var link = document.Descendants(Caex + "InternalLink").Single();
            Assert.Equal(plcId + ":X1", (string)link.Attribute("RefPartnerSideA"));
            Assert.Equal(ioPortId, (string)link.Attribute("RefPartnerSideB"));
            Assert.Equal(4, result.IdsRewritten);
            Assert.Equal(2, result.ReferencesRewritten);
            Assert.Equal(2, result.TimestampsRewritten);
        }

        [Fact]
        public void Normalize_SetsTimestampsInAttributesAndElements()
        {
            var document = Parse(RandomAml("2026-11-09T16:13:00"));

            AmlNormalizer.Normalize(document);

            Assert.Equal(AmlNormalizer.FixedTimestamp, (string)document.Descendants(Caex + "SourceDocumentInformation").Single().Attribute("LastWritingDateTime"));
            Assert.Equal(AmlNormalizer.FixedTimestamp, document.Descendants(Caex + "LastWritingDateTime").Single().Value);
        }

        [Fact]
        public void Normalize_EquallyNamedElements_GetDistinctIds()
        {
            var document = Parse(RandomAml("2026-11-09T16:13:00"));

            AmlNormalizer.Normalize(document);

            var ids = document.Descendants().Select(e => (string)e.Attribute("ID")).Where(id => id != null).ToList();
            Assert.Equal(4, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        [Fact]
        public void Normalize_KeepsGuidsThatAreNotIds()
        {
            const string foreign = "0b6f2c7e-8a4d-4c1e-9f3a-2d5e6f7a8b9c";
            var extra = "    <Attribute Name=\"Guid\"><Value>" + foreign + "</Value></Attribute>\r\n";
            var document = Parse(Aml("{" + Guid.NewGuid() + "}", "{" + Guid.NewGuid() + "}", Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "2026-11-09T16:13:00", extra));

            AmlNormalizer.Normalize(document);

            Assert.Equal(foreign, document.Descendants(Caex + "Value").Single().Value);
        }

        [Fact]
        public void Normalize_PlainIds_AreRewrittenOnlyInReferenceAttributes()
        {
            var extra = "    <Attribute Name=\"Count\"><Value>1</Value></Attribute>\r\n";
            var document = Parse(Aml("1", "2", "3", "4", "2026-11-09T16:13:00", extra));

            AmlNormalizer.Normalize(document);

            var plcId = (string)Element(document, "PLC_1").Attribute("ID");
            Assert.NotEqual("1", plcId);
            Assert.Equal(plcId + ":X1", (string)document.Descendants(Caex + "InternalLink").Single().Attribute("RefPartnerSideA"));
            Assert.Equal("1", document.Descendants(Caex + "Value").Single().Value);
        }

        [Fact]
        public void Normalize_IsIdempotent()
        {
            var document = Parse(RandomAml("2026-11-09T16:13:00"));
            AmlNormalizer.Normalize(document);
            var once = document.ToString();

            var result = AmlNormalizer.Normalize(document);

            Assert.Equal(once, document.ToString());
            Assert.Equal(0, result.IdsRewritten);
            Assert.Equal(0, result.ReferencesRewritten);
            Assert.Equal(0, result.TimestampsRewritten);
        }

        [Fact]
        public void StableId_KeepsBracesAndLetterCase()
        {
            var braced = AmlNormalizer.StableId("/a", "{0B6F2C7E-8A4D-4C1E-9F3A-2D5E6F7A8B9C}");
            var plain = AmlNormalizer.StableId("/a", "0b6f2c7e-8a4d-4c1e-9f3a-2d5e6f7a8b9c");

            Assert.StartsWith("{", braced);
            Assert.EndsWith("}", braced);
            Assert.Equal(braced.ToUpperInvariant(), braced);
            Assert.Equal(plain.ToLowerInvariant(), plain);
            Assert.Equal(plain, braced.Trim('{', '}').ToLowerInvariant());
            Assert.True(Guid.TryParse(plain, out _));
            Assert.NotEqual(plain, AmlNormalizer.StableId("/b", "0b6f2c7e-8a4d-4c1e-9f3a-2d5e6f7a8b9c"));
        }

        [Fact]
        public void NormalizeFile_TwoExports_GiveIdenticalBytesAndKeepBomAndLineEndings()
        {
            var directory = Path.Combine(Path.GetTempPath(), "AmlNormalizer_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var first = Path.Combine(directory, "first.aml");
                var second = Path.Combine(directory, "second.aml");
                File.WriteAllText(first, RandomAml("2026-11-09T16:13:00"), new UTF8Encoding(true));
                File.WriteAllText(second, RandomAml("2026-11-09T16:20:00"), new UTF8Encoding(true));

                Assert.True(AmlNormalizer.NormalizeFile(first).FileChanged);
                Assert.True(AmlNormalizer.NormalizeFile(second).FileChanged);

                var bytes = File.ReadAllBytes(first);
                Assert.Equal(bytes, File.ReadAllBytes(second));
                Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
                var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
                Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<CAEXFile ", text);
                Assert.EndsWith("</CAEXFile>\r\n", text);
                Assert.DoesNotContain("\n", text.Replace("\r\n", string.Empty));

                Assert.False(AmlNormalizer.NormalizeFile(first).FileChanged);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static List<CaxMessage> SampleMessages()
        {
            return new List<CaxMessage>
            {
                new CaxMessage
                {
                    State = "Warning",
                    Message = "Device PLC_1",
                    DateTime = new DateTime(2026, 11, 9, 16, 13, 0),
                    Children =
                    {
                        new CaxMessage { State = "Error", Message = "Module DI 16 was not exported", DateTime = new DateTime(2026, 11, 9, 16, 13, 1) },
                        new CaxMessage { State = "Information", Message = "Rack exported", DateTime = new DateTime(2026, 11, 9, 16, 13, 1) }
                    }
                },
                new CaxMessage { State = "Warning", Message = "Subnet PN/IE_1 has no address", DateTime = new DateTime(2026, 11, 9, 16, 13, 2) }
            };
        }

        [Fact]
        public void CaxLogFormat_IndentsNestedMessagesAndOptionallyOmitsTimestamps()
        {
            var withTimes = CaxLog.Format(SampleMessages(), includeTimestamps: true);
            var withoutTimes = CaxLog.Format(SampleMessages(), includeTimestamps: false);

            Assert.Equal(
                "[2026-11-09 16:13:00] Warning: Device PLC_1\r\n" +
                "  [2026-11-09 16:13:01] Error: Module DI 16 was not exported\r\n" +
                "  [2026-11-09 16:13:01] Information: Rack exported\r\n" +
                "[2026-11-09 16:13:02] Warning: Subnet PN/IE_1 has no address\r\n",
                withTimes);
            Assert.Equal(
                "Warning: Device PLC_1\r\n" +
                "  Error: Module DI 16 was not exported\r\n" +
                "  Information: Rack exported\r\n" +
                "Warning: Subnet PN/IE_1 has no address\r\n",
                withoutTimes);
        }

        [Fact]
        public void CaxLogCollectProblems_PrefixesParentsAndSkipsInformation()
        {
            var result = new CaxExportResult();

            CaxLog.CollectProblems(SampleMessages(), result);

            Assert.Equal(new[] { "Device PLC_1 > Module DI 16 was not exported" }, result.Errors);
            Assert.Equal(new[] { "Device PLC_1", "Subnet PN/IE_1 has no address" }, result.Warnings);
            Assert.False(result.MessagesTruncated);
        }

        [Fact]
        public void CaxLogCollectProblems_TruncatesLongLists()
        {
            var messages = Enumerable.Range(0, CaxLog.MaxReportedMessages + 5)
                .Select(i => new CaxMessage { State = "Error", Message = "E" + i })
                .ToList();
            var result = new CaxExportResult();

            CaxLog.CollectProblems(messages, result);

            Assert.Equal(CaxLog.MaxReportedMessages, result.Errors.Count);
            Assert.True(result.MessagesTruncated);
        }
    }
}
