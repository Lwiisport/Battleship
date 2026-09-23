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
    private readonly List<Ship> ships;
    private readonly Dictionary<Position, Ship> occupancy;

    private Board(List<Ship> ships)
    {
        this.ships = ships;
        occupancy = ships.SelectMany(ship => ship.Positions.Select(position => (position, ship)))
            .ToDictionary(entry => entry.position, entry => entry.ship);
    }

    public IReadOnlyList<Ship> Ships => ships;
    public int ShotCount => shots.Count;
    public bool HasBeenShot(Position position) => shots.Contains(position);
    public bool AllShipsSunk => occupancy.Count > 0 && occupancy.Keys.All(shots.Contains);
    public bool IsFleetComplete => ShipsToPlace.Count == 0;

    public IReadOnlyList<ShipSpecification> ShipsToPlace => Fleet
        .Where(specification => ships.All(ship => ship.Kind != specification.Kind))
        .ToArray();

    public static Board CreateEmpty() => new([]);

    public static Board CreateRandom(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var board = CreateEmpty();
        board.Randomize(random);
        return board;
    }

    public static Position[] ShipFootprint(ShipKind kind, Position start, bool vertical)
    {
        var specification = Fleet.FirstOrDefault(spec => spec.Kind == kind);
        if (specification is null)
            return [];
        return Enumerable.Range(0, specification.Size)
            .Select(offset => new Position(start.Row + (vertical ? offset : 0),
                start.Column + (vertical ? 0 : offset)))
            .ToArray();
    }

    public void PlaceShip(ShipKind kind, Position start, bool vertical)
    {
        var positions = ShipFootprint(kind, start, vertical);
        if (positions.Length == 0)
            throw new GameRuleException(GameError.InvalidShipPlacement, "Ce navire n’existe pas.");
        if (positions.Any(position => !position.IsValid))
            throw new GameRuleException(GameError.InvalidShipPlacement, "Le navire doit rester entièrement dans la grille de 10 × 10.");
        var existing = ships.FirstOrDefault(ship => ship.Kind == kind);
        if (positions.Any(position => occupancy.TryGetValue(position, out var other) && other != existing))
            throw new GameRuleException(GameError.InvalidShipPlacement, "Le navire chevauche un autre navire.");
        if (existing is not null)
            Remove(existing);
        var ship = new Ship(kind, positions);
        ships.Add(ship);
        foreach (var position in positions)
            occupancy.Add(position, ship);
    }

    public void RemoveShip(ShipKind kind)
    {
        var ship = ships.FirstOrDefault(candidate => candidate.Kind == kind)
            ?? throw new GameRuleException(GameError.ShipNotPlaced, "Ce navire n’est pas encore posé.");
        Remove(ship);
    }

    public void Randomize(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        foreach (var specification in ShipsToPlace)
        {
            List<Position[]> candidates = [];
            for (var row = 0; row < Size; row++)
            for (var column = 0; column < Size; column++)
            for (var orientation = 0; orientation < 2; orientation++)
            {
                var positions = ShipFootprint(specification.Kind,
                    new Position(row, column), vertical: orientation == 1);
                if (positions.Length > 0 && positions.All(position => position.IsValid && !occupancy.ContainsKey(position)))
                    candidates.Add(positions);
            }

            var placement = candidates[random.Next(candidates.Count)];
            var ship = new Ship(specification.Kind, placement);
            ships.Add(ship);
            foreach (var position in placement)
                occupancy.Add(position, ship);
        }
    }

    private void Remove(Ship ship)
    {
        ships.Remove(ship);
        foreach (var position in ship.Positions)
            occupancy.Remove(position);
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
