namespace ColdHarbour.Domain.Playback;

public sealed class Device
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Name { get; private set; } = "";
    public string UserAgent { get; private set; } = "";
    public IReadOnlyList<string> SupportedCodecs { get; private set; } = [];
    public string PreferredProfile { get; private set; } = "";
    public int? BitrateCap { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }

    private Device() { }

    public static Device Register(
        Guid id,
        Guid userId,
        string name,
        string userAgent,
        IReadOnlyList<string> supportedCodecs,
        string preferredProfile,
        int? bitrateCap = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(preferredProfile);

        return new Device
        {
            Id = id,
            UserId = userId,
            Name = name,
            UserAgent = userAgent,
            SupportedCodecs = supportedCodecs,
            PreferredProfile = preferredProfile,
            BitrateCap = bitrateCap,
            LastSeenAt = DateTimeOffset.UtcNow,
        };
    }

    public void Heartbeat(string userAgent, IReadOnlyList<string> supportedCodecs, string preferredProfile, int? bitrateCap = null)
    {
        UserAgent = userAgent;
        SupportedCodecs = supportedCodecs;
        PreferredProfile = preferredProfile;
        BitrateCap = bitrateCap;
        LastSeenAt = DateTimeOffset.UtcNow;
    }
}
