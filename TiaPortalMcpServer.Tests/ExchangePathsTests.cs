using System;
using System.IO;
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
        public void ToGroupPath_DirectoryOutsideRoot_Throws()
        {
            var root = Path.Combine(Path.GetTempPath(), "import");
            Assert.Throws<ArgumentException>(() => ExchangePaths.ToGroupPath(root, Path.Combine(Path.GetTempPath(), "import2")));
        }
    }
}
