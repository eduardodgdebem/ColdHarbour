using System.Collections.Concurrent;
using ColdHarbour.Application.Playback.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace ColdHarbour.Api.Playback;

/// <summary>
/// Singleton registry of per-user PlaybackUserActor instances. Lazily creates actors
/// and evicts idle ones (no connections + 5 min since last command).
/// </summary>
public sealed class PlaybackUserActorRegistry : IHostedService
{
    private static readonly TimeSpan EvictionCheckInterval = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<Guid, PlaybackUserActor> _actors = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPlaybackSessionStore _store;
    private readonly PlaybackConnectionStore _connectionStore;
    private readonly ILoggerFactory _loggerFactory;
    private readonly PeriodicTimer _evictionTimer;
    private Task? _evictionTask;

    public PlaybackUserActorRegistry(
        IServiceScopeFactory scopeFactory,
        IPlaybackSessionStore store,
        PlaybackConnectionStore connectionStore,
        ILoggerFactory loggerFactory)
    {
        _scopeFactory = scopeFactory;
        _store = store;
        _connectionStore = connectionStore;
        _loggerFactory = loggerFactory;
        _evictionTimer = new PeriodicTimer(EvictionCheckInterval);
    }

    public PlaybackUserActor GetOrCreate(Guid userId)
    {
        while (true)
        {
            if (_actors.TryGetValue(userId, out var existing) && !existing.IsDisposed)
                return existing;

            // Actor absent or has been disposed (evicted); clean up the stale entry and create fresh.
            if (existing is not null)
                _actors.TryRemove(new KeyValuePair<Guid, PlaybackUserActor>(userId, existing));

            var actor = CreateActor(userId);
            if (_actors.TryAdd(userId, actor))
                return actor;

            // Another thread won the race — discard the actor we just created.
            _ = actor.DisposeAsync().AsTask();
        }
    }

    public Task StartAsync(CancellationToken ct)
    {
        _evictionTask = Task.Run(() => EvictionLoopAsync(ct), ct);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _evictionTimer.Dispose();
        if (_evictionTask is not null)
            await _evictionTask.ConfigureAwait(false);

        var disposeAll = _actors.Values.Select(a => a.DisposeAsync().AsTask());
        await Task.WhenAll(disposeAll).ConfigureAwait(false);
    }

    private PlaybackUserActor CreateActor(Guid userId) => new(
        userId,
        _store,
        _connectionStore,
        _scopeFactory,
        _loggerFactory.CreateLogger<PlaybackUserActor>());

    private async Task EvictionLoopAsync(CancellationToken ct)
    {
        try
        {
            while (await _evictionTimer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                foreach (var (userId, actor) in _actors)
                {
                    if (!actor.IsIdle) continue;
                    // Key-value overload: only removes the entry if the value is still the same actor.
                    // If a new connection raced in and swapped it, we correctly leave it alone.
                    if (_actors.TryRemove(new KeyValuePair<Guid, PlaybackUserActor>(userId, actor)))
                        await actor.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) { }
    }
}
