using System;
using System.IO;
using System.Xml;

namespace TiaPortalMcpServer.Services
{
    /// <summary>
    /// What a SimaticML (.xml) export file contains, read without TIA Portal.
    /// </summary>
    public class SimaticMlFileInfo
    {
        /// <summary>
        /// "Block", "Type", or "Other" for a SimaticML file of another kind (for example a tag table).
        /// </summary>
        public string Kind { get; set; } = SimaticMlInspector.KindOther;

        /// <summary>
        /// Element name of the exported object, such as "SW.Blocks.FB" or "SW.Types.PlcStruct".
        /// </summary>
        public string? ElementName { get; set; }

        /// <summary>
        /// Name of the exported block or type, from its AttributeList.
        /// </summary>
        public string? Name { get; set; }
    }

    /// <summary>
    /// Reads the header of SimaticML export files so imports can be validated and filtered before calling TIA Portal.
    /// </summary>
    public static class SimaticMlInspector
    {
        public const string KindBlock = "Block";
        public const string KindType = "Type";
        public const string KindOther = "Other";

        private const string BlockElementPrefix = "SW.Blocks.";
        private const string TypeElementPrefix = "SW.Types.";

        /// <summary>
        /// Reads the first exported object of a SimaticML file. Returns false with an error when the file is not SimaticML.
        /// </summary>
        public static bool TryInspect(string filePath, out SimaticMlFileInfo info, out string errorMessage)
        {
            info = new SimaticMlFileInfo();
            errorMessage = string.Empty;

            if (!File.Exists(filePath))
            {
                errorMessage = $"File '{filePath}' not found";
                return false;
            }

            try
            {
                using var stream = File.OpenRead(filePath);
                return TryInspect(stream, out info, out errorMessage);
            }
            catch (IOException ex)
            {
                errorMessage = $"Cannot read '{filePath}': {ex.Message}";
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                errorMessage = $"Cannot read '{filePath}': {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Reads the first exported object of a SimaticML document from a stream.
        /// </summary>
        public static bool TryInspect(Stream stream, out SimaticMlFileInfo info, out string errorMessage)
        {
            info = new SimaticMlFileInfo();
            errorMessage = string.Empty;

            // Export files are untrusted input: no DTDs, no external entities
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreWhitespace = true
            };

            try
            {
                using var reader = XmlReader.Create(stream, settings);
                if (!reader.ReadToFollowing("Document"))
                {
                    errorMessage = "Not a SimaticML file: no <Document> root element";
                    return false;
                }

                var documentDepth = reader.Depth;
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.Depth != documentDepth + 1)
                    {
                        continue;
                    }

                    var elementName = reader.LocalName;
                    if (elementName.StartsWith(BlockElementPrefix, StringComparison.Ordinal))
                    {
                        info.Kind = KindBlock;
                    }
                    else if (elementName.StartsWith(TypeElementPrefix, StringComparison.Ordinal))
                    {
                        info.Kind = KindType;
                    }
                    else if (elementName.StartsWith("SW.", StringComparison.Ordinal))
                    {
                        info.Kind = KindOther;
                    }
                    else
                    {
                        // Engineering, DocumentInfo and other header elements
                        continue;
                    }

                    info.ElementName = elementName;
                    info.Name = ReadObjectName(reader);
                    return true;
                }

                errorMessage = "Not a SimaticML export: <Document> contains no exported object";
                return false;
            }
            catch (XmlException ex)
            {
                errorMessage = $"Invalid XML: {ex.Message}";
                return false;
            }
        }

        private static string? ReadObjectName(XmlReader reader)
        {
            var objectDepth = reader.Depth;
            if (reader.IsEmptyElement)
            {
                return null;
            }

            while (reader.Read() && reader.Depth > objectDepth)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "AttributeList" && reader.Depth == objectDepth + 1)
                {
                    var listDepth = reader.Depth;
                    while (reader.Read() && reader.Depth > listDepth)
                    {
                        if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Name" && reader.Depth == listDepth + 1)
                        {
                            return reader.ReadElementContentAsString().Trim();
                        }
                    }

                    return null;
                }
            }

            return null;
        }
    }
}
