using System.Text.Json;
using System.Text.Json.Nodes;
using BattleShip.App.Features.History.Services;
using BattleShip.Models.Contracts;
using BattleShip.Models.Engine;
using BattleShip.Models.Enums;
using Microsoft.JSInterop;

namespace BattleShip.Tests.Unit.History;

public sealed class GameHistoryStoreTests
{
    [Fact]
    public async Task Empty_storage_loads_an_empty_history()
    {
        var store = new GameHistoryStore(new FakeStorage());
        await store.LoadAsync();
        Assert.Empty(store.Entries);
        Assert.Null(store.Warning);
    }

    [Fact]
    public async Task History_survives_a_new_store_and_keeps_the_complete_game()
    {
        var storage = new FakeStorage();
        var store = new GameHistoryStore(storage);
        var game = new Game("Alice", new Random(42));
        await store.RememberAsync(game.GetState());
        game.Fire(new Position(0, 0));
        var state = game.Fire(new Position(0, 1));
        await store.RememberAsync(state);
        var reopened = new GameHistoryStore(storage);
        await reopened.LoadAsync();
        var entry = Assert.Single(reopened.Entries);
        Assert.Equal(JsonSerializer.Serialize(state), JsonSerializer.Serialize(entry.State));
        Assert.Equal(2, entry.State.Turns.Length);
        Assert.DoesNotContain(entry.State.OpponentGrid, cell => cell.State is CellState.Ship or CellState.Water);
        Assert.Null(reopened.Warning);
    }

    [Fact]
    public async Task Older_snapshot_does_not_overwrite_a_more_recent_turn()
    {
        var storage = new FakeStorage();
        var firstTab = new GameHistoryStore(storage);
        var secondTab = new GameHistoryStore(storage);
        var game = new Game("Alice", new Random(42));
        var initial = game.GetState();
        var updated = game.Fire(new Position(0, 0));
        await firstTab.RememberAsync(updated);
        await secondTab.RememberAsync(initial);
        Assert.Equal(1, Assert.Single(secondTab.Entries).State.TurnNumber);
        await firstTab.LoadAsync();
        Assert.Equal(1, Assert.Single(firstTab.Entries).State.TurnNumber);
    }

    [Fact]
    public async Task Sequential_writes_from_separate_stores_preserve_other_games()
    {
        var storage = new FakeStorage();
        var first = new GameHistoryStore(storage);
        var second = new GameHistoryStore(storage);
        await first.RememberAsync(new Game("Alice", new Random(1)).GetState());
        await second.RememberAsync(new Game("Bob", new Random(2)).GetState());
        await first.LoadAsync();
        Assert.Equal(2, first.Entries.Count);
    }

    [Fact]
    public async Task History_retains_only_the_fifty_most_recent_games()
    {
        var storage = new FakeStorage();
        var template = new Game("Alice", new Random(42)).GetState();
        var entries = Enumerable.Range(0, 51).Select(index => new GameHistoryEntry(
            template with { Id = Guid.NewGuid(), CreatedAtUtc = template.CreatedAtUtc.AddMinutes(index) },
            DateTimeOffset.UtcNow)).ToArray();
        storage.Json = JsonSerializer.Serialize(entries);
        var store = new GameHistoryStore(storage);
        await store.LoadAsync();
        Assert.Equal(GameHistoryStore.Capacity, store.Entries.Count);
        Assert.Equal(entries[^1].State.Id, store.Entries[0].State.Id);
        Assert.DoesNotContain(store.Entries, entry => entry.State.Id == entries[0].State.Id);
        await store.RememberAsync(entries[^1].State);
        Assert.Equal(50, JsonSerializer.Deserialize<GameHistoryEntry[]>(storage.Json!)!.Length);
    }

    [Theory]
    [InlineData("{invalid")]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    public async Task Corrupt_storage_is_not_overwritten_and_does_not_prevent_play(string json)
    {
        var storage = new FakeStorage { Json = json };
        var store = new GameHistoryStore(storage);
        await store.LoadAsync();
        Assert.Empty(store.Entries);
        Assert.NotNull(store.Warning);
        var state = new Game("Alice", new Random(42)).GetState();
        await store.RememberAsync(state);
        Assert.Equal(state.Id, Assert.Single(store.Entries).State.Id);
        Assert.Equal(json, storage.Json);
        Assert.Equal(0, storage.Writes);
        Assert.NotNull(store.Warning);
    }

    [Fact]
    public async Task Invalid_archived_grid_is_rejected_without_an_exception_in_the_ui()
    {
        var state = new Game("Alice", new Random(42)).GetState() with { OpponentGrid = [] };
        var storage = new FakeStorage { Json = JsonSerializer.Serialize(new[] { new GameHistoryEntry(state, DateTimeOffset.UtcNow) }) };
        var store = new GameHistoryStore(storage);
        await store.LoadAsync();
        Assert.Empty(store.Entries);
        Assert.NotNull(store.Warning);
    }

