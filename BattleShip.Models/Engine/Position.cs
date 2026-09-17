namespace BattleShip.Models.Engine;

public readonly record struct Position(int Row, int Column)
{
    public bool IsValid => Row is >= 0 and < Board.Size && Column is >= 0 and < Board.Size;
}
