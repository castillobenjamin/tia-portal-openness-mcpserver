using System;
using TiaPortalMcpServer.Services;
using Xunit;

namespace TiaPortalMcpServer.Tests
{
    [Trait("Category", "Unit")]
    public class AttributeValueConverterTests
    {
        public enum TestAlignment
        {
            Left,
            Center,
            Right
        }

        [Fact]
        public void TryConvert_SupportsSimpleTypes()
        {
            Assert.True(AttributeValueConverter.TryConvert("Start", typeof(string), out var text, out _));
            Assert.Equal("Start", text);

            Assert.True(AttributeValueConverter.TryConvert("true", typeof(bool), out var flag, out _));
            Assert.Equal(true, flag);

            Assert.True(AttributeValueConverter.TryConvert(" 160 ", typeof(uint), out var width, out _));
            Assert.Equal(160u, width);

            Assert.True(AttributeValueConverter.TryConvert("1.5", typeof(double), out var factor, out _));
            Assert.Equal(1.5, factor);

            Assert.True(AttributeValueConverter.TryConvert("center", typeof(TestAlignment), out var alignment, out _));
            Assert.Equal(TestAlignment.Center, alignment);

            Assert.True(AttributeValueConverter.TryConvert("7", typeof(int?), out var nullable, out _));
            Assert.Equal(7, nullable);
        }

        [Theory]
        [InlineData("yes", typeof(bool), "not a valid boolean")]
        [InlineData("-1", typeof(uint), "not a valid UInt32")]
        [InlineData("wide", typeof(int), "not a valid Int32")]
        [InlineData("Middle", typeof(TestAlignment), "Allowed values: Left, Center, Right")]
        [InlineData("x", typeof(Version), "cannot be set from text")]
        public void TryConvert_InvalidValue_ReturnsError(string text, Type type, string expectedError)
        {
            Assert.False(AttributeValueConverter.TryConvert(text, type, out _, out var error));
            Assert.Contains(expectedError, error);
        }

        [Fact]
        public void TryParseAttributeMap_ReadsStringsNumbersAndBooleans()
        {
            Assert.True(AttributeValueConverter.TryParseAttributeMap("{\"Left\": 20, \"Visible\": false, \"Text\": \"Start\", \"Scale\": 1.25}", out var map, out var error));
            Assert.Equal(string.Empty, error);
            Assert.Equal("20", map["Left"]);
            Assert.Equal("false", map["Visible"]);
            Assert.Equal("Start", map["Text"]);
            Assert.Equal("1.25", map["Scale"]);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void TryParseAttributeMap_Empty_ReturnsEmptyMap(string json)
        {
            Assert.True(AttributeValueConverter.TryParseAttributeMap(json, out var map, out _));
            Assert.Empty(map);
        }

        [Theory]
        [InlineData("[1, 2]")]
        [InlineData("{\"Left\": ")]
        [InlineData("{\"Font\": {\"Size\": 12}}")]
        [InlineData("{\"Text\": null}")]
        public void TryParseAttributeMap_Invalid_ReturnsError(string json)
        {
            Assert.False(AttributeValueConverter.TryParseAttributeMap(json, out _, out var error));
            Assert.NotEqual(string.Empty, error);
        }

        [Theory]
        [InlineData("de-DE:Start", "de-DE", "Start")]
        [InlineData("en-US:Mode: Auto", "en-US", "Mode: Auto")]
        [InlineData("Start", null, "Start")]
        [InlineData("Mode:Auto", null, "Mode:Auto")]
        [InlineData("", null, "")]
        public void ParseCultureText_SplitsOptionalCulturePrefix(string value, string expectedCulture, string expectedText)
        {
            var parsed = AttributeValueConverter.ParseCultureText(value);
            Assert.Equal(expectedCulture, parsed.Culture);
            Assert.Equal(expectedText, parsed.Text);
        }
    }
}
