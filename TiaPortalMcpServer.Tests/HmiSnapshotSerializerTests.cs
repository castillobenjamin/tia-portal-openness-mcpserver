using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using TiaPortalMcpServer.Services;
using Xunit;

namespace TiaPortalMcpServer.Tests
{
    [Trait("Category", "Unit")]
    public class HmiSnapshotSerializerTests
    {
        private enum FakeEventType
        {
            Tapped,
            Pressed
        }

        private sealed class FakeText
        {
            public string English { get; set; } = string.Empty;

            public string German { get; set; } = string.Empty;
        }

        private sealed class FakeScript
        {
            public bool Async { get; set; }

            public string GlobalDefinitionAreaScriptCode { get; set; } = string.Empty;

            public string ScriptCode { get; set; } = string.Empty;
        }

        private sealed class FakeEventHandler
        {
            public FakeEventType EventType { get; set; }

            public FakeScript Script { get; set; } = new FakeScript();
        }

        private sealed class FakeDynamization
        {
            public string PropertyName { get; set; } = string.Empty;

            public string DynamizationType { get; set; } = "Script";

            public string ScriptCode { get; set; } = string.Empty;
        }

        private sealed class FakeFont
        {
            public string Name { get; set; } = "SiemensSans";

            public float Size { get; set; } = 12.5f;

            public List<FakeDynamization> Dynamizations { get; set; } = new List<FakeDynamization>();
        }

        private sealed class FakeButton
        {
            public object Parent => this;

            public string Name { get; set; } = string.Empty;

            public int Left { get; set; }

            public Color BackColor { get; set; } = Color.FromArgb(255, 0, 128, 255);

            public FakeFont Font { get; set; } = new FakeFont();

            public FakeText Text { get; set; } = new FakeText { English = "Start", German = "Starten" };

            public List<FakeDynamization> Dynamizations { get; set; } = new List<FakeDynamization>();

            public List<FakeEventHandler> EventHandlers { get; set; } = new List<FakeEventHandler>();

            public string Broken => throw new InvalidOperationException("not readable here");
        }

        private sealed class FakeScreen
        {
            public object Parent => null;

            public string Name { get; set; } = "Main";

            public Color BackColor { get; set; } = Color.FromArgb(128, 1, 2, 3);

            public List<object> ScreenItems { get; set; } = new List<object>();

            public List<FakeEventHandler> EventHandlers { get; set; } = new List<FakeEventHandler>();
        }

        private static JToken ConvertText(object value)
        {
            return value is FakeText text ? new JObject { ["en-US"] = text.English, ["de-DE"] = text.German } : null;
        }

        private static FakeScreen CreateScreen()
        {
            var start = new FakeButton { Name = "Start", Left = 20 };
            start.EventHandlers.Add(new FakeEventHandler
            {
                EventType = FakeEventType.Tapped,
                Script = new FakeScript { ScriptCode = "export function Start_OnTapped(item, x, y) {\r\n  HMIRuntime.Trace('start');\r\n}", GlobalDefinitionAreaScriptCode = "const limit = 3;" }
            });
            start.Dynamizations.Add(new FakeDynamization { PropertyName = "BackColor", ScriptCode = "return 0xFF00FF00;" });
            start.Font.Dynamizations.Add(new FakeDynamization { PropertyName = "Size", ScriptCode = "return 14;" });

            var stop = new FakeButton { Name = "Stop", Left = 200 };
            stop.EventHandlers.Add(new FakeEventHandler { EventType = FakeEventType.Pressed });

            var screen = new FakeScreen();
            screen.ScreenItems.Add(stop);
            screen.ScreenItems.Add(start);
            screen.EventHandlers.Add(new FakeEventHandler { EventType = FakeEventType.Tapped, Script = new FakeScript { ScriptCode = "// screen" } });
            return screen;
        }

        private static (string Json, HmiSnapshotSerializer Serializer) Snapshot(FakeScreen screen)
        {
            var serializer = new HmiSnapshotSerializer("Main.scripts", ConvertText);
            var json = HmiSnapshotSerializer.ToJson(serializer.Serialize(screen, screen.Name));
            return (json, serializer);
        }

