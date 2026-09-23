using BattleShip.Models.Contracts;
using BattleShip.Models.Engine;
using BattleShip.Models.Enums;
using BattleShip.Models.Exceptions;

namespace BattleShip.Tests.Unit.Engine;

public sealed class DifficultyTests
{
    [Fact]
    public void Easy_dequeues_the_shuffled_order_without_pursuing_hits()
    {
        var order = new Queue<Position>([new Position(3, 4), new Position(1, 1), new Position(9, 9)]);
        var opponent = new ComputerOpponent(Difficulty.Easy, order, new Random(1));
        Assert.Equal(new Position(3, 4), opponent.ChooseTarget(Grid()));
        var grid = Grid((4, 4, CellState.Hit));
        Assert.Equal(new Position(1, 1), opponent.ChooseTarget(grid));
        Assert.Equal(new Position(9, 9), opponent.ChooseTarget(grid));
    }

    [Fact]
    public void Normal_pursues_hit_neighbours_but_still_makes_mistakes()
    {
        var grid = Grid((4, 4, CellState.Hit));
        var neighbours = new HashSet<Position> { new(3, 4), new(5, 4), new(4, 3), new(4, 5) };
        var pursued = 0;
        const int total = 300;
        for (var seed = 0; seed < total; seed++)
        {
            var target = new ComputerOpponent(Difficulty.Normal, new Queue<Position>(), new Random(seed)).ChooseTarget(grid);
            Assert.Equal(CellState.Unknown, grid[target.Row * Board.Size + target.Column].State);
            if (neighbours.Contains(target))
                pursued++;
        }
        Assert.InRange(pursued, (int)(total * 0.65), (int)(total * 0.90));
    }

    [Fact]
    public void Normal_extends_a_hit_line_before_adjacent_cells()
    {
        var grid = Grid((4, 4, CellState.Hit), (4, 5, CellState.Hit));
        var ends = new HashSet<Position> { new(4, 3), new(4, 6) };
        var onLine = 0;
        const int total = 300;
        for (var seed = 0; seed < total; seed++)
        {
            var target = new ComputerOpponent(Difficulty.Normal, new Queue<Position>(), new Random(seed)).ChooseTarget(grid);
            if (ends.Contains(target))
                onLine++;
        }
        Assert.InRange(onLine, (int)(total * 0.65), (int)(total * 0.90));
    }

    [Fact]
    public void Normal_stops_pursuing_once_the_ship_is_sunk()
    {
        var grid = Grid((4, 4, CellState.Sunk), (4, 5, CellState.Sunk), (4, 6, CellState.Sunk));
        for (var seed = 0; seed < 40; seed++)
        {
            var target = new ComputerOpponent(Difficulty.Normal, new Queue<Position>(), new Random(seed)).ChooseTarget(grid);
            Assert.Equal(CellState.Unknown, grid[target.Row * Board.Size + target.Column].State);
        }
    }

    [Fact]
    public void Hard_shoots_beside_an_isolated_hit()
    {
        var grid = Grid((4, 4, CellState.Hit));
        var neighbours = new HashSet<Position> { new(3, 4), new(5, 4), new(4, 3), new(4, 5) };
        for (var seed = 0; seed < 25; seed++)
        {
            var target = new ComputerOpponent(Difficulty.Hard, new Queue<Position>(), new Random(seed)).ChooseTarget(grid);
            Assert.Contains(target, neighbours);
        }
    }

    [Fact]
    public void Hard_extends_collinear_hits_before_anything_else()
    {
        var grid = Grid((4, 4, CellState.Hit), (4, 5, CellState.Hit));
        var ends = new HashSet<Position> { new(4, 3), new(4, 6) };
        for (var seed = 0; seed < 25; seed++)
        {
            var target = new ComputerOpponent(Difficulty.Hard, new Queue<Position>(), new Random(seed)).ChooseTarget(grid);
            Assert.Contains(target, ends);
        }
    }

    [Fact]
    public void Hard_discounts_sunk_ships_and_never_repeats_a_shot()
    {
        var grid = Grid((0, 0, CellState.Sunk), (0, 1, CellState.Sunk), (0, 2, CellState.Sunk), (5, 5, CellState.Hit));
        var neighbours = new HashSet<Position> { new(4, 5), new(6, 5), new(5, 4), new(5, 6) };
        for (var seed = 0; seed < 25; seed++)
        {
            var target = new ComputerOpponent(Difficulty.Hard, new Queue<Position>(), new Random(seed)).ChooseTarget(grid);
            Assert.Contains(target, neighbours);
        }
    }

    [Fact]
    public void Hard_damages_the_player_fleet_faster_than_easy()
    {
        var easyDamage = 0;
        var hardDamage = 0;
        const int measuredTurns = 40;
        for (var seed = 0; seed < 12; seed++)
        {
            easyDamage += DamageAfter(seed, Difficulty.Easy, measuredTurns);
            hardDamage += DamageAfter(seed, Difficulty.Hard, measuredTurns);
        }
        Assert.True(hardDamage > easyDamage, $"difficile={hardDamage} touches, facile={easyDamage} touches après {measuredTurns} tours");
    }

    [Theory]
    [InlineData(Difficulty.Easy)]
    [InlineData(Difficulty.Normal)]
    [InlineData(Difficulty.Hard)]
    public void Game_exposes_the_selected_difficulty(Difficulty difficulty)
        => Assert.Equal(difficulty, new Game("Alice", difficulty, new Random(1)).GetState().Difficulty);

    [Fact]
    public void Legacy_constructor_keeps_the_random_easy_opponent()
        => Assert.Equal(Difficulty.Easy, new Game("Alice", new Random(1)).GetState().Difficulty);

    [Fact]
    public void Unknown_difficulty_is_rejected()
        => Assert.ThrowsAny<ArgumentException>(() => new Game("Alice", (Difficulty)99, new Random(1)));

    private static int DamageAfter(int seed, Difficulty difficulty, int turns)
    {
        var probe = new Random(seed);
        Board.CreateRandom(probe);
        var opponent = Board.CreateRandom(probe);
        var occupied = opponent.Ships.SelectMany(ship => ship.Positions).ToHashSet();
        var game = new Game("Alice", difficulty, new Random(seed));
        var state = game.GetState();
        foreach (var position in opponent.AvailableTargets().OrderBy(occupied.Contains).Take(turns))
        {
            if (state.Status != GameStatus.InProgress)
                break;
            state = game.Fire(position);
        }
        return state.PlayerGrid.Count(cell => cell.State is CellState.Hit or CellState.Sunk);
    }

    private static CellDto[] Grid(params (int Row, int Column, CellState State)[] cells)
    {
        var grid = Enumerable.Range(0, Board.Size * Board.Size)
            .Select(index => new CellDto(index / Board.Size, index % Board.Size, CellState.Unknown)).ToArray();
        foreach (var (row, column, state) in cells)
            grid[row * Board.Size + column] = new CellDto(row, column, state);
        return grid;
    }
}
