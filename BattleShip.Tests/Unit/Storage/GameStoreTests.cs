using BattleShip.API.Features.Games.Storage;
using BattleShip.Models.Enums;
using Microsoft.Extensions.Options;

namespace BattleShip.Tests.Unit.Storage;

public sealed class GameStoreTests
{
    [Fact]
    public void Store_bounds_capacity_and_reclaims_expired_games()
    {
        var clock = new TestClock();
        var store = new GameStore(Options.Create(new GameStoreOptions { Capacity = 1, LifetimeMinutes = 1 }), clock);
        Assert.True(store.TryCreate("Alice", Difficulty.Normal, out var first));
        Assert.NotNull(first);
        Assert.Same(first, store.Find(first.Id));
        Assert.Equal(clock.Now, first.GetState().CreatedAtUtc);
        Assert.False(store.TryCreate("Bob", Difficulty.Normal, out _));
        clock.Now = clock.Now.AddMinutes(1);
        Assert.True(store.TryCreate("Bob", Difficulty.Normal, out var second));
        Assert.NotNull(second);
        Assert.Null(store.Find(first.Id));
        Assert.Same(second, store.Find(second.Id));
    }

    [Fact]
    public void Expired_game_cannot_be_retrieved()
    {
        var clock = new TestClock();
        var store = new GameStore(Options.Create(new GameStoreOptions { LifetimeMinutes = 1 }), clock);
        Assert.True(store.TryCreate("Alice", Difficulty.Normal, out var game));
        Assert.NotNull(game);
        clock.Now = clock.Now.AddMinutes(2);
        Assert.Null(store.Find(game.Id));
    }

    [Theory]
    [InlineData(Difficulty.Easy)]
    [InlineData(Difficulty.Normal)]
    [InlineData(Difficulty.Hard)]
    public void Store_keeps_the_requested_difficulty(Difficulty difficulty)
    {
        var store = new GameStore(Options.Create(new GameStoreOptions()), new TestClock());
        Assert.True(store.TryCreate("Alice", difficulty, out var game));
        Assert.Equal(difficulty, game!.GetState().Difficulty);
    }

    [Fact]
    public void Store_can_create_a_game_waiting_for_manual_placement()
    {
        var store = new GameStore(Options.Create(new GameStoreOptions()), new TestClock());
        Assert.True(store.TryCreate("Alice", Difficulty.Normal, out var game, manualPlacement: true));
        var state = game!.GetState();
        Assert.Equal(GameStatus.PlacingShips, state.Status);
        Assert.Equal(5, state.ShipsToPlace!.Length);
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
