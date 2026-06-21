using ColdHarbour.Domain.Library;
using FluentAssertions;

namespace ColdHarbour.Domain.Tests.Library;

public class ArtistTests
{
    [Fact]
    public void Create_WithValidName_SetsIdAndName()
    {
        var artist = Artist.Create("Pink Floyd");

        artist.Id.Should().NotBeEmpty();
        artist.Name.Should().Be("Pink Floyd");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithNullOrWhitespaceName_Throws(string? name)
    {
        var act = () => Artist.Create(name!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rename_WithValidName_UpdatesAndTrims()
    {
        var artist = Artist.Create("Pink Floid");

        artist.Rename("  Pink Floyd  ");

        artist.Name.Should().Be("Pink Floyd");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_WithBlankName_Throws(string? name)
    {
        var artist = Artist.Create("Pink Floyd");

        var act = () => artist.Rename(name!);

        act.Should().Throw<ArgumentException>();
    }
}
