using System.Text.Json;
using BattleShip.Models.Contracts;
using BattleShip.Models.Engine;
using BattleShip.Models.Enums;
using BattleShip.Models.Exceptions;

namespace BattleShip.Tests.Unit.Engine;

public sealed class PowerTests
{
    [Fact]
    public void Only_accepted_normal_shots_grant_points_capped_at_ten()
    {
        var game = new Game("Alice", new Random(42));
        Assert.Equal(0, game.GetState().SkillPoints);
        for (var index = 0; index < 12; index++)
        {
            var state = game.Fire(new Position(index / 10, index % 10));
            Assert.Equal(Math.Min(index + 1, 10), state.SkillPoints);
            Assert.Equal(state.SkillPoints, state.Turns[^1].SkillPointsAfter);
        }
        Unchanged(game, () => game.Fire(new Position(0, 0)), GameError.DuplicateShot);
        Unchanged(game, () => game.Fire(new Position(-1, 0)), GameError.InvalidPosition);
    }

    [Theory]
    [InlineData(GameAction.Mine, 2)]
    [InlineData(GameAction.SquareStrike, 4)]
    [InlineData(GameAction.RowStrike, 6)]
    [InlineData(GameAction.ColumnStrike, 6)]
    public void Insufficient_points_leave_everything_unchanged(GameAction action, int cost)
    {
        var game = new Game("Alice", new Random(42));
        Assert.Equal(cost, PowerRules.Cost(action));
        Unchanged(game, () => game.UsePower(action, new Position(2, 2)), GameError.InsufficientSkillPoints);
    }

    [Theory]
    [InlineData(GameAction.SquareStrike, 4, 4)]
    [InlineData(GameAction.RowStrike, 6, 10)]
    [InlineData(GameAction.ColumnStrike, 6, 10)]
    public void Area_attacks_cost_points_and_produce_one_computer_response(GameAction action, int cost, int count)
    {
        var scenario = CreateScenario(42);
        var anchor = new Position(2, 2);
        var expected = PowerRules.Targets(action, anchor);
        Charge(scenario, cost, expected);
        var state = scenario.Game.UsePower(action, anchor);
        Assert.Equal(0, state.SkillPoints);
        Assert.Equal(cost + 1, state.TurnNumber);
        Assert.NotNull(state.LastComputerShot);
        Assert.Equal(cost + 1, state.PlayerGrid.Count(IsShot));
        Assert.Equal(count, state.Turns[^1].GetPlayerShots().Count);
        Assert.Equal(expected, state.Turns[^1].GetPlayerShots().Select(shot => shot.Position));
        Assert.Equal(action, state.Turns[^1].Action);
        Assert.Equal(anchor, state.Turns[^1].Target);
        Assert.All(state.OpponentGrid.Where(cell => expected.Contains(new Position(cell.Row, cell.Column))),
            cell => Assert.True(IsShot(cell)));
        Assert.DoesNotContain(state.OpponentGrid, cell => cell.HasMine || cell.State is CellState.Ship or CellState.Water);
    }

    [Theory]
    [InlineData(GameAction.SquareStrike, 9, 0)]
    [InlineData(GameAction.SquareStrike, 0, 9)]
    [InlineData(GameAction.SquareStrike, 9, 9)]
    [InlineData(GameAction.RowStrike, -1, 0)]
    [InlineData(GameAction.ColumnStrike, 0, 10)]
    [InlineData(GameAction.Mine, 10, 0)]
    public void Invalid_geometry_never_spends_points_or_advances_a_turn(GameAction action, int row, int column)
    {
        var scenario = CreateScenario(42);
        Charge(scenario, 10);
        Assert.Empty(PowerRules.Targets(action, new Position(row, column)));
        Unchanged(scenario.Game, () => scenario.Game.UsePower(action, new Position(row, column)), GameError.InvalidPosition);
    }

    [Theory]
    [InlineData(GameAction.NormalShot)]
    [InlineData((GameAction)99)]
    public void Power_endpoint_cannot_be_used_as_a_normal_or_unknown_action(GameAction action)
    {
        var game = new Game("Alice", new Random(42));
        Unchanged(game, () => game.UsePower(action, new Position(0, 0)), GameError.InvalidPower);
    }

    [Fact]
    public void Square_skips_previous_shots_but_an_empty_zone_is_rejected()
    {
        var scenario = CreateScenario(42);
        var anchor = new Position(3, 3);
        var targets = PowerRules.Targets(GameAction.SquareStrike, anchor);
        Charge(scenario, 9, targets);
        scenario.Game.Fire(anchor);
        var state = scenario.Game.UsePower(GameAction.SquareStrike, anchor);
        Assert.Equal(3, state.Turns[^1].GetPlayerShots().Count);
        Assert.DoesNotContain(state.Turns[^1].GetPlayerShots(), shot => shot.Position == anchor);
        Assert.Equal(6, state.SkillPoints);
        Unchanged(scenario.Game, () => scenario.Game.UsePower(GameAction.SquareStrike, anchor), GameError.NoNewTargets);
    }