    [Fact]
    public async Task Quota_failure_keeps_history_in_memory_and_recovers_on_next_save()
    {
        var storage = new FakeStorage { FailWrites = true };
        var store = new GameHistoryStore(storage);
        var state = new Game("Alice", new Random(42)).GetState();
        await store.RememberAsync(state);
        Assert.Single(store.Entries);
        Assert.NotNull(store.Warning);
        Assert.Null(storage.Json);
        storage.FailWrites = false;
        await store.RememberAsync(state);
        Assert.Null(store.Warning);
        Assert.NotNull(storage.Json);
    }

    [Fact]
    public async Task Completed_games_keep_their_outcome_and_last_winning_turn()
    {
        var random = new Random(42);
        Board.CreateRandom(random);
        var opponent = Board.CreateRandom(random);
        var game = new Game("Alice", new Random(42));
        foreach (var position in opponent.Ships.SelectMany(ship => ship.Positions))
            game.Fire(position);
        var storage = new FakeStorage();
        await new GameHistoryStore(storage).RememberAsync(game.GetState());
        var reopened = new GameHistoryStore(storage);
        await reopened.LoadAsync();
        var state = Assert.Single(reopened.Entries).State;
        Assert.Equal(GameStatus.PlayerWon, state.Status);
        Assert.Equal(17, state.Turns.Length);
        Assert.Null(state.Turns[^1].ComputerShot);
    }

    [Theory]
    [InlineData(GameAction.Mine)]
    [InlineData(GameAction.SquareStrike)]
    [InlineData(GameAction.RowStrike)]
    [InlineData(GameAction.ColumnStrike)]
    public async Task Powers_and_points_survive_archiving(GameAction action)
    {
        var game = new Game("Alice", new Random(42));
        for (var index = 0; index < 6; index++)
            game.Fire(new Position(0, index));
        var target = action == GameAction.Mine ? game.GetState().PlayerGrid.Last(cell => cell.State is CellState.Ship or CellState.Water)
            : new CellDto(2, 8, CellState.Unknown);
        var state = game.UsePower(action, new Position(target.Row, target.Column));
        var storage = new FakeStorage();
        await new GameHistoryStore(storage).RememberAsync(state);
        var reopened = new GameHistoryStore(storage);
        await reopened.LoadAsync();
        Assert.Null(reopened.Warning);
        Assert.Equal(JsonSerializer.Serialize(state), JsonSerializer.Serialize(Assert.Single(reopened.Entries).State));
    }

    [Fact]
    public async Task Archives_without_power_fields_remain_readable()
    {
        var state = new Game("Alice", new Random(42)).Fire(new Position(0, 0));
        var json = JsonSerializer.SerializeToNode(new[] { new GameHistoryEntry(state, DateTimeOffset.UtcNow) })!;
        var oldState = json[0]!["State"]!.AsObject();
        oldState.Remove("SkillPoints");
        foreach (var grid in new[] { "PlayerGrid", "OpponentGrid" })
            foreach (var cell in oldState[grid]!.AsArray())
                cell!.AsObject().Remove("HasMine");
        foreach (var turn in oldState["Turns"]!.AsArray())
            foreach (var property in new[] { "Action", "Target", "PlayerShots", "MineDetonation", "SkillPointsAfter" })
                turn!.AsObject().Remove(property);
        var storage = new FakeStorage { Json = json.ToJsonString() };
        var reopened = new GameHistoryStore(storage);
        await reopened.LoadAsync();
        Assert.Null(reopened.Warning);
        var restored = Assert.Single(reopened.Entries).State;
        Assert.Equal(0, restored.SkillPoints);
        Assert.Equal(state.Turns[0].PlayerShot, Assert.Single(restored.Turns[0].GetPlayerShots()));
        Assert.Equal(GameAction.NormalShot, restored.Turns[0].Action);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public async Task Archives_with_impossible_skill_points_are_rejected(int points)
    {
        var state = new Game("Alice", new Random(42)).GetState() with { SkillPoints = points };
        var storage = new FakeStorage { Json = JsonSerializer.Serialize(new[] { new GameHistoryEntry(state, DateTimeOffset.UtcNow) }) };
        var store = new GameHistoryStore(storage);
        await store.LoadAsync();
        Assert.Empty(store.Entries);
        Assert.NotNull(store.Warning);
    }

    private sealed class FakeStorage : IJSRuntime
    {
        public string? Json { get; set; }
        public bool FailWrites { get; set; }
        public int Writes { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Assert.Equal(GameHistoryStore.StorageKey, args![0]);
            if (identifier == "localStorage.getItem")
                return ValueTask.FromResult((TValue)(object?)Json!);
            Assert.Equal("localStorage.setItem", identifier);
            if (FailWrites)
                throw new JSException("Quota exceeded");
            Json = (string)args[1]!;
            Writes++;
            return ValueTask.FromResult(default(TValue)!);
        }
    }
}
