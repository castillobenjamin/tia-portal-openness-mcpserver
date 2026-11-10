using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TiaPortalMcpServer.Services
{
    /// <summary>
    /// A script file written next to a snapshot, with its path relative to the snapshot's directory ("Main.scripts/Button_1.Tapped.js").
    /// </summary>
    public sealed class SnapshotSidecar
    {
        public SnapshotSidecar(string relativePath, string content)
        {
            RelativePath = relativePath;
            Content = content;
        }

        public string RelativePath { get; }

        public string Content { get; }
    }

    /// <summary>
    /// Turns an object graph (a WinCC Unified screen with its items, parts, dynamizations and event handlers) into a
    /// deterministic JSON snapshot by reading the public properties of the objects the graph owns through reflection.
    /// Script code goes to sidecar files.
    /// </summary>
    /// <remarks>
    /// Works on any object graph and references no Siemens types, so it can be tested with plain objects. Values the graph
    /// holds in Openness-specific types (multilingual texts) are converted by the converter passed in.
    /// <list type="bullet">
    /// <item>Every object gets an "@type" key with its runtime type name; "Parent" properties are skipped.</item>
    /// <item>An object or collection that the owner check passed in says does not belong to the object holding it (a
    /// reference to another engineering object, such as a faceplate's library type) is not read: it is written as
    /// { "@type", "@ref": "&lt;its Name&gt;" }. ScreenItems and the keyed collections below are always read.</item>
    /// <item>Colors are written as #AARRGGBB, enums by member name, dates in ISO 8601 form.</item>
    /// <item>ScreenItems stay in their order (the z-order); Dynamizations, EventHandlers, PropertyEventHandlers,
    /// Interface and DriverProperties become objects keyed by property or event; other collections stay arrays; empty collections are left out.</item>
    /// <item>Non-empty ScriptCode and GlobalDefinitionAreaScriptCode values are moved to sidecar files and replaced by
    /// { "file": "&lt;relative path&gt;" }.</item>
    /// <item>Properties that throw when read are listed under "@unreadable".</item>
    /// </list>
    /// </remarks>
    public sealed class HmiSnapshotSerializer
    {
        /// <summary>
        /// Value of the "@format" key at the root of a screen snapshot; changes when the layout changes.
        /// </summary>
        public const string Format = "tia-portal-mcp/hmi-unified-screen-snapshot/1";

        /// <summary>
        /// Value of the "@format" key of a snapshot of named objects (alarms, alarm classes, logs, connections), written by
        /// <see cref="SerializeNamed"/>.
        /// </summary>
        public const string CollectionFormat = "tia-portal-mcp/hmi-unified-collection-snapshot/1";

        /// <summary>
        /// Value of the "@format" key of a snapshot of a single settings object such as the runtime settings.
        /// </summary>
        public const string ObjectFormat = "tia-portal-mcp/hmi-unified-object-snapshot/1";

        private const int MaxDepth = 32;
        private const string ScreenItemsProperty = "ScreenItems";
        private const string DynamizationsProperty = "Dynamizations";
        private const string ScriptProperty = "Script";
        private const string GlobalDefinitionProperty = "GlobalDefinitionAreaScriptCode";

        private static readonly HashSet<string> SkippedProperties = new HashSet<string>(StringComparer.Ordinal) { "Parent" };

        private static readonly HashSet<string> ScriptProperties = new HashSet<string>(StringComparer.Ordinal) { "ScriptCode", GlobalDefinitionProperty };

        /// <summary>
        /// Collections written as objects keyed by these properties of their elements, and whether the collection name is part
        /// of the sidecar file name of scripts below them.
        /// </summary>
        private static readonly Dictionary<string, (string[] KeyProperties, bool NameInPath)> KeyedCollections =
            new Dictionary<string, (string[] KeyProperties, bool NameInPath)>(StringComparer.Ordinal)
            {
                [DynamizationsProperty] = (new[] { "PropertyName" }, false),
                ["EventHandlers"] = (new[] { "EventType" }, false),
                ["PropertyEventHandlers"] = (new[] { "PropertyName", "EventType" }, false),
                ["Interface"] = (new[] { "PropertyName" }, true),
                ["DriverProperties"] = (new[] { "PropertyName" }, true)
            };

        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new ConcurrentDictionary<Type, PropertyInfo[]>();

        private readonly Func<object, JToken?>? _convert;
        private readonly Func<object, object, bool>? _isOwnedBy;
        private readonly Action<string>? _trace;
        private readonly string _sidecarFolder;
        private readonly List<SnapshotSidecar> _sidecars = new List<SnapshotSidecar>();
        private readonly HashSet<string> _sidecarNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _unreadable = new List<string>();
        private string _rootName = string.Empty;

        /// <param name="sidecarFolder">Folder for script files, relative to the snapshot's directory, such as "Main.scripts".</param>
        /// <param name="convert">Converts values the serializer does not know; returns null to let the serializer handle them.</param>
        /// <param name="isOwnedBy">Called as (value, owner) for each object or collection found in a property of owner;
        /// false writes the value as a reference instead of reading it. Null reads everything.</param>
        /// <param name="trace">Called with the path of each root object, collection entry and screen item before it is read,
        /// so a log names the object being read when reading fails badly.</param>
        public HmiSnapshotSerializer(string sidecarFolder, Func<object, JToken?>? convert = null, Func<object, object, bool>? isOwnedBy = null, Action<string>? trace = null)
        {
            _sidecarFolder = sidecarFolder;
            _convert = convert;
            _isOwnedBy = isOwnedBy;
            _trace = trace;
        }

        /// <summary>
        /// Script files collected by <see cref="Serialize"/>, in the order they were found.
        /// </summary>
        public IReadOnlyList<SnapshotSidecar> Sidecars => _sidecars;

        /// <summary>
        /// One line per property that could not be read, such as "Main: Button_1.Font.Size: message" (screen, then the
        /// item path; properties of the screen itself start with the screen name).
        /// </summary>
        public IReadOnlyList<string> Unreadable => _unreadable;

        /// <summary>
        /// The snapshot of <paramref name="root"/>; <paramref name="rootName"/> names scripts of the root itself.
        /// </summary>
        /// <param name="format">The "@format" value: <see cref="Format"/> for screens, <see cref="ObjectFormat"/> for other objects.</param>
        public JObject Serialize(object root, string rootName, string format = Format)
        {
            _rootName = rootName;
            _trace?.Invoke(rootName);
            var snapshot = SerializeObject(root, new[] { ExchangePaths.ToFileName(rootName) }, false, 0);
            snapshot["@format"] = format;
            return snapshot;
        }

        /// <summary>
        /// The snapshot of a collection of named objects such as alarms: { "@format", "items": { "&lt;Name&gt;": { ... } } },
        /// so the entries end up sorted by name. Entries without a readable name are keyed "#&lt;index&gt;"; duplicate names get
        /// a "~2" suffix. <paramref name="collectionName"/> starts the lines of <see cref="Unreadable"/>.
        /// </summary>
        public JObject SerializeNamed(IEnumerable entries, string collectionName)
        {
            _rootName = collectionName;
            var items = new JObject();
            var index = 0;
            foreach (var entry in entries)
            {
                if (entry != null)
                {
                    var key = UniqueKey(items, ReadName(entry) ?? "#" + index.ToString(CultureInfo.InvariantCulture));
                    _trace?.Invoke(collectionName + "/" + key);
                    items[key] = SerializeObject(entry, new[] { ExchangePaths.ToFileName(key) }, false, 0);
                }

                index++;
            }

            return new JObject { ["@format"] = CollectionFormat, ["items"] = items };
        }

        /// <summary>
        /// Indented JSON with object keys sorted by ordinal comparison, '\n' line endings and a final newline.
        /// </summary>
        public static string ToJson(JToken token)
        {
            using (var text = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" })
            {
                using (var writer = new JsonTextWriter(text) { Formatting = Formatting.Indented, Indentation = 2 })
                {
                    Sort(token).WriteTo(writer);
                }

                return text.ToString() + "\n";
            }
        }

        /// <summary>
        /// Writes <paramref name="content"/> as UTF-8 without a byte order mark, creating the directory. Leaves the file
        /// untouched and returns false when it already holds exactly these bytes.
        /// </summary>
        public static bool WriteIfChanged(string path, string content)
        {
            var bytes = new UTF8Encoding(false).GetBytes(content);
            if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bytes))
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllBytes(path, bytes);
            return true;
        }

        /// <summary>
        /// #AARRGGBB, the color form of snapshots.
        /// </summary>
        public static string FormatColor(Color color)
        {
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}{3:X2}", color.A, color.R, color.G, color.B);
        }

        private JObject SerializeObject(object value, IReadOnlyList<string> path, bool inDynamization, int depth)
        {
            var result = new JObject { ["@type"] = value.GetType().Name };
            var unreadable = new List<string>();

            foreach (var property in GetProperties(value.GetType()))
            {
                if (SkippedProperties.Contains(property.Name))
                {
                    continue;
                }

                try
                {
                    var propertyValue = property.GetValue(value);
                    if (ScriptProperties.Contains(property.Name) && propertyValue is string code)
                    {
                        result[property.Name] = ToScriptToken(code, path, property.Name, inDynamization);
                        continue;
                    }

                    var token = SerializeValue(propertyValue, property.Name, path, inDynamization, depth + 1, value);
                    if (token != null)
                    {
                        result[property.Name] = token;
                    }
                }
                catch (Exception ex) when (IsReadFailure(ex))
                {
                    var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                    unreadable.Add(property.Name);
                    _unreadable.Add($"{_rootName}: {string.Join(".", path.Concat(new[] { property.Name }))}: {inner.Message}");
                }
            }

            if (unreadable.Count > 0)
            {
                result["@unreadable"] = new JArray(unreadable.Cast<object>().ToArray());
            }

            return result;
        }

        /// <summary>
        /// The JSON for a property value, or null to leave the property out (empty collections).
        /// </summary>
        private JToken? SerializeValue(object? value, string name, IReadOnlyList<string> path, bool inDynamization, int depth, object owner)
        {
            if (value == null)
            {
                return JValue.CreateNull();
            }

            if (depth > MaxDepth)
            {
                return new JValue("(not expanded: nesting too deep)");
            }

            var converted = _convert?.Invoke(value);
            if (converted != null)
            {
                return converted;
            }

            switch (value)
            {
                case string text:
                    return new JValue(text);
                case Enum member:
                    return new JValue(member.ToString());
                case Color color:
                    return new JValue(FormatColor(color));
                case DateTime time:
                    return new JValue(time.ToString("o", CultureInfo.InvariantCulture));
                case DateTimeOffset time:
                    return new JValue(time.ToString("o", CultureInfo.InvariantCulture));
                case TimeSpan span:
                    return new JValue(span.ToString("c", CultureInfo.InvariantCulture));
                case IEnumerable collection:
                    if (!IsChildCollection(name) && !IsOwned(collection, owner))
                    {
                        return Reference(collection);
                    }

                    return SerializeCollection(collection, name, path, inDynamization, depth, owner);
            }

            var type = value.GetType();
            if (type.IsPrimitive || value is decimal)
            {
                return new JValue(value);
            }

            if (type.IsValueType)
            {
                return new JValue(Convert.ToString(value, CultureInfo.InvariantCulture));
            }

            if (!IsOwned(value, owner))
            {
                return Reference(value);
            }

            return SerializeObject(value, name == ScriptProperty ? path : Append(path, name), inDynamization, depth);
        }

        private static bool IsChildCollection(string name)
        {
            return name == ScreenItemsProperty || KeyedCollections.ContainsKey(name);
        }

        /// <summary>
        /// Whether <paramref name="value"/> belongs to <paramref name="owner"/>; an owner check that throws counts as "no".
        /// </summary>
        private bool IsOwned(object value, object owner)
        {
            if (_isOwnedBy == null)
            {
                return true;
            }

            try
            {
                return _isOwnedBy(value, owner);
            }
            catch (Exception ex) when (IsReadFailure(ex))
            {
                return false;
            }
        }

        /// <summary>
        /// A reference to an object the snapshot does not read: its type and, when it has one, its name.
        /// </summary>
        private static JObject Reference(object value)
        {
            var reference = new JObject { ["@type"] = value.GetType().Name };
            reference["@ref"] = value is IEnumerable ? "(collection)" : ReadName(value) ?? "(unnamed)";
            return reference;
        }

        private JToken? SerializeCollection(IEnumerable collection, string name, IReadOnlyList<string> path, bool inDynamization, int depth, object owner)
        {
            var elements = collection.Cast<object?>().ToList();
            if (elements.Count == 0)
            {
                return null;
            }

            if (name == ScreenItemsProperty)
            {
                var items = new JArray();
                for (var i = 0; i < elements.Count; i++)
                {
                    var element = elements[i];
                    if (element == null)
                    {
                        items.Add(JValue.CreateNull());
                        continue;
                    }

                    var itemName = ReadName(element) ?? "#" + i;
                    _trace?.Invoke(_rootName + "/" + itemName);
                    items.Add(SerializeObject(element, new[] { ExchangePaths.ToFileName(itemName) }, false, depth));
                }

                return items;
            }

            if (KeyedCollections.TryGetValue(name, out var keyed))
            {
                var basePath = keyed.NameInPath ? Append(path, name) : path;
                var childInDynamization = inDynamization || name == DynamizationsProperty;
                var entries = new JObject();
                for (var i = 0; i < elements.Count; i++)
                {
                    var element = elements[i];
                    var key = UniqueKey(entries, (element == null ? null : ReadKey(element, keyed.KeyProperties)) ?? "#" + i);
                    entries[key] = element == null
                        ? JValue.CreateNull()
                        : SerializeObject(element, Append(basePath, key), childInDynamization, depth);
                }

                return entries;
            }

            var array = new JArray();
            for (var i = 0; i < elements.Count; i++)
            {
                var element = elements[i];
                if (element != null && IsComplex(element))
                {
                    if (!IsOwned(element, owner))
                    {
                        array.Add(Reference(element));
                        continue;
                    }

                    var segment = ReadName(element) ?? "#" + i;
                    array.Add(SerializeObject(element, Append(Append(path, name), segment), inDynamization, depth));
                }
                else
                {
                    array.Add(SerializeValue(element, name, path, inDynamization, depth, owner) ?? JValue.CreateNull());
                }
            }

            return array;
        }

        private JToken ToScriptToken(string code, IReadOnlyList<string> path, string propertyName, bool inDynamization)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return new JValue(code);
            }

            var baseName = string.Join(".", path.Select(ExchangePaths.ToFileName)) + (propertyName == GlobalDefinitionProperty ? ".global" : string.Empty);
            var extension = inDynamization ? ".dyn.js" : ".js";
            var fileName = baseName + extension;
            for (var n = 2; !_sidecarNames.Add(fileName); n++)
            {
                fileName = baseName + "~" + n.ToString(CultureInfo.InvariantCulture) + extension;
            }

            var content = code.Replace("\r\n", "\n").Replace('\r', '\n');
            if (!content.EndsWith("\n", StringComparison.Ordinal))
            {
                content += "\n";
            }

            var relativePath = _sidecarFolder + "/" + fileName;
            _sidecars.Add(new SnapshotSidecar(relativePath, content));
            return new JObject { ["file"] = relativePath };
        }

        private static JToken Sort(JToken token)
        {
            switch (token)
            {
                case JObject obj:
                    return new JObject(obj.Properties()
                        .OrderBy(p => p.Name, StringComparer.Ordinal)
                        .Select(p => new JProperty(p.Name, Sort(p.Value))));
                case JArray array:
                    return new JArray(array.Select(Sort));
                default:
                    return token.DeepClone();
            }
        }

        /// <summary>
        /// Public readable properties without index parameters, one per name (the most derived), sorted by name.
        /// </summary>
        private static PropertyInfo[] GetProperties(Type type)
        {
            return PropertyCache.GetOrAdd(type, t =>
            {
                var byName = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
                foreach (var property in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!property.CanRead || property.GetGetMethod() == null || property.GetIndexParameters().Length > 0)
                    {
                        continue;
                    }

                    if (!byName.TryGetValue(property.Name, out var existing) ||
                        (property.DeclaringType != null && existing.DeclaringType != null && property.DeclaringType.IsSubclassOf(existing.DeclaringType)))
                    {
                        byName[property.Name] = property;
                    }
                }

                return byName.Values.OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
            });
        }

        private static bool IsComplex(object value)
        {
            var type = value.GetType();
            return !(value is string) && !type.IsValueType && !(value is IEnumerable);
        }

        private static string? ReadName(object element)
        {
            return ReadKey(element, new[] { "Name" });
        }

        /// <summary>
        /// The values of <paramref name="keyProperties"/> joined with '.', or null when one is missing, empty or unreadable.
        /// </summary>
        private static string? ReadKey(object element, string[] keyProperties)
        {
            var parts = new List<string>();
            foreach (var name in keyProperties)
            {
                var property = GetProperties(element.GetType()).FirstOrDefault(p => p.Name == name);
                if (property == null)
                {
                    return null;
                }

                string? text;
                try
                {
                    text = Convert.ToString(property.GetValue(element), CultureInfo.InvariantCulture);
                }
                catch (TargetInvocationException)
                {
                    return null;
                }

                if (string.IsNullOrEmpty(text))
                {
                    return null;
                }

                parts.Add(text!);
            }

            return string.Join(".", parts);
        }

        private static string UniqueKey(JObject entries, string key)
        {
            var candidate = key;
            for (var n = 2; entries.ContainsKey(candidate); n++)
            {
                candidate = key + "~" + n.ToString(CultureInfo.InvariantCulture);
            }

            return candidate;
        }

        private static IReadOnlyList<string> Append(IReadOnlyList<string> path, string segment)
        {
            return path.Concat(new[] { segment }).ToArray();
        }

        /// <summary>
        /// True for failures reading one property, which are recorded and skipped. Failures that mean the connection to TIA
        /// Portal is gone (Openness NonRecoverableException, disposed objects) end the snapshot instead.
        /// </summary>
        private static bool IsReadFailure(Exception ex)
        {
            var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
            if (inner is OutOfMemoryException || inner is ObjectDisposedException)
            {
                return false;
            }

            var typeName = inner.GetType().Name;
            return typeName.IndexOf("NonRecoverable", StringComparison.Ordinal) < 0 &&
                typeName.IndexOf("Disposed", StringComparison.Ordinal) < 0;
        }
    }
}
