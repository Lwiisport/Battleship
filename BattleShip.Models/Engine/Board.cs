using BattleShip.Models.Contracts;
using BattleShip.Models.Enums;
using BattleShip.Models.Exceptions;

namespace BattleShip.Models.Engine;

public sealed class Board
{
    public const int Size = 10;

    public static IReadOnlyList<ShipSpecification> Fleet { get; } = Array.AsReadOnly<ShipSpecification>(
    [
        new(ShipKind.AircraftCarrier, 5),
        new(ShipKind.Cruiser, 4),
        new(ShipKind.Destroyer, 3),
        new(ShipKind.Submarine, 3),
        new(ShipKind.PatrolBoat, 2)
    ]);

    private readonly HashSet<Position> shots = [];
    private readonly Dictionary<Position, Ship> occupancy;

    private Board(List<Ship> ships)
    {
        Ships = ships.AsReadOnly();
        occupancy = ships.SelectMany(ship => ship.Positions.Select(position => (position, ship)))
            .ToDictionary(entry => entry.position, entry => entry.ship);
    }

    public IReadOnlyList<Ship> Ships { get; }
    public int ShotCount => shots.Count;
    public bool HasBeenShot(Position position) => shots.Contains(position);
    public bool AllShipsSunk => occupancy.Keys.All(shots.Contains);

    public static Board CreateRandom(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        List<Ship> ships = [];
        HashSet<Position> occupied = [];
        foreach (var specification in Fleet)
        {
            List<Position[]> candidates = [];
            for (var row = 0; row < Size; row++)
            for (var column = 0; column < Size; column++)
            for (var orientation = 0; orientation < 2; orientation++)
            {
                var positions = Enumerable.Range(0, specification.Size)
                    .Select(offset => new Position(row + (orientation == 0 ? offset : 0),
                        column + (orientation == 1 ? offset : 0)))
                    .ToArray();
                if (positions.All(position => position.IsValid && !occupied.Contains(position)))
                    candidates.Add(positions);
            }

            var placement = candidates[random.Next(candidates.Count)];
            ships.Add(new Ship(specification.Kind, placement));
            occupied.UnionWith(placement);
        }
        return new Board(ships);
    }

    public ShotDto Fire(Position position)
    {
        if (!position.IsValid)
            throw new GameRuleException(GameError.InvalidPosition, "Le tir doit être dans la grille de 10 × 10.");
        if (!shots.Add(position))
            throw new GameRuleException(GameError.DuplicateShot, "Cette case a déjà été visée.");
        if (!occupancy.TryGetValue(position, out var ship))
            return new ShotDto(position, ShotOutcome.Miss);
        return new ShotDto(position, ship.Positions.All(shots.Contains) ? ShotOutcome.Sunk : ShotOutcome.Hit);
    }

    public Position[] AvailableTargets() => Enumerable.Range(0, Size * Size)
        .Select(index => new Position(index / Size, index % Size))
        .Where(position => !shots.Contains(position))
        .ToArray();

    public CellDto[] ToGrid(bool revealShips) => Enumerable.Range(0, Size * Size)
        .Select(index =>
        {
            var position = new Position(index / Size, index % Size);
            var state = revealShips ? CellState.Water : CellState.Unknown;
            if (shots.Contains(position))
            {
                state = !occupancy.TryGetValue(position, out var hitShip) ? CellState.Miss
                    : hitShip.Positions.All(shots.Contains) ? CellState.Sunk : CellState.Hit;
            }
            else if (revealShips && occupancy.ContainsKey(position))
            {
                state = CellState.Ship;
            }
            return new CellDto(position.Row, position.Column, state);
        })
        .ToArray();
}
