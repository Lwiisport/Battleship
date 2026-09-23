using BattleShip.Grpc;
using BattleShip.Models.Contracts;
using BattleShip.Models.Engine;
using BattleShip.Models.Enums;

namespace BattleShip.App.Features.Games.Clients;

public sealed class GameGrpcClient(GameService.GameServiceClient client)
{
    public async Task<GameStateDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var reply = await client.GetGameStatusAsync(new GetGameStatusRequest { GameId = id.ToString() },
            deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: cancellationToken);
        return new GameStateDto(Guid.Parse(reply.GameId), reply.PlayerName, reply.Status switch
        {
            GamePhase.InProgress => GameStatus.InProgress,
            GamePhase.PlayerWon => GameStatus.PlayerWon,
            GamePhase.ComputerWon => GameStatus.ComputerWon,
            GamePhase.Draw => GameStatus.Draw,
            GamePhase.Placing => GameStatus.PlacingShips,
            _ => throw new InvalidOperationException("État de partie inconnu.")
        }, reply.TurnNumber, reply.PlayerGrid.Select(MapCell).ToArray(), reply.OpponentGrid.Select(MapCell).ToArray(),
            MapShot(reply.LastPlayerShot), MapShot(reply.LastComputerShot), reply.CreatedAtUtc.ToDateTimeOffset(),
            reply.Turns.Select(MapTurn).ToArray(), reply.SkillPoints, reply.Difficulty switch
            {
                DifficultyLevel.Easy => Difficulty.Easy,
                DifficultyLevel.Normal => Difficulty.Normal,
                DifficultyLevel.Hard => Difficulty.Hard,
                _ => throw new InvalidOperationException("Difficulté inconnue.")
            }, reply.ShipsToPlace.Select(spec => new ShipSpecification(MapShipKind(spec.Kind), spec.Size)).ToArray());
    }

    private static TurnDto MapTurn(TurnMessage turn) => new(turn.Number, MapShot(turn.PlayerShot), MapShot(turn.ComputerShot),
        turn.Action switch
        {
            ActionKind.NormalShot => GameAction.NormalShot,
            ActionKind.Mine => GameAction.Mine,
            ActionKind.SquareStrike => GameAction.SquareStrike,
            ActionKind.RowStrike => GameAction.RowStrike,
            ActionKind.ColumnStrike => GameAction.ColumnStrike,
            _ => throw new InvalidOperationException("Action inconnue.")
        }, turn.Target is null ? null : new Position(turn.Target.Row, turn.Target.Column),
        turn.PlayerShots.Count == 0 && turn.Action != ActionKind.Mine ? null
            : turn.PlayerShots.Select(shot => MapShot(shot)!).ToArray(),
        turn.MineDetonation is null ? null : new MineDetonationDto(
            new Position(turn.MineDetonation.Position.Row, turn.MineDetonation.Position.Column),
            MapShot(turn.MineDetonation.ReflectedShot)), turn.SkillPointsAfter);

    private static global::BattleShip.Models.Enums.ShipKind MapShipKind(global::BattleShip.Grpc.ShipKind kind) => kind switch
    {
        global::BattleShip.Grpc.ShipKind.AircraftCarrier => global::BattleShip.Models.Enums.ShipKind.AircraftCarrier,
        global::BattleShip.Grpc.ShipKind.Cruiser => global::BattleShip.Models.Enums.ShipKind.Cruiser,
        global::BattleShip.Grpc.ShipKind.Destroyer => global::BattleShip.Models.Enums.ShipKind.Destroyer,
        global::BattleShip.Grpc.ShipKind.Submarine => global::BattleShip.Models.Enums.ShipKind.Submarine,
        global::BattleShip.Grpc.ShipKind.PatrolBoat => global::BattleShip.Models.Enums.ShipKind.PatrolBoat,
        _ => throw new InvalidOperationException("Navire inconnu.")
    };

    private static CellDto MapCell(CellMessage cell) => new(cell.Row, cell.Column, cell.State switch
    {
        CellKind.Unknown => CellState.Unknown,
        CellKind.Water => CellState.Water,
        CellKind.Ship => CellState.Ship,
        CellKind.Miss => CellState.Miss,
        CellKind.Hit => CellState.Hit,
        CellKind.Sunk => CellState.Sunk,
        _ => throw new InvalidOperationException("État de case inconnu.")
    }, cell.HasMine);

    private static ShotDto? MapShot(ShotMessage? shot) => shot is null ? null
        : new ShotDto(new Position(shot.Row, shot.Column), shot.Outcome switch
        {
            ImpactKind.Miss => ShotOutcome.Miss,
            ImpactKind.Hit => ShotOutcome.Hit,
            ImpactKind.Sunk => ShotOutcome.Sunk,
            _ => throw new InvalidOperationException("Résultat de tir inconnu.")
        });
}
