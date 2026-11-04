using System;
using TiaPortalMcpServer.Services;
using Xunit;

namespace TiaPortalMcpServer.Tests
{
    [Trait("Category", "Unit")]
    public class EnumFlagsTests
    {
        [Flags]
        public enum TestOptions
        {
            None = 0,
            Override = 1,
            SkipInactiveCultures = 2,
            ActivateInactiveCultures = 4
        }

        [Theory]
        [InlineData("", TestOptions.None)]
        [InlineData(null, TestOptions.None)]
        [InlineData("None", TestOptions.None)]
        [InlineData("Override", TestOptions.Override)]
        [InlineData("override", TestOptions.Override)]
        [InlineData("Override, SkipInactiveCultures", TestOptions.Override | TestOptions.SkipInactiveCultures)]
        [InlineData("Override|ActivateInactiveCultures", TestOptions.Override | TestOptions.ActivateInactiveCultures)]
        [InlineData(" Override ,, SkipInactiveCultures ", TestOptions.Override | TestOptions.SkipInactiveCultures)]
        public void TryParse_ValidNames_ReturnsFlags(string value, TestOptions expected)
        {
            Assert.True(EnumFlags.TryParse<TestOptions>(value, out var result, out var error));
            Assert.Equal(expected, result);
            Assert.Equal(string.Empty, error);
        }

        [Theory]
        [InlineData("Overwrite")]
        [InlineData("1")]
        [InlineData("Override, Bogus")]
        public void TryParse_UnknownValue_ReturnsErrorListingAllowedValues(string value)
        {
            Assert.False(EnumFlags.TryParse<TestOptions>(value, out var result, out var error));
            Assert.Equal(TestOptions.None, result);
            Assert.Contains("Invalid value", error);
            Assert.Contains("SkipInactiveCultures", error);
        }
    }
}
