using System.Net.WebSockets;
using ColdHarbour.Api.Playback;
using FluentAssertions;

namespace ColdHarbour.Api.IntegrationTests.Playback;

/// <summary>
/// Verifies the WebSocket close code the hub selects when the server is shutting down.
///
/// The close-code selection is extracted into <see cref="PlaybackSessionHub.PickCloseStatus"/>
/// so it can be tested without fighting TestServer's in-process lifecycle (TestServer's
/// StopAsync is a no-op and doesn't propagate to RequestAborted for in-flight WS connections).
/// </summary>
public sealed class PlaybackHubShutdownTests
{
    /// <summary>
    /// When the host cancels <c>RequestAborted</c> (i.e., the server is shutting down),
    /// the hub must close with <b>1001 EndpointUnavailable</b> so the client knows to reconnect.
    /// Code 1000 (NormalClosure) suppresses client-side reconnect.
    /// </summary>
    [Fact]
    public void PickCloseStatus_ReturnEndpointUnavailable_WhenRequestAbortedIsCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // simulate RequestAborted being triggered by shutdown

        var status = PlaybackSessionHub.PickCloseStatus(cts.Token);

        status.Should().Be(WebSocketCloseStatus.EndpointUnavailable,
            because: "1001 (Going Away) signals the client that the server went away and it should reconnect; " +
                     "1000 (NormalClosure) would suppress reconnect");
    }

    /// <summary>
    /// When the connection ends for a normal reason (client sent close, or the loop
    /// exited cleanly before any shutdown), the hub must close with <b>1000 NormalClosure</b>.
    /// </summary>
    [Fact]
    public void PickCloseStatus_ReturnsNormalClosure_WhenRequestAbortedIsNotCancelled()
    {
        using var cts = new CancellationTokenSource(); // not cancelled

        var status = PlaybackSessionHub.PickCloseStatus(cts.Token);

        status.Should().Be(WebSocketCloseStatus.NormalClosure,
            because: "a non-shutdown close should be reported as a clean 1000 disconnect");
    }
}
