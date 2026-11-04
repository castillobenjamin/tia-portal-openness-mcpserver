using System.IO;
using TiaPortalMcpServer.Services;
using Xunit;

namespace TiaPortalMcpServer.Tests
{
    [Trait("Category", "Unit")]
    public class ProjectPathsTests
    {
        [Fact]
        public void TryValidateSaveAsTarget_NewDirectoryInExistingParent_IsValid()
        {
            using (var files = new SimaticMlTestFiles())
            {
                var target = Path.Combine(files.Root, "Line_v2");
                Assert.True(ProjectPaths.TryValidateSaveAsTarget(target, out var fullPath, out var error));
                Assert.Equal(target, fullPath);
                Assert.Equal(string.Empty, error);
            }
        }

        [Fact]
        public void TryValidateSaveAsTarget_EmptyExistingDirectory_IsValid()
        {
            using (var files = new SimaticMlTestFiles())
            {
                Assert.True(ProjectPaths.TryValidateSaveAsTarget(files.Root + Path.DirectorySeparatorChar, out var fullPath, out _));
                Assert.Equal(files.Root, fullPath);
            }
        }

        [Fact]
        public void TryValidateSaveAsTarget_NonEmptyDirectory_IsRejected()
        {
            using (var files = new SimaticMlTestFiles())
            {
                files.Write("Line.ap21", "x");
                Assert.False(ProjectPaths.TryValidateSaveAsTarget(files.Root, out _, out var error));
                Assert.Contains("not empty", error);
            }
        }

        [Fact]
        public void TryValidateSaveAsTarget_MissingParent_IsRejected()
        {
            using (var files = new SimaticMlTestFiles())
            {
                Assert.False(ProjectPaths.TryValidateSaveAsTarget(Path.Combine(files.Root, "missing", "Line_v2"), out _, out var error));
                Assert.Contains("Parent directory", error);
            }
        }

        [Fact]
        public void TryValidateSaveAsTarget_File_IsRejected()
        {
            using (var files = new SimaticMlTestFiles())
            {
                var file = files.Write("Line.ap21", "x");
                Assert.False(ProjectPaths.TryValidateSaveAsTarget(file, out _, out var error));
                Assert.Contains("is a file", error);
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("relative\\Line_v2")]
        public void TryValidateSaveAsTarget_MissingOrRelative_IsRejected(string target)
        {
            Assert.False(ProjectPaths.TryValidateSaveAsTarget(target, out _, out var error));
            Assert.NotEqual(string.Empty, error);
        }
    }
}