        [Fact]
        public void Snapshot_IsDeterministic()
        {
            var first = Snapshot(CreateScreen());
            var second = Snapshot(CreateScreen());

            Assert.Equal(first.Json, second.Json);
            Assert.Equal(first.Serializer.Sidecars.Select(s => s.RelativePath + s.Content), second.Serializer.Sidecars.Select(s => s.RelativePath + s.Content));
        }

        [Fact]
        public void Snapshot_UsesLfLineEndingsAndFinalNewline()
        {
            var json = Snapshot(CreateScreen()).Json;

            Assert.DoesNotContain("\r", json);
            Assert.EndsWith("}\n", json);
        }

        [Fact]
        public void Snapshot_SortsKeysButKeepsItemOrder()
        {
            var root = JObject.Parse(Snapshot(CreateScreen()).Json);

            var keys = root.Properties().Select(p => p.Name).ToList();
            Assert.Equal(keys.OrderBy(k => k, StringComparer.Ordinal), keys);

            var items = (JArray)root["ScreenItems"];
            Assert.Equal(new[] { "Stop", "Start" }, items.Select(i => (string)i["Name"]));

            var itemKeys = ((JObject)items[1]).Properties().Select(p => p.Name).ToList();
            Assert.Equal(itemKeys.OrderBy(k => k, StringComparer.Ordinal), itemKeys);
        }

        [Fact]
        public void Snapshot_WritesTypesColorsEnumsTextsAndNumbers()
        {
            var root = JObject.Parse(Snapshot(CreateScreen()).Json);
            var start = (JObject)root["ScreenItems"][1];

            Assert.Equal(HmiSnapshotSerializer.Format, (string)root["@format"]);
            Assert.Equal("FakeScreen", (string)root["@type"]);
            Assert.Equal("#80010203", (string)root["BackColor"]);
            Assert.Equal("#FF0080FF", (string)start["BackColor"]);
            Assert.Equal("Starten", (string)start["Text"]["de-DE"]);
            Assert.Equal(12.5, (double)start["Font"]["Size"]);
            Assert.Equal("Tapped", (string)start["EventHandlers"]["Tapped"]["EventType"]);
            Assert.Null(start["Parent"]);
            Assert.Null(root["Parent"]);
        }

        [Fact]
        public void Snapshot_KeysDynamizationsAndEventHandlers()
        {
            var root = JObject.Parse(Snapshot(CreateScreen()).Json);
            var start = (JObject)root["ScreenItems"][1];

            Assert.NotNull(start["Dynamizations"]["BackColor"]);
            Assert.NotNull(start["Font"]["Dynamizations"]["Size"]);
            Assert.NotNull(start["EventHandlers"]["Tapped"]);
            Assert.NotNull(root["EventHandlers"]["Tapped"]);
        }

        [Fact]
        public void Snapshot_MovesScriptsToSidecars()
        {
            var (json, serializer) = Snapshot(CreateScreen());
            var root = JObject.Parse(json);
            var start = (JObject)root["ScreenItems"][1];

            Assert.Equal("Main.scripts/Start.Tapped.js", (string)start["EventHandlers"]["Tapped"]["Script"]["ScriptCode"]["file"]);
            Assert.Equal("Main.scripts/Start.Tapped.global.js", (string)start["EventHandlers"]["Tapped"]["Script"]["GlobalDefinitionAreaScriptCode"]["file"]);
            Assert.Equal("Main.scripts/Start.BackColor.dyn.js", (string)start["Dynamizations"]["BackColor"]["ScriptCode"]["file"]);
            Assert.Equal("Main.scripts/Start.Font.Size.dyn.js", (string)start["Font"]["Dynamizations"]["Size"]["ScriptCode"]["file"]);
            Assert.Equal("Main.scripts/Main.Tapped.js", (string)root["EventHandlers"]["Tapped"]["Script"]["ScriptCode"]["file"]);

            var tapped = serializer.Sidecars.Single(s => s.RelativePath == "Main.scripts/Start.Tapped.js");
            Assert.Equal("export function Start_OnTapped(item, x, y) {\n  HMIRuntime.Trace('start');\n}\n", tapped.Content);
            Assert.Equal(5, serializer.Sidecars.Count);
        }

