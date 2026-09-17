using System.Text.Json;
using BattleShip.Models;

namespace BattleShip.Tests;

public sealed class GameTests
{
    [Fact]
    public void New_game_reveals_only_player_fleet()
    {
        var state = new Game(" Alice ", new Random(42)).GetState();
        Assert.NotEqual(Guid.Empty, state.Id);
        Assert.Equal("Alice", state.PlayerName);
        Assert.Equal(GameStatus.InProgress, state.Status);
        Assert.Equal(0, state.TurnNumber);
        Assert.Equal(100, state.PlayerGrid.Length);
        Assert.Equal(100, state.OpponentGrid.Length);
        Assert.Equal(17, state.PlayerGrid.Count(cell => cell.State == CellState.Ship));
        Assert.All(state.OpponentGrid, cell => Assert.Equal(CellState.Unknown, cell.State));
        Assert.Null(state.LastPlayerShot);
        Assert.Null(state.LastComputerShot);
    }

    [Fact]
    public void New_game_has_a_creation_date_and_empty_history()
    {
        var before = DateTimeOffset.UtcNow;
        var state = new Game("Alice", new Random(42)).GetState();
        Assert.InRange(state.CreatedAtUtc, before, DateTimeOffset.UtcNow);
        Assert.Empty(state.Turns);
    }

    [Fact]
    public void History_keeps_every_accepted_turn_in_order()
    {
        var game = new Game("Alice", new Random(42));
        var first = game.Fire(new Position(0, 0));
        var second = game.Fire(new Position(0, 1));
        Assert.Single(first.Turns);
        Assert.Equal(2, second.Turns.Length);
        Assert.Equal(first.CreatedAtUtc, second.CreatedAtUtc);
        Assert.Equal(new TurnDto(1, first.LastPlayerShot!, first.LastComputerShot), second.Turns[0]);
        Assert.Equal(new TurnDto(2, second.LastPlayerShot!, second.LastComputerShot), second.Turns[1]);
        Assert.Equal(second.Turns, game.GetState().Turns);
    }

    [Fact]
    public void Returned_history_cannot_mutate_the_game()
    {
        var game = new Game("Alice", new Random(42));
        var snapshot = game.Fire(new Position(0, 0));
        var before = JsonSerializer.Serialize(game.GetState());
        snapshot.Turns[0] = new TurnDto(99, new ShotDto(new Position(9, 9), ShotOutcome.Sunk), null);
        Assert.Equal(before, JsonSerializer.Serialize(game.GetState()));
    }

