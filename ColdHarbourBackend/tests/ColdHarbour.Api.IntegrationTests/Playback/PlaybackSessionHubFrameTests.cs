using System.Net.WebSockets;
using System.Text;
using ColdHarbour.Api.Playback;
using ColdHarbour.Application.Playback.Commands;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace ColdHarbour.Api.IntegrationTests.Playback;

/// <summary>
/// Phase 1 of WS_PROTOCOL_HARDENING: the receive loop must reassemble fragmented
/// WebSocket frames before parsing, and cap the assembled size so a hostile/buggy
/// client cannot truncate a queue or exhaust server memory.
///
/// Following the project's established WS-testing pattern (see PlaybackHubShutdownTests),
/// the reassembly + cap logic is extracted into a static, fake-WebSocket-testable method
/// rather than driven through TestServer's unreliable in-process WS lifecycle.
/// </summary>
public sealed class PlaybackSessionHubFrameTests
{
    private const int OneMb = 1_048_576;

    [Fact]
    public async Task ReadFullMessage_reassembles_a_fragmented_text_message()
    {
        // A setQueue with 200 track IDs (~8 KB) that the browser splits across two frames.
        var ids = Enumerable.Range(0, 200).Select(_ => Guid.NewGuid()).ToArray();
        var json = "{\"type\":\"setQueue\",\"deviceId\":\"" + Guid.NewGuid() +
                   "\",\"trackIds\":[" + string.Join(",", ids.Select(g => $"\"{g}\"")) +
                   "],\"startIndex\":0}";
        var bytes = Encoding.UTF8.GetBytes(json);
        bytes.Length.Should().BeGreaterThan(4096, "the message must actually span more than one receive buffer");

        var first = bytes[..4000];
        var second = bytes[4000..];
        var ws = new ScriptedWebSocket(
            (first, false, WebSocketMessageType.Text),
            (second, true, WebSocketMessageType.Text));

        var read = await PlaybackSessionHub.ReadFullMessageAsync(
            ws, Guid.NewGuid(), OneMb, NullLogger.Instance, CancellationToken.None);

        read.Status.Should().Be(WsFrameStatus.Text);
        Encoding.UTF8.GetString(read.Payload).Should().Be(json);

        // The reassembled message parses to the full 200-id queue — not a truncated prefix.
        var cmd = ParseHub().ParseCommand(Encoding.UTF8.GetString(read.Payload));
        cmd.Should().BeOfType<SetQueueCmd>()
            .Which.TrackIds.Should().HaveCount(200);
    }

    [Fact]
    public async Task ReadFullMessage_accepts_a_payload_of_exactly_the_cap()
    {
        const int cap = 64;
        var payload = Encoding.UTF8.GetBytes(new string('x', cap));
        var ws = new ScriptedWebSocket((payload, true, WebSocketMessageType.Text));

        var read = await PlaybackSessionHub.ReadFullMessageAsync(
            ws, Guid.NewGuid(), cap, NullLogger.Instance, CancellationToken.None);

        read.Status.Should().Be(WsFrameStatus.Text);
        read.Payload.Length.Should().Be(cap);
        ws.ClosedWith.Should().BeNull("an exactly-cap payload is valid and must not close the socket");
    }

    [Fact]
    public async Task ReadFullMessage_rejects_a_payload_one_byte_over_the_cap()
    {
        const int cap = 64;
        var payload = Encoding.UTF8.GetBytes(new string('x', cap + 1));
        var ws = new ScriptedWebSocket((payload, true, WebSocketMessageType.Text));

        var read = await PlaybackSessionHub.ReadFullMessageAsync(
            ws, Guid.NewGuid(), cap, NullLogger.Instance, CancellationToken.None);

        read.Status.Should().Be(WsFrameStatus.TooBig);
        read.Payload.Should().BeEmpty();
        ws.ClosedWith.Should().Be(WebSocketCloseStatus.MessageTooBig, "1009 signals the client its frame was too large");
    }

    [Fact]
    public async Task ReadFullMessage_rejects_when_cumulative_fragments_exceed_the_cap_mid_assembly()
    {
        const int cap = 100;
        // Two 60-byte fragments: each fits, but together (120) blow the cap before EndOfMessage.
        var f1 = Encoding.UTF8.GetBytes(new string('a', 60));
        var f2 = Encoding.UTF8.GetBytes(new string('b', 60));
        var ws = new ScriptedWebSocket(
            (f1, false, WebSocketMessageType.Text),
            (f2, true, WebSocketMessageType.Text));

        var read = await PlaybackSessionHub.ReadFullMessageAsync(
            ws, Guid.NewGuid(), cap, NullLogger.Instance, CancellationToken.None);

        read.Status.Should().Be(WsFrameStatus.TooBig);
        ws.ClosedWith.Should().Be(WebSocketCloseStatus.MessageTooBig);
    }

    [Fact]
    public async Task ReadFullMessage_reports_a_close_frame()
    {
        var ws = new ScriptedWebSocket(([], true, WebSocketMessageType.Close));

        var read = await PlaybackSessionHub.ReadFullMessageAsync(
            ws, Guid.NewGuid(), OneMb, NullLogger.Instance, CancellationToken.None);

        read.Status.Should().Be(WsFrameStatus.Closed);
    }

    [Fact]
    public async Task ReadFullMessage_reports_an_empty_text_frame()
    {
        var ws = new ScriptedWebSocket(([], true, WebSocketMessageType.Text));

        var read = await PlaybackSessionHub.ReadFullMessageAsync(
            ws, Guid.NewGuid(), OneMb, NullLogger.Instance, CancellationToken.None);

        read.Status.Should().Be(WsFrameStatus.Empty);
    }

    private static PlaybackSessionHub ParseHub() =>
        // ParseCommand only needs the logger; the other deps are unused by it.
        new(null!, null!, null!, null!, null!, NullLogger<PlaybackSessionHub>.Instance);

    /// <summary>Plays back a scripted sequence of <c>ReceiveAsync</c> results.</summary>
    private sealed class ScriptedWebSocket : WebSocket
    {
        private readonly Queue<(byte[] Data, bool Eom, WebSocketMessageType Type)> _frames;
        private WebSocketState _state = WebSocketState.Open;

        public WebSocketCloseStatus? ClosedWith { get; private set; }

        public ScriptedWebSocket(params (byte[] Data, bool Eom, WebSocketMessageType Type)[] frames)
            => _frames = new Queue<(byte[], bool, WebSocketMessageType)>(frames);

        public override WebSocketState State => _state;

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct)
        {
            if (_frames.Count == 0)
            {
                _state = WebSocketState.Closed;
                return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
            }

            var (data, eom, type) = _frames.Dequeue();
            data.Length.Should().BeLessThanOrEqualTo(buffer.Count, "scripted frames must fit the receive buffer");
            Array.Copy(data, 0, buffer.Array!, buffer.Offset, data.Length);
            return Task.FromResult(new WebSocketReceiveResult(data.Length, type, eom));
        }

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken ct)
        {
            ClosedWith = closeStatus;
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override WebSocketCloseStatus? CloseStatus => ClosedWith;
        public override string? CloseStatusDescription => null;
        public override string? SubProtocol => null;
        public override void Abort() => _state = WebSocketState.Aborted;
        public override Task CloseOutputAsync(WebSocketCloseStatus s, string? d, CancellationToken ct) => Task.CompletedTask;
        public override Task SendAsync(ArraySegment<byte> b, WebSocketMessageType t, bool e, CancellationToken ct) => Task.CompletedTask;
        public override void Dispose() { }
    }
}
