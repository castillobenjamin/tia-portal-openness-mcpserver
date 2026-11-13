using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace TiaPortalMcpServer.Services
{
    /// <summary>
    /// Builds the JSON of a hardware snapshot from Openness attributes: one object per device, device item, network
    /// interface, node, IO connector, IO controller, IO system and channel, holding every readable attribute.
    /// </summary>
    /// <remarks>
    /// References no Siemens types, so it can be tested with plain values. The caller passes the attribute names, a
    /// reader and converters for the values only Openness knows (multilingual texts, references to engineering objects).
    /// <list type="bullet">
    /// <item>Attributes that describe the tree itself (Items, Container, UnpluggedItems, Ports) are left out; the
    /// snapshot holds the items below each object as "items".</item>
    /// <item>Enums are written by member name, dates in ISO 8601 form, collections as arrays, references to other
    /// engineering objects as { "@ref": "&lt;name&gt;" } and other objects only by their type: { "@type": "&lt;type&gt;" }.</item>
    /// <item>Attributes that throw when read are listed under "@unreadable".</item>
    /// </list>
    /// </remarks>
    public sealed class HardwareSnapshotWriter
    {
        /// <summary>
        /// Value of the "@format" key at the root of a device snapshot; changes when the layout changes.
        /// </summary>
        public const string Format = "tia-portal-mcp/hardware-snapshot/1";

        private static readonly HashSet<string> SkippedAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            "Items", "Container", "UnpluggedItems", "Ports"
        };

        private readonly Func<object, JToken?>? _convert;
        private readonly List<string> _unreadable = new List<string>();
        private string _rootName = string.Empty;

        /// <param name="convert">Converts values the writer does not know, such as multilingual texts and references to
        /// engineering objects; returns null to let the writer handle them.</param>
        public HardwareSnapshotWriter(Func<object, JToken?>? convert = null)
        {
            _convert = convert;
        }

        /// <summary>
        /// One line per attribute that could not be read: "&lt;device&gt;: &lt;path&gt;: &lt;attribute&gt;: message".
        /// </summary>
        public IReadOnlyList<string> Unreadable => _unreadable;

        /// <summary>
        /// Starts the snapshot of a device; <paramref name="deviceName"/> starts the lines of <see cref="Unreadable"/>.
        /// </summary>
        public JObject BeginDevice(string deviceName)
        {
            _rootName = deviceName;
            return new JObject { ["@format"] = Format };
        }

        /// <summary>
        /// Writes the readable attributes in <paramref name="names"/> into <paramref name="target"/>["attributes"],
        /// and the ones whose read threw into "@unreadable". A failure that means TIA Portal is gone is rethrown as
        /// <see cref="HmiSnapshotReadException"/> naming <paramref name="path"/> and the attribute.
        /// </summary>
        public void WriteAttributes(JObject target, IEnumerable<string> names, Func<string, object?> read, string path)
        {
            var attributes = new JObject();
            var unreadable = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                if (SkippedAttributes.Contains(name) || !seen.Add(name))
                {
                    continue;
                }

                try
                {
                    attributes[name] = ToToken(read(name));
                }
                catch (Exception ex) when (HmiSnapshotSerializer.IsReadFailure(ex))
                {
                    unreadable.Add(name);
                    _unreadable.Add($"{_rootName}: {path}: {name}: {ex.Message}");
                }
                catch (Exception ex) when (!(ex is HmiSnapshotReadException))
                {
                    throw new HmiSnapshotReadException($"{_rootName}: {path}: {name}", ex);
                }
            }

            target["attributes"] = attributes;
            if (unreadable.Count > 0)
            {
                unreadable.Sort(StringComparer.Ordinal);
                target["@unreadable"] = new JArray(unreadable.ToArray());
            }
        }

        /// <summary>
        /// Adds <paramref name="child"/> to the object <paramref name="target"/>[<paramref name="collection"/>] under
        /// <paramref name="key"/>, with a "~2" suffix when the key is taken. Returns the key used.
        /// </summary>
        public static string AddChild(JObject target, string collection, string key, JObject child)
        {
            if (!(target[collection] is JObject children))
            {
                children = new JObject();
                target[collection] = children;
            }

            var candidate = string.IsNullOrEmpty(key) ? "#" : key;
            var unique = candidate;
            for (var n = 2; children.ContainsKey(unique); n++)
            {
                unique = candidate + "~" + n.ToString(CultureInfo.InvariantCulture);
            }

            children[unique] = child;
            return unique;
        }

        /// <summary>
        /// The JSON for an attribute value.
        /// </summary>
        public JToken ToToken(object? value)
        {
            if (value == null)
            {
                return JValue.CreateNull();
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
                case DateTime time:
                    return new JValue(time.ToString("o", CultureInfo.InvariantCulture));
                case DateTimeOffset time:
                    return new JValue(time.ToString("o", CultureInfo.InvariantCulture));
                case TimeSpan span:
                    return new JValue(span.ToString("c", CultureInfo.InvariantCulture));
                case IEnumerable collection:
                    var array = new JArray();
                    foreach (var element in collection)
                    {
                        array.Add(ToToken(element));
                    }

                    return array;
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

            return new JObject { ["@type"] = type.Name };
        }
    }
}
