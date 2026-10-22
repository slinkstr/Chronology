using Chronology.Services;
using Xunit;

namespace Chronology.Tests;

public class TimeExpressionParserTests
{
    [Theory]
    [InlineData("1:37", "1:37:00")]
    [InlineData("0:30", "0:30:00")]
    [InlineData("90", "1:30:00")]
    [InlineData("45", "0:45:00")]
    [InlineData("0", "0:00:00")]
    [InlineData("1:37 + 0:15", "1:52:00")]
    [InlineData("1:37 - 0:12", "1:25:00")]
    [InlineData("1:37 - 0:12 + 0:30", "1:55:00")]
    [InlineData("  1:37  ", "1:37:00")]
    [InlineData("1:37+0:15", "1:52:00")]
    [InlineData("1:37 - 0:12 + 1:00", "2:25:00")]
    [InlineData("0:12 - 0:30 + 1:00", "0:42:00")]
    public void TryParse_ValidExpressions_ReturnsExpected(string input, string expected)
    {
        Assert.True(TimeExpressionParser.TryParse(input, out var result));
        Assert.Equal(TimeSpan.Parse(expected), result);
    }

    [Theory]
    [InlineData("1:37 - 2:00")]
    [InlineData("0:10 - 0:30")]
    public void TryParse_NegativeResult_ReturnsFalse(string input)
    {
        Assert.False(TimeExpressionParser.TryParse(input, out _));
    }

    [Fact]
    public void TryParse_NegativeResult_AllowNegative_ReturnsParsedValue()
    {
        Assert.True(TimeExpressionParser.TryParse("1:37 - 2:00", out var result, allowNegative: true));
        Assert.Equal(TimeSpan.FromMinutes(-23), result);
    }

    [Theory]
    [InlineData("999999999:00")]
    [InlineData("256204778:49")]
    [InlineData("256204778:00 + 256204778:00")]
    public void TryParse_Overflow_ReturnsFalse(string input)
    {
        Assert.False(TimeExpressionParser.TryParse(input, out _));
    }

    [Fact]
    public void TryParse_MaximalExpression_Parses()
    {
        Assert.True(TimeExpressionParser.TryParse("256204778:48", out var result));
        Assert.Equal(TimeSpan.FromMinutes(15372286728), result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1:")]
    [InlineData(":30")]
    [InlineData("1:60")]
    [InlineData("1:99")]
    [InlineData("1:30:00")]
    [InlineData("-1:30")]
    [InlineData("1:30 +")]
    [InlineData("+ 1:30")]
    [InlineData("1:30 - abc")]
    public void TryParse_InvalidInput_ReturnsFalse(string input)
    {
        Assert.False(TimeExpressionParser.TryParse(input, out _));
    }
}
