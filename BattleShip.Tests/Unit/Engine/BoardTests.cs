using BattleShip.Models.Engine;
using BattleShip.Models.Enums;
using BattleShip.Models.Exceptions;

namespace BattleShip.Tests.Unit.Engine;

public sealed class BoardTests
{
    [Fact]
    public void Random_fleets_have_correct_sizes_and_no_overlap_or_overflow()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var board = Board.CreateRandom(new Random(seed));
            Assert.Equal(5, board.Ships.Count);
            Assert.Equal(17, board.Ships.SelectMany(ship => ship.Positions).Distinct().Count());
            foreach (var specification in Board.Fleet)
            {
                var ship = Assert.Single(board.Ships, ship => ship.Kind == specification.Kind);
                Assert.Equal(specification.Size, ship.Positions.Count);
                Assert.All(ship.Positions, position => Assert.True(position.IsValid));
                var horizontal = ship.Positions.Select(position => position.Row).Distinct().Count() == 1;
                var vertical = ship.Positions.Select(position => position.Column).Distinct().Count() == 1;
                Assert.True(horizontal || vertical);
                var axis = ship.Positions.Select(position => horizontal ? position.Column : position.Row).Order().ToArray();
                Assert.Equal(Enumerable.Range(axis[0], specification.Size), axis);
            }
        }
    }

    [Fact]
    public void Hidden_grid_only_reveals_shots_and_sunk_cells()
    {
        var board = Board.CreateRandom(new Random(42));
        Assert.All(board.ToGrid(false), cell => Assert.Equal(CellState.Unknown, cell.State));
        var ship = board.Ships[0];
        foreach (var position in ship.Positions)
            board.Fire(position);
        var grid = board.ToGrid(false);
        Assert.Equal(ship.Positions.Count, grid.Count(cell => cell.State == CellState.Sunk));
        Assert.Equal(100 - ship.Positions.Count, grid.Count(cell => cell.State == CellState.Unknown));
        Assert.DoesNotContain(grid, cell => cell.State is CellState.Ship or CellState.Water);
    }

    [Fact]
    public void Shots_distinguish_miss_hit_and_sunk()
    {
        var board = Board.CreateRandom(new Random(42));
        var ship = board.Ships.Single(ship => ship.Kind == ShipKind.PatrolBoat);
        var occupied = board.Ships.SelectMany(value => value.Positions).ToHashSet();
        var water = board.AvailableTargets().First(position => !occupied.Contains(position));
        Assert.Equal(ShotOutcome.Miss, board.Fire(water).Outcome);
        Assert.Equal(ShotOutcome.Hit, board.Fire(ship.Positions[0]).Outcome);
        Assert.Equal(ShotOutcome.Sunk, board.Fire(ship.Positions[1]).Outcome);
        Assert.DoesNotContain(water, board.AvailableTargets());
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(10, 0)]
    [InlineData(0, 10)]
    [InlineData(int.MaxValue, int.MinValue)]
    public void Invalid_shots_leave_board_unchanged(int row, int column)
    {
        var board = Board.CreateRandom(new Random(42));
        var before = board.ToGrid(true);
        var exception = Assert.Throws<GameRuleException>(() => board.Fire(new Position(row, column)));
        Assert.Equal(GameError.InvalidPosition, exception.Error);
        Assert.Equal(before, board.ToGrid(true));
        Assert.Equal(0, board.ShotCount);
    }

    [Fact]
    public void Replayed_misses_and_hits_are_rejected_without_mutation()
    {
        var board = Board.CreateRandom(new Random(42));
        var positions = new[]
        {
            board.Ships[0].Positions[0],
            board.ToGrid(true).Where(cell => cell.State == CellState.Water)
                .Select(cell => new Position(cell.Row, cell.Column)).First()
        };
        foreach (var position in positions)
        {
            board.Fire(position);
            var before = board.ToGrid(true);
            var count = board.ShotCount;
            var exception = Assert.Throws<GameRuleException>(() => board.Fire(position));
            Assert.Equal(GameError.DuplicateShot, exception.Error);
            Assert.Equal(before, board.ToGrid(true));
            Assert.Equal(count, board.ShotCount);
        }
    }
}