    [Fact]
    public void Mine_placement_costs_a_turn_and_is_visible_only_on_the_player_grid()
    {
        var scenario = CreateScenario(42);
        Charge(scenario, 4);
        var mine = scenario.ComputerTargets[8];
        var state = scenario.Game.UsePower(GameAction.Mine, mine);
        Assert.Equal(2, state.SkillPoints);
        Assert.Equal(5, state.TurnNumber);
        Assert.Null(state.LastPlayerShot);
        Assert.NotNull(state.LastComputerShot);
        Assert.Empty(state.Turns[^1].GetPlayerShots());
        Assert.Equal(GameAction.Mine, state.Turns[^1].Action);
        Assert.Equal(mine, state.Turns[^1].Target);
        Assert.True(Cell(state.PlayerGrid, mine).HasMine);
        Assert.DoesNotContain(state.OpponentGrid, cell => cell.HasMine);
        Unchanged(scenario.Game, () => scenario.Game.UsePower(GameAction.Mine, mine), GameError.InvalidMinePlacement);
        Unchanged(scenario.Game, () => scenario.Game.UsePower(GameAction.Mine, scenario.ComputerTargets[0]), GameError.InvalidMinePlacement);
    }

    [Fact]
    public void Mine_reflects_at_the_same_position_without_cancelling_the_computer_shot()
    {
        var scenario = CreateScenario(42);
        var mine = scenario.ComputerTargets[2];
        Charge(scenario, 2, [mine]);
        var state = scenario.Game.UsePower(GameAction.Mine, mine);
        Assert.Equal(0, state.SkillPoints);
        Assert.Equal(mine, state.LastComputerShot!.Position);
        Assert.True(IsShot(Cell(state.PlayerGrid, mine)));
        Assert.False(Cell(state.PlayerGrid, mine).HasMine);
        var explosion = Assert.IsType<MineDetonationDto>(state.Turns[^1].MineDetonation);
        Assert.Equal(mine, explosion.Position);
        Assert.NotNull(explosion.ReflectedShot);
        Assert.Equal(mine, explosion.ReflectedShot.Position);
        Assert.True(IsShot(Cell(state.OpponentGrid, mine)));
        Assert.Equal(3, state.OpponentGrid.Count(IsShot));
    }

    [Fact]
    public void Mine_is_consumed_even_if_the_reflected_cell_was_already_fired_at()
    {
        var scenario = CreateScenario(42);
        var mine = scenario.ComputerTargets[2];
        scenario.Game.Fire(mine);
        Charge(scenario, 1, [mine]);
        var state = scenario.Game.UsePower(GameAction.Mine, mine);
        Assert.NotNull(state.Turns[^1].MineDetonation);
        Assert.Null(state.Turns[^1].MineDetonation!.ReflectedShot);
        Assert.Equal(2, state.OpponentGrid.Count(IsShot));
        Assert.False(Cell(state.PlayerGrid, mine).HasMine);
        Assert.Equal(0, state.SkillPoints);
    }

    [Fact]
    public void Mine_can_win_the_game()
    {
        var scenario = Enumerable.Range(0, 200).Select(CreateScenario).First(candidate =>
            candidate.Opponent.Ships.SelectMany(ship => ship.Positions).Contains(candidate.ComputerTargets[16])
            && candidate.Player.Ships.SelectMany(ship => ship.Positions).Any(position => Array.IndexOf(candidate.ComputerTargets, position) > 16));
        var mine = scenario.ComputerTargets[16];
        foreach (var position in scenario.Opponent.Ships.SelectMany(ship => ship.Positions).Where(position => position != mine))
            scenario.Game.Fire(position);
        var state = scenario.Game.UsePower(GameAction.Mine, mine);
        Assert.Equal(GameStatus.PlayerWon, state.Status);
        Assert.NotNull(state.LastComputerShot);
        Assert.Equal(ShotOutcome.Sunk, state.Turns[^1].MineDetonation!.ReflectedShot!.Outcome);
    }

