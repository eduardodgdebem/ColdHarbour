namespace ColdHarbour.Application.Playback.Ports;

public interface ITranscodeService
{
    /// <summary>
    /// Returns the path to the cached (or freshly transcoded) file.
    /// Returns null when the source file does not exist.
    /// Throws when FFmpeg fails.
    /// </summary>
    Task<string?> GetOrTranscodeAsync(string sourcePath, string audioSha1, string profile, CancellationToken ct = default);
}
