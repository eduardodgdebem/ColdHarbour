using Microsoft.AspNetCore.Mvc;

namespace ColdHarbourBackend.Controllers;

[ApiController]
[Route("api/music")]
public class MusicController : ControllerBase
{
    public class MusicItem
    {
        public string Name { get; set; }
        public string Author { get; set; }
        public string AudioRef { get; set; }
        public string ImageRef { get; set; }
        public int Id { get; set; }
    }

    public class PlaylistItem
    {
        public string Name { get; set; }
        public string ImageRef { get; set; }
        public int Id { get; set; }
        public List<MusicItem> Musics { get; set; }
    }

    [HttpGet("playlist/{id}")]
    public ActionResult<IEnumerable<PlaylistItem>> GetPlaylist(int id)
    {
        var musics = new List<MusicItem>
        {
            new MusicItem
            {
                Name = "Baby You're Bad",
                Author = "HONNE",
                AudioRef = "/assets/music/babyyourebad.mp3",
                ImageRef = "/assets/images/babyyourebad.jpg",
                Id = 1
            },
            new MusicItem
            {
                Name = "Liz",
                Author = "Remi Wolf",
                AudioRef = "/assets/music/liz.mp3",
                ImageRef = "/assets/images/liz.jpg",
                Id = 2
            }
        };

        var playlist = new PlaylistItem
        {
            Name = "Playlist 1",
            ImageRef = "/assets/images/playlist1.jpg",
            Id = id,
            Musics = musics
        };

        return Ok(playlist);
    }
} 