    [Fact]
    public void Simultaneous_destruction_by_a_mine_is_a_draw()
    {
        var scenario = Enumerable.Range(0, 200).Select(CreateScenario).First(candidate =>
        {
            var lastOwnCell = candidate.Player.Ships.SelectMany(ship => ship.Positions)
                .MaxBy(position => Array.IndexOf(candidate.ComputerTargets, position));
            return candidate.Opponent.Ships.SelectMany(ship => ship.Positions).Contains(lastOwnCell);
        });
        var mine = scenario.Player.Ships.SelectMany(ship => ship.Positions)
            .MaxBy(position => Array.IndexOf(scenario.ComputerTargets, position));
        var turnsBeforeMine = Array.IndexOf(scenario.ComputerTargets, mine);
        var opponentShips = scenario.Opponent.Ships.SelectMany(ship => ship.Positions).ToHashSet();
        foreach (var position in scenario.Opponent.AvailableTargets().Where(position => position != mine)
            .OrderByDescending(opponentShips.Contains).Take(turnsBeforeMine))
            scenario.Game.Fire(position);
        var state = scenario.Game.UsePower(GameAction.Mine, mine);
        Assert.Equal(GameStatus.Draw, state.Status);
        Assert.Equal(17, state.PlayerGrid.Count(cell => cell.State == CellState.Sunk));
        Assert.Equal(17, state.OpponentGrid.Count(cell => cell.State == CellState.Sunk));
        Unchanged(scenario.Game, () => scenario.Game.UsePower(GameAction.RowStrike, new Position(0, 0)), GameError.GameFinished);
    }

    [Fact]
    public async Task Concurrent_powers_do_not_overspend_or_duplicate_the_turn()
    {
        var scenario = CreateScenario(42);
        var position = new Position(2, 2);
        Charge(scenario, 4, PowerRules.Targets(GameAction.SquareStrike, position));
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
        {
            try { scenario.Game.UsePower(GameAction.SquareStrike, position); return true; }
            catch (GameRuleException) { return false; }
        })));
        Assert.Single(results, success => success);
        Assert.Equal(0, scenario.Game.GetState().SkillPoints);
        Assert.Equal(5, scenario.Game.GetState().TurnNumber);
    }

    [Fact]
    public void Area_shot_arrays_are_detached_from_the_engine()
    {
        var scenario = CreateScenario(42);
        Charge(scenario, 4, PowerRules.Targets(GameAction.SquareStrike, new Position(2, 2)));
        var state = scenario.Game.UsePower(GameAction.SquareStrike, new Position(2, 2));
        var before = JsonSerializer.Serialize(scenario.Game.GetState());
        state.Turns[^1].PlayerShots![0] = new ShotDto(new Position(9, 9), ShotOutcome.Sunk);
        Assert.Equal(before, JsonSerializer.Serialize(scenario.Game.GetState()));
    }

    [Fact]
    public void Preview_geometry_matches_the_bottom_right_valid_square_and_lines()
    {
        Assert.Equal(new[] { new Position(8, 8), new Position(8, 9), new Position(9, 8), new Position(9, 9) },
            PowerRules.Targets(GameAction.SquareStrike, new Position(8, 8)));
        Assert.All(PowerRules.Targets(GameAction.RowStrike, new Position(9, 4)), position => Assert.Equal(9, position.Row));
        Assert.All(PowerRules.Targets(GameAction.ColumnStrike, new Position(4, 9)), position => Assert.Equal(9, position.Column));
    }

    private static void Unchanged(Game game, Action action, GameError expected)
    {
        var before = JsonSerializer.Serialize(game.GetState());
        Assert.Equal(expected, Assert.Throws<GameRuleException>(action).Error);
        Assert.Equal(before, JsonSerializer.Serialize(game.GetState()));
    }

    private static bool IsShot(CellDto cell) => cell.State is CellState.Miss or CellState.Hit or CellState.Sunk;
    private static CellDto Cell(CellDto[] grid, Position position) => grid[position.Row * Board.Size + position.Column];

    private static void Charge(Scenario scenario, int count, Position[]? exclude = null)
    {
        var occupied = scenario.Opponent.Ships.SelectMany(ship => ship.Positions).ToHashSet();
        var known = scenario.Game.GetState().OpponentGrid.Where(IsShot).Select(cell => new Position(cell.Row, cell.Column)).ToHashSet();
        foreach (var position in scenario.Opponent.AvailableTargets()
            .Where(position => !occupied.Contains(position) && !known.Contains(position) && !(exclude ?? []).Contains(position)).Take(count))
            scenario.Game.Fire(position);
    }

    private static Scenario CreateScenario(int seed)
    {
        var random = new Random(seed);
        var player = Board.CreateRandom(random);
        var opponent = Board.CreateRandom(random);
        var targets = player.AvailableTargets();
        random.Shuffle(targets);
        return new Scenario(new Game("Alice", new Random(seed)), player, opponent, targets);
    }

    private sealed record Scenario(Game Game, Board Player, Board Opponent, Position[] ComputerTargets);
}
