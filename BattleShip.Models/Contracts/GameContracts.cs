using BattleShip.Models.Engine;
using BattleShip.Models.Enums;

namespace BattleShip.Models.Contracts;

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
