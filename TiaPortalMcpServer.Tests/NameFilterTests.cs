using System;
using TiaPortalMcpServer.Services;
using Xunit;

namespace TiaPortalMcpServer.Tests
{
    [Trait("Category", "Unit")]
    public class NameFilterTests
    {
        [Theory]
        [InlineData("Motor_FB", "")]
        [InlineData("Motor_FB", null)]
        [InlineData("Motor_FB", "motor")]
        [InlineData("Motor_FB", "^Motor_.*$")]
        [InlineData("Motor_FB", "^motor_fb$")]
        [InlineData("Valve_FC", "Motor|Valve")]
        public void IsMatch_MatchingFilter_ReturnsTrue(string name, string filter)
        {
            Assert.True(NameFilter.IsMatch(name, filter));
        }

        [Theory]
        [InlineData("Motor_FB", "Valve")]
        [InlineData("Motor_FB", "^FB")]
        public void IsMatch_NonMatchingFilter_ReturnsFalse(string name, string filter)
        {
            Assert.False(NameFilter.IsMatch(name, filter));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("Motor")]
        [InlineData("^Motor_.*$")]
        public void TryValidate_ValidFilter_ReturnsTrue(string filter)
        {
            Assert.True(NameFilter.TryValidate(filter, out var error));
            Assert.Equal(string.Empty, error);
        }

        [Fact]
        public void TryValidate_InvalidRegex_ReturnsFalseWithMessage()
        {
            Assert.False(NameFilter.TryValidate("Motor[", out var error));
            Assert.Contains("Motor[", error);
        }

        [Fact]
        public void IsMatch_InvalidRegex_Throws()
        {
            Assert.Throws<ArgumentException>(() => NameFilter.IsMatch("Motor_FB", "Motor["));
        }
    }
}
