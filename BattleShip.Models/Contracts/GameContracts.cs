using BattleShip.Models.Engine;
using BattleShip.Models.Enums;

namespace BattleShip.Models.Contracts;

public sealed record CreateGameRequest(string PlayerName, Difficulty Difficulty = Difficulty.Easy,
    bool ManualPlacement = false);
public sealed record FireRequest(int Row, int Column);
public sealed record UsePowerRequest(GameAction Action, int Row, int Column);
public sealed record PlaceShipRequest(ShipKind Ship, int Row, int Column, bool Vertical = false);
public sealed record RemoveShipRequest(ShipKind Ship);
public sealed record CellDto(int Row, int Column, CellState State, bool HasMine = false);
public sealed record ShotDto(Position Position, ShotOutcome Outcome);
public sealed record MineDetonationDto(Position Position, ShotDto? ReflectedShot);
public sealed record TurnDto(
    int Number,
    ShotDto? PlayerShot,
    ShotDto? ComputerShot,
    GameAction Action = GameAction.NormalShot,
    Position? Target = null,
    ShotDto[]? PlayerShots = null,
    MineDetonationDto? MineDetonation = null,
    int SkillPointsAfter = 0)
{
    public IReadOnlyList<ShotDto> GetPlayerShots() => PlayerShots ?? (PlayerShot is { } shot ? [shot] : []);
}
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
    TurnDto[] Turns,
    int SkillPoints = 0,
    Difficulty Difficulty = Difficulty.Easy,
    ShipSpecification[]? ShipsToPlace = null);
