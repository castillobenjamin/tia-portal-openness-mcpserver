using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;

namespace TiaPortalMcpServer.Services
{
    /// <summary>
    /// Reads the Openness attributes of a device and everything below it into a hardware snapshot: each device item
    /// with its network interface (nodes, IO connectors, IO controllers and their IO systems), its channels and its
    /// child items. Only reads; nothing in the project is changed.
    /// </summary>
    public static class HardwareSnapshotReader
    {
        private const int MaxReferenceDepth = 12;

        /// <summary>
        /// The snapshot of <paramref name="device"/>. <paramref name="trace"/> is called with the path of the device and
        /// of each device item before it is read, so a log names the object being read when reading fails badly.
        /// </summary>
        public static JObject Read(Device device, HardwareSnapshotWriter writer, Action<string>? trace)
        {
            trace?.Invoke(device.Name);
            var root = writer.BeginDevice(device.Name);
            writer.WriteAttributes(root, ReadableNames(device), device.GetAttribute, "(device)");
            foreach (DeviceItem item in device.DeviceItems)
            {
                ReadItem(root, item, item.Name, writer, trace, device.Name);
            }

            return root;
        }

        /// <summary>
        /// Converts the Openness values <see cref="HardwareSnapshotWriter"/> does not know: multilingual texts become
        /// { "&lt;culture&gt;": "&lt;text&gt;" }, engineering objects a reference { "@ref": "&lt;path&gt;" } that is not read
        /// further. Returns null for every other value.
        /// </summary>
        public static JToken? ConvertValue(object value)
        {
            var text = HmiUnifiedService.ConvertSnapshotValue(value);
            if (text != null)
            {
                return text;
            }

            if (value is IEngineeringObject engineeringObject)
            {
                return new JObject { ["@ref"] = ReferencePath(engineeringObject) };
            }

            return null;
        }

        private static void ReadItem(JObject parent, DeviceItem item, string path, HardwareSnapshotWriter writer, Action<string>? trace, string deviceName)
        {
            trace?.Invoke(deviceName + "/" + path);
            var node = new JObject();
            writer.WriteAttributes(node, ReadableNames(item), item.GetAttribute, path);

            var network = item.GetService<NetworkInterface>();
            if (network != null)
            {
                node["network"] = ReadNetwork(network, path + "#network", writer);
            }

            var channels = new JObject();
            foreach (Channel channel in item.Channels)
            {
                var key = channel.IoType.ToString() + " " + channel.Type.ToString() + " " + channel.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var entry = new JObject();
                writer.WriteAttributes(entry, ReadableNames(channel), channel.GetAttribute, path + "#channel[" + key + "]");
                HardwareSnapshotWriter.AddChild(node, "channels", key, entry);
            }

            HardwareSnapshotWriter.AddChild(parent, "items", item.Name, node);
            foreach (DeviceItem child in item.DeviceItems)
            {
                ReadItem(node, child, path + "/" + child.Name, writer, trace, deviceName);
            }
        }

        private static JObject ReadNetwork(NetworkInterface network, string path, HardwareSnapshotWriter writer)
        {
            var result = new JObject();
            writer.WriteAttributes(result, ReadableNames(network), network.GetAttribute, path);

            foreach (Node node in network.Nodes)
            {
                var entry = new JObject();
                writer.WriteAttributes(entry, ReadableNames(node), node.GetAttribute, path + "/node[" + node.Name + "]");
                HardwareSnapshotWriter.AddChild(result, "nodes", node.Name, entry);
            }

            var index = 0;
            foreach (IoConnector connector in network.IoConnectors)
            {
                var entry = new JObject();
                writer.WriteAttributes(entry, ReadableNames(connector), connector.GetAttribute, path + "/ioConnector[" + index + "]");
                HardwareSnapshotWriter.AddChild(result, "ioConnectors", "#" + index, entry);
                index++;
            }

            index = 0;
            foreach (IoController controller in network.IoControllers)
            {
                var entry = new JObject();
                writer.WriteAttributes(entry, ReadableNames(controller), controller.GetAttribute, path + "/ioController[" + index + "]");
                var system = controller.IoSystem;
                if (system != null)
                {
                    var systemEntry = new JObject();
                    writer.WriteAttributes(systemEntry, ReadableNames(system), system.GetAttribute, path + "/ioController[" + index + "]/ioSystem");
                    entry["ioSystem"] = systemEntry;
                }

                HardwareSnapshotWriter.AddChild(result, "ioControllers", "#" + index, entry);
                index++;
            }

            return result;
        }

        /// <summary>
        /// The names of the attributes of <paramref name="value"/> that can be read.
        /// </summary>
        private static List<string> ReadableNames(IEngineeringObject value)
        {
            var names = new List<string>();
            foreach (EngineeringAttributeInfo info in value.GetAttributeInfos())
            {
                if ((info.AccessMode & EngineeringAttributeAccessMode.Read) != 0)
                {
                    names.Add(info.Name);
                }
            }

            names.Sort(StringComparer.Ordinal);
            return names;
        }

        /// <summary>
        /// "&lt;device&gt;/&lt;item&gt;/..." for a device item, "&lt;subnet&gt;/&lt;IO system&gt;" for an IO system: the names of the
        /// object and its parents up to the project, with "(&lt;type&gt;)" for objects without a name and a name repeated
        /// by the next parent written once.
        /// </summary>
        private static string ReferencePath(IEngineeringObject value)
        {
            var parts = new List<string>();
            IEngineeringObject? current = value;
            for (var depth = 0; current != null && !(current is Project) && depth < MaxReferenceDepth; depth++)
            {
                // A service such as the network interface carries the name of its device item; name it once.
                var part = ReadName(current) ?? "(" + current.GetType().Name + ")";
                if (parts.Count == 0 || parts[parts.Count - 1] != part)
                {
                    parts.Add(part);
                }

                if (current is Device)
                {
                    break;
                }

                current = (current as IEngineeringInstance)?.Parent as IEngineeringObject;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        private static string? ReadName(IEngineeringObject value)
        {
            try
            {
                var name = value.GetAttribute("Name") as string;
                return string.IsNullOrEmpty(name) ? null : name;
            }
            catch (Exception ex) when (HmiSnapshotSerializer.IsReadFailure(ex))
            {
                return null;
            }
        }
    }
}
