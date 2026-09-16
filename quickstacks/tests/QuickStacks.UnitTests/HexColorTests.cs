using QuickStacks.Domain;
using Xunit;

namespace QuickStacks.UnitTests;

public class HexColorTests
{
    [Theory]
    [InlineData("#1E3A5F")]
    [InlineData("#000000")]
    [InlineData("#FFFFFF")]
    [InlineData("#abcdef")]
    public void IsValid_AcceptsWellFormedHex(string value) => Assert.True(HexColor.IsValid(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1E3A5F")]
    [InlineData("#1E3A5")]
    [InlineData("#1E3A5FF")]
    [InlineData("#GGGGGG")]
    [InlineData("red")]
    public void IsValid_RejectsMalformedInput(string? value) => Assert.False(HexColor.IsValid(value));
}