    [Fact]
    public void Returned_arrays_cannot_mutate_the_game()
    {
        var game = new Game("Alice", new Random(42));
        var before = JsonSerializer.Serialize(game.GetState());
        var snapshot = game.GetState();
        snapshot.PlayerGrid[0] = new CellDto(0, 0, CellState.Sunk);
        snapshot.OpponentGrid[0] = new CellDto(0, 0, CellState.Ship);
        Assert.Equal(before, JsonSerializer.Serialize(game.GetState()));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 10)]
    public void Invalid_turn_does_not_trigger_a_computer_shot(int row, int column)
    {
        var game = new Game("Alice", new Random(42));
        var before = JsonSerializer.Serialize(game.GetState());
        Assert.Throws<GameRuleException>(() => game.Fire(new Position(row, column)));
        Assert.Equal(before, JsonSerializer.Serialize(game.GetState()));
    }

    [Fact]
    public void Duplicate_turn_does_not_trigger_a_computer_shot()
    {
        var game = new Game("Alice", new Random(42));
        game.Fire(new Position(0, 0));
        var before = JsonSerializer.Serialize(game.GetState());
        var exception = Assert.Throws<GameRuleException>(() => game.Fire(new Position(0, 0)));
        Assert.Equal(GameError.DuplicateShot, exception.Error);
        Assert.Equal(before, JsonSerializer.Serialize(game.GetState()));
    }

    [Fact]
    public void Computer_uses_unique_valid_targets_and_games_terminate()
    {
        for (var seed = 0; seed < 20; seed++)
        {
            var game = new Game("Alice", new Random(seed));
            HashSet<Position> targets = [];
            var state = game.GetState();
            for (var index = 0; index < 100 && state.Status == GameStatus.InProgress; index++)
            {
                state = game.Fire(new Position(index / 10, index % 10));
                Assert.Equal(index + 1, state.TurnNumber);
                Assert.Equal(state.TurnNumber, state.Turns.Length);
                Assert.Equal(state.LastPlayerShot, state.Turns[^1].PlayerShot);
                Assert.Equal(state.LastComputerShot, state.Turns[^1].ComputerShot);
                Assert.DoesNotContain(state.OpponentGrid, cell => cell.State is CellState.Ship or CellState.Water);
                Assert.Equal(99 - index, state.OpponentGrid.Count(cell => cell.State == CellState.Unknown));
                if (state.LastComputerShot is { } shot)
                {
                    Assert.True(shot.Position.IsValid);
                    Assert.True(targets.Add(shot.Position));
                }
            }
            Assert.NotEqual(GameStatus.InProgress, state.Status);
            var before = JsonSerializer.Serialize(state);
            var exception = Assert.Throws<GameRuleException>(() => game.Fire(new Position(0, 0)));
            Assert.Equal(GameError.GameFinished, exception.Error);
            Assert.Equal(before, JsonSerializer.Serialize(game.GetState()));
        }
    }

    [Fact]
    public void Player_victory_has_no_extra_computer_shot()
    {
        var random = new Random(42);
        Board.CreateRandom(random);
        var opponent = Board.CreateRandom(random);
        var game = new Game("Alice", new Random(42));
        var state = game.GetState();
        foreach (var position in opponent.Ships.SelectMany(ship => ship.Positions))
            state = game.Fire(position);
        Assert.Equal(GameStatus.PlayerWon, state.Status);
        Assert.Equal(17, state.TurnNumber);
        Assert.Null(state.LastComputerShot);
        Assert.Equal(17, state.Turns.Length);
        Assert.Null(state.Turns[^1].ComputerShot);
        Assert.All(state.Turns[..^1], turn => Assert.NotNull(turn.ComputerShot));
        Assert.Equal(17, state.OpponentGrid.Count(cell => cell.State == CellState.Sunk));
        Assert.Equal(16, state.PlayerGrid.Count(cell => cell.State is CellState.Miss or CellState.Hit or CellState.Sunk));
    }

    [Fact]
    public void Computer_can_win()
    {
        var random = new Random(42);
        Board.CreateRandom(random);
        var opponent = Board.CreateRandom(random);
        var occupied = opponent.Ships.SelectMany(ship => ship.Positions).ToHashSet();
        var game = new Game("Alice", new Random(42));
        var state = game.GetState();
        foreach (var position in opponent.AvailableTargets().OrderBy(occupied.Contains))
        {
            state = game.Fire(position);
            if (state.Status != GameStatus.InProgress)
                break;
        }
        Assert.Equal(GameStatus.ComputerWon, state.Status);
        Assert.Equal(17, state.PlayerGrid.Count(cell => cell.State == CellState.Sunk));
    }

    [Fact]
    public async Task Concurrent_identical_shots_only_apply_one_turn()
    {
        var game = new Game("Alice", new Random(42));
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
        {
            try
            {
                game.Fire(new Position(0, 0));
                return true;
            }
            catch (GameRuleException exception) when (exception.Error == GameError.DuplicateShot)
            {
                return false;
            }
        })));
        Assert.Single(results, applied => applied);
        Assert.Equal(1, game.GetState().TurnNumber);
        Assert.Single(game.GetState().Turns);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz")]
    public void Invalid_player_names_are_rejected(string name)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Game(name));
    }
}
