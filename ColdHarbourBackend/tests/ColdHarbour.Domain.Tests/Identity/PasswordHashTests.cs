using ColdHarbour.Domain.Identity;
using FluentAssertions;

namespace ColdHarbour.Domain.Tests.Identity;

public class PasswordHashTests
{
    [Fact]
    public void From_WithValidHash_ReturnsInstance()
    {
        var hash = new string('a', 64);

        var result = PasswordHash.From(hash);

        result.Value.Should().Be(hash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void From_WithEmptyHash_Throws(string? hash)
    {
        var act = () => PasswordHash.From(hash!);

        act.Should().Throw<ArgumentException>();
    }
}
