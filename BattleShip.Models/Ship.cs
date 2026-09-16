namespace BattleShip.Models;

public sealed class Ship
{
    internal Ship(ShipKind kind, IEnumerable<Position> positions)
    {
        Kind = kind;
        Positions = Array.AsReadOnly(positions.ToArray());
    }

    public ShipKind Kind { get; }
    public IReadOnlyList<Position> Positions { get; }
}
