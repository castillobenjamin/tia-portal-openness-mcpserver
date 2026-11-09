using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using TiaPortalMcpServer.Models;

namespace TiaPortalMcpServer.Services
{
    /// <summary>
    /// Rewrites the values of an AutomationML (CAEX) file that change on every export, so an unchanged project
    /// exports to identical bytes. Element IDs become GUIDs derived from the element's path (element name, Name attribute,
    /// position among equally named siblings), references to those IDs are rewritten to match, and writer timestamps
    /// get a fixed value. Nothing is sorted, added or removed.
    /// </summary>
    public static class AmlNormalizer
    {
        /// <summary>
        /// Value written to timestamp fields.
        /// </summary>
        public const string FixedTimestamp = "1970-01-01T00:00:00";

        private const string CrLf = "\r\n";

        /// <summary>
        /// Elements and attributes holding the time the file was written.
        /// </summary>
        private static readonly HashSet<string> TimestampNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "LastWritingDateTime"
        };

        /// <summary>
        /// CAEX attributes that refer to other objects; IDs that are not GUIDs are rewritten only here.
        /// </summary>
        private static readonly HashSet<string> ReferenceAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            "RefPartnerSideA",
            "RefPartnerSideB",
            "RefBaseSystemUnitPath"
        };

        private static readonly Regex GuidPattern = new Regex(
            "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex WholeGuidPattern = new Regex(
            "^\\{?[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\\}?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Normalizes a UTF-8 .aml file in place. The file is only rewritten when its content changes. Everything before
        /// and after the root element (XML declaration, trailing line break), the byte order mark and the line endings
        /// are kept as they are.
        /// </summary>
        public static AmlNormalizationResult NormalizeFile(string path)
        {
            var original = File.ReadAllBytes(path);
            var hasBom = original.Length >= 3 && original[0] == 0xEF && original[1] == 0xBB && original[2] == 0xBF;
            var text = new UTF8Encoding(false).GetString(original, hasBom ? 3 : 0, original.Length - (hasBom ? 3 : 0));

            XDocument document;
            using (var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
            {
                document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            }

            var result = Normalize(document);
            if (document.Root == null)
            {
                return result;
            }

            var settings = new XmlWriterSettings
            {
                OmitXmlDeclaration = true,
                ConformanceLevel = ConformanceLevel.Fragment,
                NewLineChars = text.Contains(CrLf) ? CrLf : "\n",
                NewLineHandling = NewLineHandling.Replace
            };
            var builder = new StringBuilder(text.Length);
            builder.Append(text, 0, RootStart(text));
            using (var writer = XmlWriter.Create(builder, settings))
            {
                document.Root.WriteTo(writer);
            }
            var trimmed = text.TrimEnd();
            builder.Append(text, trimmed.Length, text.Length - trimmed.Length);

            var normalized = new UTF8Encoding(hasBom).GetPreamble().Concat(new UTF8Encoding(false).GetBytes(builder.ToString())).ToArray();
            result.FileChanged = !original.SequenceEqual(normalized);
            if (result.FileChanged)
            {
                File.WriteAllBytes(path, normalized);
            }
            return result;
        }

        /// <summary>
        /// Normalizes a parsed AML document in place.
        /// </summary>
        public static AmlNormalizationResult Normalize(XDocument document)
        {
            var result = new AmlNormalizationResult();
            if (document.Root == null)
            {
                return result;
            }

            // Pass 1: derive the new ID of every element that has one
            var guidMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var plainMap = new Dictionary<string, string>(StringComparer.Ordinal);
            var idAttributes = new List<KeyValuePair<XAttribute, string>>();
            CollectIds(document.Root, string.Empty, guidMap, plainMap, idAttributes);

            foreach (var entry in idAttributes)
            {
                if (!string.Equals(entry.Key.Value, entry.Value, StringComparison.Ordinal))
                {
                    entry.Key.Value = entry.Value;
                    result.IdsRewritten++;
                }
            }

            var idAttributeSet = new HashSet<XAttribute>(idAttributes.Select(e => e.Key));

            // Pass 2: references and timestamps
            foreach (var element in document.Root.DescendantsAndSelf())
            {
                if (TimestampNames.Contains(element.Name.LocalName) && !element.HasElements)
                {
                    if (!string.Equals(element.Value, FixedTimestamp, StringComparison.Ordinal))
                    {
                        element.Value = FixedTimestamp;
                        result.TimestampsRewritten++;
                    }
                    continue;
                }

                foreach (var attribute in element.Attributes())
                {
                    if (idAttributeSet.Contains(attribute) || attribute.IsNamespaceDeclaration)
                    {
                        continue;
                    }

                    if (TimestampNames.Contains(attribute.Name.LocalName))
                    {
                        if (!string.Equals(attribute.Value, FixedTimestamp, StringComparison.Ordinal))
                        {
                            attribute.Value = FixedTimestamp;
                            result.TimestampsRewritten++;
                        }
                        continue;
                    }

                    var value = RewriteReferences(attribute.Value, guidMap, ReferenceAttributes.Contains(attribute.Name.LocalName) ? plainMap : null, result);
                    if (!ReferenceEquals(value, attribute.Value))
                    {
                        attribute.Value = value;
                    }
                }

                foreach (var text in element.Nodes().OfType<XText>())
                {
                    var value = RewriteReferences(text.Value, guidMap, null, result);
                    if (!ReferenceEquals(value, text.Value))
                    {
                        text.Value = value;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// GUID derived from a path, in the style (braces, letter case) of the ID it replaces.
        /// </summary>
        public static string StableId(string path, string originalId)
        {
            byte[] hash;
            using (var sha1 = SHA1.Create())
            {
                hash = sha1.ComputeHash(Encoding.UTF8.GetBytes("aml:" + path));
            }

            var bytes = new byte[16];
            Array.Copy(hash, bytes, 16);
            bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50); // version 5 (name based, SHA-1)
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
            var text = new Guid(bytes).ToString("D");

            if (originalId.Any(char.IsUpper))
            {
                text = text.ToUpperInvariant();
            }
            return originalId.StartsWith("{", StringComparison.Ordinal) ? "{" + text + "}" : text;
        }

        private static void CollectIds(
            XElement element,
            string parentPath,
            Dictionary<string, string> guidMap,
            Dictionary<string, string> plainMap,
            List<KeyValuePair<XAttribute, string>> idAttributes)
        {
            var path = parentPath + "/" + Segment(element);
            var id = element.Attribute("ID");
            if (id != null && id.Value.Length > 0)
            {
                var oldId = id.Value;
                var newId = StableId(path, oldId);
                idAttributes.Add(new KeyValuePair<XAttribute, string>(id, newId));
                if (WholeGuidPattern.IsMatch(oldId))
                {
                    guidMap[oldId.Trim('{', '}')] = newId.Trim('{', '}').ToLowerInvariant();
                }
                else
                {
                    plainMap[oldId] = newId;
                }
            }

            foreach (var child in element.Elements())
            {
                CollectIds(child, path, guidMap, plainMap, idAttributes);
            }
        }

        /// <summary>
        /// Element name, Name attribute and, for siblings with the same name and Name, the position among them.
        /// </summary>
        private static string Segment(XElement element)
        {
            var name = (string?)element.Attribute("Name") ?? string.Empty;
            var index = 0;
            foreach (var sibling in element.ElementsBeforeSelf())
            {
                if (sibling.Name == element.Name && string.Equals((string?)sibling.Attribute("Name") ?? string.Empty, name, StringComparison.Ordinal))
                {
                    index++;
                }
            }

            var segment = element.Name.LocalName + "[" + name + "]";
            return index == 0 ? segment : segment + "#" + index.ToString(CultureInfo.InvariantCulture);
        }

        private static string RewriteReferences(string value, Dictionary<string, string> guidMap, Dictionary<string, string>? plainMap, AmlNormalizationResult result)
        {
            if (plainMap != null && plainMap.Count > 0)
            {
                // Reference attributes hold "<ID>" or "<ID>:<interface name>"
                var separator = value.IndexOf(':');
                var target = separator < 0 ? value : value.Substring(0, separator);
                if (plainMap.TryGetValue(target, out var replacement) && !string.Equals(replacement, target, StringComparison.Ordinal))
                {
                    result.ReferencesRewritten++;
                    return replacement + (separator < 0 ? string.Empty : value.Substring(separator));
                }
            }

            if (guidMap.Count == 0 || value.Length < 36)
            {
                return value;
            }

            var changed = false;
            var rewritten = GuidPattern.Replace(value, match =>
            {
                if (!guidMap.TryGetValue(match.Value, out var newGuid))
                {
                    return match.Value;
                }

                if (match.Value.Any(char.IsUpper))
                {
                    newGuid = newGuid.ToUpperInvariant();
                }
                if (!string.Equals(newGuid, match.Value, StringComparison.Ordinal))
                {
                    changed = true;
                    result.ReferencesRewritten++;
                }
                return newGuid;
            });
            return changed ? rewritten : value;
        }

        /// <summary>
        /// Index of the root element's start tag, after the XML declaration, processing instructions, comments and whitespace.
        /// </summary>
        private static int RootStart(string text)
        {
            var index = 0;
            while (index < text.Length)
            {
                if (char.IsWhiteSpace(text[index]))
                {
                    index++;
                }
                else if (string.CompareOrdinal(text, index, "<?", 0, 2) == 0)
                {
                    index = SkipPast(text, index, "?>");
                }
                else if (string.CompareOrdinal(text, index, "<!--", 0, 4) == 0)
                {
                    index = SkipPast(text, index, "-->");
                }
                else
                {
                    return index;
                }
            }
            return index;
        }

        private static int SkipPast(string text, int index, string terminator)
        {
            var end = text.IndexOf(terminator, index, StringComparison.Ordinal);
            return end < 0 ? text.Length : end + terminator.Length;
        }
    }
}
