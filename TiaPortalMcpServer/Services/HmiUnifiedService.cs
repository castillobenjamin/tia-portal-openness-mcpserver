using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiTags;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.ScreenGroup;
using Siemens.Engineering.HmiUnified.UI.Screens;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using TiaPortalMcpServer.Models;

namespace TiaPortalMcpServer.Services
{
    /// <summary>
    /// Access to WinCC Unified HMI software: finding it on a device, walking screen and tag table groups,
    /// and reading or writing screen items by attribute name.
    /// </summary>
    public static class HmiUnifiedService
    {
        private const string TypePrefix = "Hmi";

        private static readonly object ScreenItemTypesLock = new object();
        private static Dictionary<string, Type>? _screenItemTypes;

        // No generic methods constrained to Siemens types in this class: the constraint makes the runtime load
        // Siemens.Engineering when it loads the class, which breaks reflection over this assembly in unit tests.

        /// <summary>
        /// The device item that hosts the first software passing <paramref name="predicate"/>, with that software.
        /// </summary>
        public static (DeviceItem Item, Software Software)? FindSoftwareItem(Device device, Func<Software, bool> predicate)
        {
            foreach (DeviceItem item in device.DeviceItems)
            {
                var found = FindSoftwareItem(item, predicate);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static (DeviceItem Item, Software Software)? FindSoftwareItem(DeviceItem item, Func<Software, bool> predicate)
        {
            var software = item.GetService<SoftwareContainer>()?.Software;
            if (software != null && predicate(software))
            {
                return (item, software);
            }

            foreach (DeviceItem child in item.DeviceItems)
            {
                var found = FindSoftwareItem(child, predicate);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        public static HmiSoftware? GetHmiSoftware(Device device)
        {
            return FindSoftwareItem(device, s => s is HmiSoftware)?.Software as HmiSoftware;
        }

        /// <summary>
        /// Every screen with its screen group path ("Overview/Lines"; empty at the top level).
        /// </summary>
        public static List<(HmiScreen Screen, string GroupPath)> GetScreensWithGroupPaths(HmiSoftware software)
        {
            var screens = new List<(HmiScreen Screen, string GroupPath)>();
            foreach (HmiScreen screen in software.Screens)
            {
                screens.Add((screen, string.Empty));
            }

            foreach (HmiScreenGroup group in software.ScreenGroups)
            {
                CollectScreens(group, group.Name, screens);
            }

            return screens;
        }

        public static (HmiScreen? Screen, string GroupPath) FindScreen(HmiSoftware software, string screenName)
        {
            foreach (var entry in GetScreensWithGroupPaths(software))
            {
                if (entry.Screen.Name == screenName)
                {
                    return entry;
                }
            }

            return (null, string.Empty);
        }

        /// <summary>
        /// The screen group at <paramref name="groupPath"/>, or null when a segment is missing. Not valid for an empty path.
        /// </summary>
        public static HmiScreenGroup? FindScreenGroup(HmiSoftware software, string groupPath)
        {
            HmiScreenGroup? current = null;
            foreach (var segment in SplitPath(groupPath))
            {
                current = current == null ? software.ScreenGroups.Find(segment) : current.Groups.Find(segment);
                if (current == null)
                {
                    return null;
                }
            }

            return current;
        }

        /// <summary>
        /// Creates a screen at the top level or in the screen group at <paramref name="groupPath"/>, creating missing groups.
        /// </summary>
        public static HmiScreen CreateScreen(HmiSoftware software, string screenName, string groupPath)
        {
            var segments = SplitPath(groupPath);
            if (segments.Count == 0)
            {
                return software.Screens.Create(screenName);
            }

            HmiScreenGroup? current = null;
            foreach (var segment in segments)
            {
                var existing = current == null ? software.ScreenGroups.Find(segment) : current.Groups.Find(segment);
                current = existing ?? (current == null ? software.ScreenGroups.Create(segment) : current.Groups.Create(segment));
            }

            return current!.Screens.Create(screenName);
        }

        /// <summary>
        /// Every tag table with its tag table group path.
        /// </summary>
        public static List<(HmiTagTable Table, string GroupPath)> GetTagTablesWithGroupPaths(HmiSoftware software)
        {
            var tables = new List<(HmiTagTable Table, string GroupPath)>();
            foreach (HmiTagTable table in software.TagTables)
            {
                tables.Add((table, string.Empty));
            }

            foreach (HmiTagTableGroup group in software.TagTableGroups)
            {
                CollectTagTables(group, group.Name, tables);
            }

            return tables;
        }

        public static HmiScreenInfo CreateScreenInfo(HmiScreen screen, string groupPath)
        {
            var info = new HmiScreenInfo { Name = screen.Name, GroupPath = groupPath, ItemCount = screen.ScreenItems.Count };
            try
            {
                info.ScreenNumber = screen.ScreenNumber;
                info.Width = screen.Width;
                info.Height = screen.Height;
            }
            catch (EngineeringException)
            {
                // Size attributes are not readable on every screen type; leave them out
            }

            return info;
        }

        /// <summary>
        /// Name, type, position, size and state of a screen item; with <paramref name="includeAttributes"/> also every other
        /// readable attribute with a simple value.
        /// </summary>
        public static HmiScreenItemInfo CreateScreenItemInfo(HmiScreenItemBase item, bool includeAttributes)
        {
            var info = new HmiScreenItemInfo
            {
                Name = item.Name,
                Type = ToTypeName(item.GetType()),
                Left = ReadNumber(item, "Left"),
                Top = ReadNumber(item, "Top"),
                Width = ReadNumber(item, "Width"),
                Height = ReadNumber(item, "Height"),
                Visible = ReadAttribute(item, "Visible") as bool?,
                Enabled = ReadAttribute(item, "Enabled") as bool?
            };

            if (includeAttributes)
            {
                info.Attributes = ReadSimpleAttributes(item);
            }

            return info;
        }

        /// <summary>
        /// Sets attributes from text values, converting each to the attribute's type. Returns one line per change, or
        /// false with an error for the first attribute that cannot be set. With <paramref name="dryRun"/> only converts.
        /// </summary>
        public static bool TrySetAttributes(IEngineeringObject target, IDictionary<string, string> attributes, bool dryRun, IList<string> changes, out string errorMessage)
        {
            errorMessage = string.Empty;
            var converted = new List<(string Name, object? Value)>();
            var texts = new List<(MultilingualText Text, string Name, string Value)>();

            foreach (var pair in attributes)
            {
                var targetType = GetAttributeType(target, pair.Key);
                if (targetType == null)
                {
                    errorMessage = $"Attribute '{pair.Key}' not found on {target.GetType().Name}";
                    return false;
                }

                if (targetType == typeof(MultilingualText))
                {
                    if (!(ReadAttribute(target, pair.Key) is MultilingualText multilingualText))
                    {
                        errorMessage = $"Attribute '{pair.Key}' cannot be read";
                        return false;
                    }

                    texts.Add((multilingualText, pair.Key, pair.Value));
                    continue;
                }

                if (!AttributeValueConverter.TryConvert(pair.Value, targetType, out var value, out var conversionError))
                {
                    errorMessage = $"Attribute '{pair.Key}': {conversionError}";
                    return false;
                }

                converted.Add((pair.Key, value));
            }

            foreach (var entry in converted)
            {
                var previous = ReadAttribute(target, entry.Name);
                if (!dryRun)
                {
                    target.SetAttribute(entry.Name, entry.Value);
                }

                changes.Add($"{entry.Name}: {Format(previous)} -> {Format(entry.Value)}");
            }

            foreach (var entry in texts)
            {
                if (!TrySetMultilingualText(entry.Text, entry.Name, entry.Value, dryRun, changes, out errorMessage))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Sets one language ("de-DE:Start") or all languages (plain text) of a multilingual text.
        /// </summary>
        private static bool TrySetMultilingualText(MultilingualText text, string attributeName, string value, bool dryRun, IList<string> changes, out string errorMessage)
        {
            errorMessage = string.Empty;
            var (culture, newText) = AttributeValueConverter.ParseCultureText(value);
            var matched = 0;

            foreach (MultilingualTextItem item in text.Items)
            {
                var itemCulture = item.Language?.Culture?.Name ?? string.Empty;
                if (culture != null && !string.Equals(itemCulture, culture, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                matched++;
                if (!dryRun)
                {
                    item.Text = newText;
                }

                changes.Add($"{attributeName} [{itemCulture}]: {Format(item.Text)} -> {newText}");
            }

            if (matched == 0)
            {
                var available = new List<string>();
                foreach (MultilingualTextItem item in text.Items)
                {
                    available.Add(item.Language?.Culture?.Name ?? string.Empty);
                }

                errorMessage = $"Attribute '{attributeName}' has no text for language '{culture}'. Available languages: {string.Join(", ", available)}";
                return false;
            }

            return true;
        }

        /// <summary>
        /// The screen item type for a name such as "Button", "HmiButton" or "IOField" (case-insensitive).
        /// </summary>
        public static Type? ResolveScreenItemType(string typeName)
        {
            var types = GetScreenItemTypes();
            var key = NormalizeTypeName(typeName);
            return types.TryGetValue(key, out var type) ? type : null;
        }

        /// <summary>
        /// Names of the screen item types that can be created, without the "Hmi" prefix.
        /// </summary>
        public static IList<string> GetScreenItemTypeNames()
        {
            return GetScreenItemTypes().Values.Select(ToTypeName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Creates a screen item of the given type through the generic HmiScreenItemBaseComposition.Create&lt;T&gt;(name).
        /// </summary>
        public static HmiScreenItemBase CreateScreenItem(HmiScreen screen, Type itemType, string itemName)
        {
            var create = typeof(HmiScreenItemBaseComposition)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .First(m => m.Name == "Create" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);

            try
            {
                return (HmiScreenItemBase)create.MakeGenericMethod(itemType).Invoke(screen.ScreenItems, new object[] { itemName });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }

        /// <summary>
        /// Screen item types that snapshots do not read. Reading the system diagnosis control
        /// (HmiSystemDiagnosisControl) made TIA Portal V21 hang for 26 s and exit with a NonRecoverableException.
        /// </summary>
        public static readonly IReadOnlyList<string> SnapshotSkippedTypes = new[] { "HmiSystemDiagnosisControl" };

        /// <summary>
        /// Owner check for <see cref="HmiSnapshotSerializer"/>: an engineering object or composition belongs to
        /// <paramref name="owner"/> when its Parent is the owner. Anything else in a property (another screen, a tag, a
        /// library type) is a reference the snapshot must not read into. Plain .NET values always belong to their owner.
        /// </summary>
        public static bool IsSnapshotChild(object value, object owner)
        {
            var instance = value as IEngineeringInstance;
            if (instance == null)
            {
                return true;
            }

            var parent = instance.Parent;
            return parent != null && (ReferenceEquals(parent, owner) || parent.Equals(owner));
        }

        /// <summary>
        /// Converts the Openness values <see cref="HmiSnapshotSerializer"/> does not know: a multilingual text becomes
        /// { "&lt;culture&gt;": "&lt;text&gt;" } with one entry per project language. Returns null for every other value.
        /// </summary>
        public static JToken? ConvertSnapshotValue(object value)
        {
            if (!(value is MultilingualText text))
            {
                return null;
            }

            var result = new JObject();
            foreach (MultilingualTextItem item in text.Items)
            {
                result[item.Language?.Culture?.Name ?? string.Empty] = item.Text ?? string.Empty;
            }

            return result;
        }

        public static string ToTypeName(Type type)
        {
            var name = type.Name;
            return name.StartsWith(TypePrefix, StringComparison.Ordinal) && name.Length > TypePrefix.Length ? name.Substring(TypePrefix.Length) : name;
        }

        /// <summary>
        /// Lower-case type name without the "Hmi" prefix, the key screen item types are matched by.
        /// </summary>
        public static string NormalizeTypeName(string typeName)
        {
            var name = (typeName ?? string.Empty).Trim();
            if (name.StartsWith(TypePrefix, StringComparison.OrdinalIgnoreCase) && name.Length > TypePrefix.Length)
            {
                name = name.Substring(TypePrefix.Length);
            }

            return name.ToLowerInvariant();
        }

        private static void CollectScreens(HmiScreenGroup group, string groupPath, List<(HmiScreen Screen, string GroupPath)> screens)
        {
            foreach (HmiScreen screen in group.Screens)
            {
                screens.Add((screen, groupPath));
            }

            foreach (HmiScreenGroup child in group.Groups)
            {
                CollectScreens(child, ExchangePaths.CombineGroupPath(groupPath, child.Name), screens);
            }
        }

        private static void CollectTagTables(HmiTagTableGroup group, string groupPath, List<(HmiTagTable Table, string GroupPath)> tables)
        {
            foreach (HmiTagTable table in group.TagTables)
            {
                tables.Add((table, groupPath));
            }

            foreach (HmiTagTableGroup child in group.Groups)
            {
                CollectTagTables(child, ExchangePaths.CombineGroupPath(groupPath, child.Name), tables);
            }
        }

        private static List<string> SplitPath(string? path)
        {
            return ExchangePaths.CombineGroupPath(path).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        private static Dictionary<string, Type> GetScreenItemTypes()
        {
            lock (ScreenItemTypesLock)
            {
                if (_screenItemTypes != null)
                {
                    return _screenItemTypes;
                }

                Type?[] candidates;
                try
                {
                    candidates = typeof(HmiScreenItemBase).Assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    candidates = ex.Types;
                }

                var types = new Dictionary<string, Type>();
                foreach (var type in candidates)
                {
                    if (type != null && type.IsPublic && !type.IsAbstract && typeof(HmiScreenItemBase).IsAssignableFrom(type))
                    {
                        types[NormalizeTypeName(type.Name)] = type;
                    }
                }

                _screenItemTypes = types;
                return types;
            }
        }

        private static Type? GetAttributeType(IEngineeringObject target, string attributeName)
        {
            var property = target.GetType().GetProperty(attributeName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property != null)
            {
                return property.PropertyType;
            }

            var current = ReadAttribute(target, attributeName);
            return current?.GetType();
        }

        private static object? ReadAttribute(IEngineeringObject target, string attributeName)
        {
            try
            {
                return target.GetAttribute(attributeName);
            }
            catch (EngineeringException)
            {
                return null;
            }
        }

        private static long? ReadNumber(IEngineeringObject target, string attributeName)
        {
            var value = ReadAttribute(target, attributeName);
            if (value == null)
            {
                return null;
            }

            try
            {
                return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException)
            {
                return null;
            }
        }

        private static IDictionary<string, object?> ReadSimpleAttributes(IEngineeringObject target)
        {
            var attributes = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (var info in target.GetAttributeInfos())
            {
                var value = ReadAttribute(target, info.Name);
                if (value != null && AttributeValueConverter.IsSimple(value.GetType()))
                {
                    attributes[info.Name] = AttributeValueConverter.ToClientValue(value);
                }
            }

            return attributes;
        }

        private static string Format(object? value)
        {
            return value == null ? "(none)" : Convert.ToString(AttributeValueConverter.ToClientValue(value), System.Globalization.CultureInfo.InvariantCulture) ?? "(none)";
        }
    }
}
