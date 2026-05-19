using ColdHarbour.Application.Library.Commands;
using ColdHarbour.Application.Library.Dtos;
using ColdHarbour.Application.Library.Handlers;
using ColdHarbour.Application.Library.Ports;
using ColdHarbour.Application.Library.Queries;
using FluentAssertions;

namespace ColdHarbour.Application.Tests.Library;

public sealed class PreviewSyncAndSyncCommandHandlerTests
{
    [Fact]
    public async Task PreviewHandler_DelegatesToReconciler()
    {
        var diff = new LibrarySyncDiffDto(
            Added: [new LibrarySyncItemDto("/content/library/Artist/Album/new.mp3", "New", "Artist")],
            Missing: [],
            Renamed: []);
        var reconciler = new StubLibraryReconciler(diff);

        var handler = new PreviewLibrarySyncQueryHandler(reconciler);
        var result = await handler.Handle(new PreviewLibrarySyncQuery(), CancellationToken.None);

        result.Added.Should().HaveCount(1);
        result.Added[0].Title.Should().Be("New");
        reconciler.PreviewCalled.Should().BeTrue();
    }

    [Fact]
    public async Task SyncHandler_DelegatesToReconciler_Apply()
    {
        var reconciler = new StubLibraryReconciler(new LibrarySyncDiffDto([], [], []));
        var handler = new SyncLibraryCommandHandler(reconciler);

        await handler.Handle(new SyncLibraryCommand(), CancellationToken.None);

        reconciler.ApplyCalled.Should().BeTrue();
    }

    private sealed class StubLibraryReconciler(LibrarySyncDiffDto diff) : ILibraryReconciler
    {
        public bool PreviewCalled { get; private set; }
        public bool ApplyCalled { get; private set; }

        public Task<LibrarySyncDiffDto> PreviewAsync(CancellationToken ct = default)
        {
            PreviewCalled = true;
            return Task.FromResult(diff);
        }

        public Task ApplyAsync(CancellationToken ct = default)
        {
            ApplyCalled = true;
            return Task.CompletedTask;
        }
    }
}
