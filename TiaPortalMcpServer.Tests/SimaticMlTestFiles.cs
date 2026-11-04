using System;
using System.IO;

namespace TiaPortalMcpServer.Tests
{
    /// <summary>
    /// Minimal SimaticML documents and a temporary directory to write them to.
    /// </summary>
    internal sealed class SimaticMlTestFiles : IDisposable
    {
        public SimaticMlTestFiles()
        {
            Root = Path.Combine(Path.GetTempPath(), "SimaticMlTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public static string Document(string elementName, string name)
        {
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                   "<Document>\r\n" +
                   "  <Engineering version=\"V21\" />\r\n" +
                   "  <" + elementName + " ID=\"0\">\r\n" +
                   "    <AttributeList>\r\n" +
                   "      <Interface />\r\n" +
                   "      <Name>" + name + "</Name>\r\n" +
                   "    </AttributeList>\r\n" +
                   "  </" + elementName + ">\r\n" +
                   "</Document>\r\n";
        }

        public static string Block(string name)
        {
            return Document("SW.Blocks.FB", name);
        }

        public static string Type(string name)
        {
            return Document("SW.Types.PlcStruct", name);
        }

        public string Write(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