        [Fact]
        public void Snapshot_KeepsEmptyScriptsInline()
        {
            var root = JObject.Parse(Snapshot(CreateScreen()).Json);
            var stop = (JObject)root["ScreenItems"][0];

            Assert.Equal(string.Empty, (string)stop["EventHandlers"]["Pressed"]["Script"]["ScriptCode"]);
        }

        [Fact]
        public void Snapshot_LeavesOutEmptyCollections()
        {
            var root = JObject.Parse(Snapshot(CreateScreen()).Json);
            var stop = (JObject)root["ScreenItems"][0];

            Assert.Null(stop["Dynamizations"]);
        }

        [Fact]
        public void Snapshot_GivesDuplicateSidecarNamesASuffix()
        {
            var screen = CreateScreen();
            var twin = new FakeButton { Name = "Start" };
            twin.EventHandlers.Add(new FakeEventHandler { EventType = FakeEventType.Tapped, Script = new FakeScript { ScriptCode = "// twin" } });
            screen.ScreenItems.Add(twin);

            var serializer = Snapshot(screen).Serializer;

            Assert.Contains(serializer.Sidecars, s => s.RelativePath == "Main.scripts/Start.Tapped~2.js" && s.Content == "// twin\n");
        }

        [Fact]
        public void Snapshot_RecordsUnreadableProperties()
        {
            var (json, serializer) = Snapshot(CreateScreen());
            var start = (JObject)JObject.Parse(json)["ScreenItems"][1];

            Assert.Equal(new[] { "Broken" }, start["@unreadable"].ToObject<string[]>());
            Assert.Contains("Main: Start.Broken: not readable here", serializer.Unreadable);
            Assert.Null(start["Broken"]);
        }

        private sealed class FakeAlarm
        {
            public string Name { get; set; } = string.Empty;

            public uint Id { get; set; }

            public FakeText EventText { get; set; } = new FakeText { English = "Motor fault", German = "Motorst\u00F6rung" };
        }

        private sealed class FakeSettings
        {
            public object Parent => null;

            public string StartScreen { get; set; } = "Main";
        }

        [Fact]
        public void SerializeNamed_KeysEntriesByNameInSortedOrder()
        {
            var alarms = new List<FakeAlarm> { new FakeAlarm { Name = "Pump", Id = 2 }, new FakeAlarm { Name = "Motor", Id = 1 }, new FakeAlarm { Name = "Pump", Id = 3 } };
            var serializer = new HmiSnapshotSerializer("DiscreteAlarms.scripts", ConvertText);

            var json = HmiSnapshotSerializer.ToJson(serializer.SerializeNamed(alarms, "DiscreteAlarms"));
            var root = JObject.Parse(json);
            var items = (JObject)root["items"];

            Assert.Equal(HmiSnapshotSerializer.CollectionFormat, (string)root["@format"]);
            Assert.Equal(new[] { "Motor", "Pump", "Pump~2" }, items.Properties().Select(p => p.Name));
            Assert.Equal(3u, (uint)items["Pump~2"]["Id"]);
            Assert.Equal("Motorst\u00F6rung", (string)items["Motor"]["EventText"]["de-DE"]);
            Assert.True(json.IndexOf("\"Motor\"", StringComparison.Ordinal) < json.IndexOf("\"Pump\"", StringComparison.Ordinal));
        }

        /// <summary>
        /// Stands in for an engineering object outside the screen, such as a faceplate's library type.
        /// </summary>
        private sealed class FakeLibraryType
        {
            public int ReadCount;

            public string Name => "Motor_FP";

            public string Version
            {
                get
                {
                    ReadCount++;
                    return "1.0.2";
                }
            }
        }

        private sealed class FakePart
        {
            public int Width { get; set; } = 2;
        }

        private sealed class FakeFaceplate
        {
            public string Name { get; set; } = "Motor_1";

            public FakePart Border { get; set; } = new FakePart();

            public FakeLibraryType ContainedType { get; set; } = new FakeLibraryType();

            public List<FakeLibraryType> Usages { get; set; } = new List<FakeLibraryType> { new FakeLibraryType() };

            public List<FakeDynamization> Dynamizations { get; set; } = new List<FakeDynamization> { new FakeDynamization { PropertyName = "Visible" } };
        }

