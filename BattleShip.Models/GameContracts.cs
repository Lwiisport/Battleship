namespace BattleShip.Models;

public readonly record struct Position(int Row, int Column)
{
    public bool IsValid => Row is >= 0 and < Board.Size && Column is >= 0 and < Board.Size;
}

public enum ShipKind { AircraftCarrier, Cruiser, Destroyer, Submarine, PatrolBoat }
public enum CellState { Unknown, Water, Ship, Miss, Hit, Sunk }
public enum ShotOutcome { Miss, Hit, Sunk }
public enum GameStatus { InProgress, PlayerWon, ComputerWon }
public enum GameError { InvalidPosition, DuplicateShot, GameFinished }

public sealed record ShipSpecification(ShipKind Kind, int Size);
public sealed record CreateGameRequest(string PlayerName);
public sealed record FireRequest(int Row, int Column);
public sealed record CellDto(int Row, int Column, CellState State);
public sealed record ShotDto(Position Position, ShotOutcome Outcome);
public sealed record TurnDto(int Number, ShotDto PlayerShot, ShotDto? ComputerShot);
public sealed record GameStateDto(
    Guid Id,
    string PlayerName,
    GameStatus Status,
    int TurnNumber,
    CellDto[] PlayerGrid,
    CellDto[] OpponentGrid,
    ShotDto? LastPlayerShot,
    ShotDto? LastComputerShot,
    DateTimeOffset CreatedAtUtc,
    TurnDto[] Turns);

public sealed class GameRuleException(GameError error, string message) : Exception(message)
{
    public GameError Error { get; } = error;
}
