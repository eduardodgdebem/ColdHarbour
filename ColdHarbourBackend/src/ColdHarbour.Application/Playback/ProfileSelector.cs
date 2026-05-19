namespace ColdHarbour.Application.Playback;

public static class ProfileSelector
{
    public static string Select(
        string trackFormat,
        IReadOnlyList<string> deviceCodecs,
        string? preferredProfile,
        int? bitrateCap)
    {
        var canPassThrough = bitrateCap is null
            && deviceCodecs.Any(c => c.Equals(trackFormat, StringComparison.OrdinalIgnoreCase));

        if (canPassThrough)
            return "original";

        return preferredProfile ?? "mp3-192";
    }
}
