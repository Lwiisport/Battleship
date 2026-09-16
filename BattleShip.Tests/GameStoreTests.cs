using BattleShip.API.Services;
using Microsoft.Extensions.Options;

namespace BattleShip.Tests;

public sealed class GameStoreTests
{
    [Fact]
    public void Store_bounds_capacity_and_reclaims_expired_games()
    {
        var clock = new TestClock();
        var store = new GameStore(Options.Create(new GameStoreOptions { Capacity = 1, LifetimeMinutes = 1 }), clock);
        Assert.True(store.TryCreate("Alice", out var first));
        Assert.NotNull(first);
        Assert.Same(first, store.Find(first.Id));
        Assert.False(store.TryCreate("Bob", out _));
        clock.Now = clock.Now.AddMinutes(1);
        Assert.True(store.TryCreate("Bob", out var second));
        Assert.NotNull(second);
        Assert.Null(store.Find(first.Id));
        Assert.Same(second, store.Find(second.Id));
    }

    [Fact]
    public void Expired_game_cannot_be_retrieved()
    {
        var clock = new TestClock();
        var store = new GameStore(Options.Create(new GameStoreOptions { LifetimeMinutes = 1 }), clock);
        Assert.True(store.TryCreate("Alice", out var game));
        Assert.NotNull(game);
        clock.Now = clock.Now.AddMinutes(2);
        Assert.Null(store.Find(game.Id));
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
