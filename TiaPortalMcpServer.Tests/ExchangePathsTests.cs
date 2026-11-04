using System;
using System.IO;
using System.Linq;
using TiaPortalMcpServer.Services;
using Xunit;

namespace TiaPortalMcpServer.Tests
{
    [Trait("Category", "Unit")]
    public class ExchangePathsTests
    {
        [Theory]
        [InlineData("Motor_FB", "Motor_FB")]
        [InlineData("Motor:FB", "Motor_FB")]
        [InlineData("A/B\\C", "A_B_C")]
        [InlineData("  Spaced  ", "Spaced")]
        [InlineData("", "_")]
        [InlineData("..", "_")]
        public void ToFileName_ReplacesInvalidCharacters(string name, string expected)
        {
            Assert.Equal(expected, ExchangePaths.ToFileName(name));
        }

        [Fact]
        public void CombineGroupPath_JoinsAndNormalizesSeparators()
        {
            Assert.Equal("Motion/Axis/Drives", ExchangePaths.CombineGroupPath("Motion", null, "", "Axis\\Drives/"));
            Assert.Equal("Motion", ExchangePaths.CombineGroupPath("/Motion/"));
            Assert.Equal(string.Empty, ExchangePaths.CombineGroupPath(null, " "));
        }

        [Fact]
        public void ToDirectory_MirrorsGroupPathBelowRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "export");
            Assert.Equal(root, ExchangePaths.ToDirectory(root, ""));
            Assert.Equal(Path.Combine(root, "Motion", "Axis_1"), ExchangePaths.ToDirectory(root, "Motion/Axis:1"));
        }

        [Fact]
        public void ToGroupPath_ReturnsRelativeGroupPath()
        {
            var root = Path.Combine(Path.GetTempPath(), "import");
            Assert.Equal(string.Empty, ExchangePaths.ToGroupPath(root, root + Path.DirectorySeparatorChar));
            Assert.Equal("Motion/Axis", ExchangePaths.ToGroupPath(root, Path.Combine(root, "Motion", "Axis")));
        }

        [Fact]
        public void ToExportDirectory_WithPreservePath_StartsWithRootFolder()
        {
            var root = Path.Combine(Path.GetTempPath(), "export");
            Assert.Equal(root, ExchangePaths.ToExportDirectory(root, ExchangePaths.BlocksRootFolder, "Motion/Axis", false));
            Assert.Equal(Path.Combine(root, "Program blocks"), ExchangePaths.ToExportDirectory(root, ExchangePaths.BlocksRootFolder, "", true));
            Assert.Equal(Path.Combine(root, "PLC data types", "Motion", "Axis"), ExchangePaths.ToExportDirectory(root, ExchangePaths.TypesRootFolder, "Motion/Axis", true));
        }

        [Fact]
        public void ResolveTreeRoot_UsesRootFolderWhenPresent()
        {
            using (var files = new SimaticMlTestFiles())
            {
                var directory = new DirectoryInfo(files.Root);
                Assert.Equal(directory.FullName, ExchangePaths.ResolveTreeRoot(directory, ExchangePaths.BlocksRootFolder).FullName);

                Directory.CreateDirectory(Path.Combine(files.Root, "Program blocks"));
                Assert.Equal(Path.Combine(files.Root, "Program blocks"), ExchangePaths.ResolveTreeRoot(directory, ExchangePaths.BlocksRootFolder).FullName);
            }
        }

        [Fact]
        public void FindFiles_WithPreservePath_ReadsTreeBelowRootFolder()
        {
            using (var files = new SimaticMlTestFiles())
            {
                files.Write(Path.Combine("Program blocks", "Main.s7dcl"), "x");
                files.Write(Path.Combine("Program blocks", "Motion", "Axis", "Axis_FB.s7dcl"), "x");
                files.Write(Path.Combine("Program blocks", "Motion", "Axis", "Axis_FB.s7res"), "x");
                files.Write(Path.Combine("PLC data types", "Axis_UDT.s7dcl"), "x");

                var found = ExchangePaths.FindFiles(new DirectoryInfo(files.Root), ".s7dcl", "Imported", true, "", ExchangePaths.BlocksRootFolder);

                Assert.Equal(2, found.Count);
                Assert.Equal("Imported", found.Single(f => f.Name == "Main").GroupPath);
                Assert.Equal("Imported/Motion/Axis", found.Single(f => f.Name == "Axis_FB").GroupPath);
            }
        }

        [Fact]
        public void FindFiles_WithoutPreservePath_ReadsTopLevelOnlyWithExactExtension()
        {
            using (var files = new SimaticMlTestFiles())
            {
                files.Write("Main.xml", "x");
                files.Write("Main.xmlx", "x");
                files.Write(Path.Combine("Motion", "Axis_FB.xml"), "x");

                var found = ExchangePaths.FindFiles(new DirectoryInfo(files.Root), ".xml", "Motion/", false, "", ExchangePaths.BlocksRootFolder);

                var file = Assert.Single(found);
                Assert.Equal("Main", file.Name);
                Assert.Equal("Motion", file.GroupPath);
            }
        }

        [Fact]
        public void FindFiles_RegexFilter_MatchesFileNames()
        {
            using (var files = new SimaticMlTestFiles())
            {
                files.Write("Motor_FB.s7dcl", "x");
                files.Write("Valve_FC.s7dcl", "x");

                var found = ExchangePaths.FindFiles(new DirectoryInfo(files.Root), ".s7dcl", "", true, "motor", ExchangePaths.BlocksRootFolder);

                Assert.Equal("Motor_FB", Assert.Single(found).Name);
            }
        }

        [Fact]
        public void ToGroupPath_DirectoryOutsideRoot_Throws()
        {
            var root = Path.Combine(Path.GetTempPath(), "import");
            Assert.Throws<ArgumentException>(() => ExchangePaths.ToGroupPath(root, Path.Combine(Path.GetTempPath(), "import2")));
        }
    }
}