        private static bool OwnsAllButLibraryTypes(object value, object owner)
        {
            return !(value is FakeLibraryType) && !(value is List<FakeLibraryType>);
        }

        [Fact]
        public void Snapshot_WritesReferencesWithoutReadingThem()
        {
            var faceplate = new FakeFaceplate();
            var screen = new FakeScreen();
            screen.ScreenItems.Add(faceplate);
            var serializer = new HmiSnapshotSerializer("Main.scripts", ConvertText, OwnsAllButLibraryTypes);

            var item = (JObject)serializer.Serialize(screen, "Main")["ScreenItems"][0];

            Assert.Equal(new JObject { ["@type"] = "FakeLibraryType", ["@ref"] = "Motor_FP" }, item["ContainedType"]);
            Assert.Equal("(collection)", (string)item["Usages"]["@ref"]);
            Assert.Equal(0, faceplate.ContainedType.ReadCount);
            Assert.Equal(0, faceplate.Usages[0].ReadCount);
            Assert.Equal(2, (int)item["Border"]["Width"]);
        }

        [Fact]
        public void Snapshot_AlwaysReadsScreenItemsAndKeyedCollections()
        {
            var screen = new FakeScreen();
            screen.ScreenItems.Add(new FakeFaceplate());
            var serializer = new HmiSnapshotSerializer("Main.scripts", ConvertText, (value, owner) => false);

            var item = (JObject)serializer.Serialize(screen, "Main")["ScreenItems"][0];

            Assert.Equal("Motor_1", (string)item["Name"]);
            Assert.Equal("Script", (string)item["Dynamizations"]["Visible"]["DynamizationType"]);
            Assert.NotNull(item["Border"]["@ref"]);
        }

        [Fact]
        public void Snapshot_OwnerCheckThatThrows_WritesAReference()
        {
            var screen = new FakeScreen();
            screen.ScreenItems.Add(new FakeFaceplate());
            var serializer = new HmiSnapshotSerializer("Main.scripts", ConvertText, (value, owner) => throw new InvalidOperationException("no parent"));

            var item = (JObject)serializer.Serialize(screen, "Main")["ScreenItems"][0];

            Assert.Equal("FakeLibraryType", (string)item["ContainedType"]["@type"]);
            Assert.Equal("Motor_FP", (string)item["ContainedType"]["@ref"]);
        }

        [Fact]
        public void Snapshot_TracesTheRootAndEachScreenItem()
        {
            var traced = new List<string>();
            var serializer = new HmiSnapshotSerializer("Main.scripts", ConvertText, null, traced.Add);

            serializer.Serialize(CreateScreen(), "Main");
            serializer.SerializeNamed(new List<FakeAlarm> { new FakeAlarm { Name = "Pump" } }, "DiscreteAlarms");

            Assert.Equal(new[] { "Main", "Main/Stop", "Main/Start", "DiscreteAlarms/Pump" }, traced);
        }

        [Fact]
        public void Serialize_WritesTheGivenFormat()
        {
            var root = new HmiSnapshotSerializer("RuntimeSettings.scripts").Serialize(new FakeSettings(), "RuntimeSettings", HmiSnapshotSerializer.ObjectFormat);

            Assert.Equal(HmiSnapshotSerializer.ObjectFormat, (string)root["@format"]);
            Assert.Equal("Main", (string)root["StartScreen"]);
            Assert.Null(root["Parent"]);
        }

        [Fact]
        public void WriteIfChanged_WritesUtf8WithoutBomAndSkipsUnchangedFiles()
        {
            using (var files = new SimaticMlTestFiles())
            {
                var path = Path.Combine(files.Root, "Screens", "Main.json");

                Assert.True(HmiSnapshotSerializer.WriteIfChanged(path, "{ \"Text\": \"Gr\u00FC\u00DFe\" }\n"));
                Assert.False(HmiSnapshotSerializer.WriteIfChanged(path, "{ \"Text\": \"Gr\u00FC\u00DFe\" }\n"));

                var bytes = File.ReadAllBytes(path);
                Assert.NotEqual(0xEF, bytes[0]);
                Assert.Equal("{ \"Text\": \"Gr\u00FC\u00DFe\" }\n", System.Text.Encoding.UTF8.GetString(bytes));
            }
        }
    }
}
