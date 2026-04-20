using ColdHarbour.Domain.Library;
using FluentAssertions;

namespace ColdHarbour.Domain.Tests.Library;

public class AlbumTests
{
    private static readonly Guid ValidArtistId = Guid.NewGuid();

    [Fact]
    public void Create_WithValidInputs_SetsAllProperties()
    {
        var coverPath = "/content/library/Pink Floyd/The Wall/cover.jpg";

        var album = Album.Create("The Wall", ValidArtistId, 1979, coverPath);

        album.Id.Should().NotBeEmpty();
        album.Title.Should().Be("The Wall");
        album.ArtistId.Should().Be(ValidArtistId);
        album.Year.Should().Be(1979);
        album.CoverPath.Should().Be(coverPath);
    }

    [Fact]
    public void Create_WithNullTitle_Throws()
    {
        var act = () => Album.Create(null!, ValidArtistId);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithWhitespaceTitle_Throws()
    {
        var act = () => Album.Create("   ", ValidArtistId);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithEmptyArtistId_Throws()
    {
        var act = () => Album.Create("The Wall", Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }
}
