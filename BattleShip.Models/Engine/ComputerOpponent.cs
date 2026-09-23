using BattleShip.Models.Contracts;
using BattleShip.Models.Enums;

namespace BattleShip.Models.Engine;

public sealed class ComputerOpponent(Difficulty difficulty, Queue<Position> shuffledOrder, Random random)
{
    private const int MistakePercent = 20;
    private const int HitWeight = 100;
    private static readonly (int Row, int Column)[] Directions = [(1, 0), (-1, 0), (0, 1), (0, -1)];
    private static readonly (int Row, int Column)[] Axes = [(1, 0), (0, 1)];

    public Position ChooseTarget(CellDto[] grid) => difficulty switch
    {
        Difficulty.Easy => shuffledOrder.Dequeue(),
        Difficulty.Normal => ChooseNormal(grid),
        _ => ChooseHard(grid),
    };

    private Position ChooseNormal(CellDto[] grid)
    {
        var pursuit = PursuitTargets(grid);
        if (pursuit.Length > 0 && random.Next(100) >= MistakePercent)
            return pursuit[random.Next(pursuit.Length)];
        var unknown = UnknownCells(grid);
        return unknown[random.Next(unknown.Length)];
    }

    private Position ChooseHard(CellDto[] grid)
    {
        var scores = new int[Board.Size * Board.Size];
        foreach (var size in RemainingSizes(grid))
            foreach (var placement in Placements(grid, size))
            {
                var hits = placement.Count(position => grid[Index(position)].State == CellState.Hit);
                var weight = hits == 0 ? 1 : HitWeight * hits * hits;
                foreach (var position in placement)
                    scores[Index(position)] += weight;
            }
        var best = int.MinValue;
        List<Position> candidates = [];
        foreach (var position in UnknownCells(grid))
        {
            var score = scores[Index(position)];
            if (score > best)
            {
                best = score;
                candidates = [position];
            }
            else if (score == best)
            {
                candidates.Add(position);
            }
        }
        return candidates.Count == 0 ? new Position(0, 0) : candidates[random.Next(candidates.Count)];
    }

    private static Position[] PursuitTargets(CellDto[] grid)
    {
        List<Position> line = [];
        List<Position> adjacent = [];
        for (var row = 0; row < Board.Size; row++)
        for (var column = 0; column < Board.Size; column++)
        {
            if (grid[Index(row, column)].State != CellState.Hit)
                continue;
            foreach (var (dr, dc) in Directions)
            {
                var (neighborRow, neighborColumn) = (row + dr, column + dc);
                if (!Inside(neighborRow, neighborColumn))
                    continue;
                var state = grid[Index(neighborRow, neighborColumn)].State;
                if (state == CellState.Unknown)
                    adjacent.Add(new Position(neighborRow, neighborColumn));
                else if (state == CellState.Hit && Inside(neighborRow + dr, neighborColumn + dc)
                    && grid[Index(neighborRow + dr, neighborColumn + dc)].State == CellState.Unknown)
                    line.Add(new Position(neighborRow + dr, neighborColumn + dc));
            }
        }
        return (line.Count > 0 ? line : adjacent).Distinct().ToArray();
    }

    private static IEnumerable<Position[]> Placements(CellDto[] grid, int size)
    {
        for (var row = 0; row < Board.Size; row++)
        for (var column = 0; column < Board.Size; column++)
            foreach (var (dr, dc) in Axes)
            {
                var placement = Enumerable.Range(0, size)
                    .Select(offset => new Position(row + dr * offset, column + dc * offset)).ToArray();
                if (placement.All(position => position.IsValid
                    && grid[Index(position)].State is CellState.Unknown or CellState.Hit))
                    yield return placement;
            }
    }

    private static int[] RemainingSizes(CellDto[] grid)
    {
        var sizes = Board.Fleet.Select(ship => ship.Size).ToList();
        foreach (var length in SunkRuns(grid))
        {
            if (sizes.Remove(length))
                continue;
            // Deux navires coulés accolés forment une seule ligne : retirer les plus grandes tailles possibles.
            for (var rest = length; rest > 0;)
            {
                var fit = sizes.Where(size => size <= rest).DefaultIfEmpty().Max();
                if (fit == 0)
                    break;
                sizes.Remove(fit);
                rest -= fit;
            }
        }
        return sizes.ToArray();
    }

    private static IEnumerable<int> SunkRuns(CellDto[] grid)
    {
        for (var row = 0; row < Board.Size; row++)
        {
            var length = 0;
            for (var column = 0; column <= Board.Size; column++)
            {
                if (column < Board.Size && grid[Index(row, column)].State == CellState.Sunk)
                    length++;
                else if (length > 0)
                {
                    yield return length;
                    length = 0;
                }
            }
        }
        for (var column = 0; column < Board.Size; column++)
        {
            var length = 0;
            for (var row = 0; row <= Board.Size; row++)
            {
                if (row < Board.Size && grid[Index(row, column)].State == CellState.Sunk)
                    length++;
                else if (length > 0)
                {
                    yield return length;
                    length = 0;
                }
            }
        }
    }

    private static Position[] UnknownCells(CellDto[] grid)
    {
        List<Position> cells = [];
        for (var index = 0; index < Board.Size * Board.Size; index++)
            if (grid[index].State == CellState.Unknown)
                cells.Add(new Position(index / Board.Size, index % Board.Size));
        return cells.ToArray();
    }

    private static int Index(Position position) => Index(position.Row, position.Column);
    private static int Index(int row, int column) => row * Board.Size + column;
    private static bool Inside(int row, int column) => new Position(row, column).IsValid;
}
