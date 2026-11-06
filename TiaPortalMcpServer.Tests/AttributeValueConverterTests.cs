using System;
using System.Drawing;
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

        [Theory]
        [InlineData("#2D323C", 255, 45, 50, 60)]
        [InlineData("#802d323c", 128, 45, 50, 60)]
        [InlineData("45, 50, 60", 255, 45, 50, 60)]
        [InlineData("128,45,50,60", 128, 45, 50, 60)]
        [InlineData("red", 255, 255, 0, 0)]
        [InlineData("Transparent", 0, 255, 255, 255)]
        public void TryConvert_Color_AcceptsHexChannelsAndNames(string text, int a, int r, int g, int b)
        {
            Assert.True(AttributeValueConverter.TryConvert(text, typeof(Color), out var value, out var error));
            Assert.Equal(string.Empty, error);
            var color = Assert.IsType<Color>(value);
            Assert.Equal(Color.FromArgb(a, r, g, b).ToArgb(), color.ToArgb());
        }

        [Theory]
        [InlineData("#12345")]
        [InlineData("#GG0000")]
        [InlineData("256,0,0")]
        [InlineData("1,2")]
        [InlineData("-1,0,0")]
        [InlineData("Reddish")]
        [InlineData("")]
        public void TryConvert_InvalidColor_ReturnsError(string text)
        {
            Assert.False(AttributeValueConverter.TryConvert(text, typeof(Color), out _, out var error));
            Assert.Contains("not a valid color", error);
        }

        [Fact]
        public void FormatColor_RoundTripsThroughTryConvert()
        {
            Assert.Equal("#2D323C", AttributeValueConverter.FormatColor(Color.FromArgb(255, 45, 50, 60)));
            Assert.Equal("#802D323C", AttributeValueConverter.FormatColor(Color.FromArgb(128, 45, 50, 60)));

            var original = Color.FromArgb(10, 20, 30, 40);
            Assert.True(AttributeValueConverter.TryConvert(AttributeValueConverter.FormatColor(original), typeof(Color), out var value, out _));
            Assert.Equal(original.ToArgb(), ((Color)value).ToArgb());
        }

        [Fact]
        public void ToClientValue_FormatsColorsAndEnums()
        {
            Assert.True(AttributeValueConverter.IsSimple(typeof(Color)));
            Assert.Equal("#FF0000", AttributeValueConverter.ToClientValue(Color.Red));
            Assert.Equal("Center", AttributeValueConverter.ToClientValue(TestAlignment.Center));
            Assert.Equal(7, AttributeValueConverter.ToClientValue(7));
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
