using BattleShip.Grpc;
using BattleShip.Models;

namespace BattleShip.App.Services;

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
            _ => throw new InvalidOperationException("État de partie inconnu.")
        }, reply.TurnNumber, reply.PlayerGrid.Select(MapCell).ToArray(), reply.OpponentGrid.Select(MapCell).ToArray(),
            MapShot(reply.LastPlayerShot), MapShot(reply.LastComputerShot), reply.CreatedAtUtc.ToDateTimeOffset(),
            reply.Turns.Select(turn => new TurnDto(turn.Number,
                MapShot(turn.PlayerShot) ?? throw new InvalidOperationException("Tir joueur manquant."),
                MapShot(turn.ComputerShot))).ToArray());
    }

    private static CellDto MapCell(CellMessage cell) => new(cell.Row, cell.Column, cell.State switch
    {
        CellKind.Unknown => CellState.Unknown,
        CellKind.Water => CellState.Water,
        CellKind.Ship => CellState.Ship,
        CellKind.Miss => CellState.Miss,
        CellKind.Hit => CellState.Hit,
        CellKind.Sunk => CellState.Sunk,
        _ => throw new InvalidOperationException("État de case inconnu.")
    });

    private static ShotDto? MapShot(ShotMessage? shot) => shot is null ? null
        : new ShotDto(new Position(shot.Row, shot.Column), shot.Outcome switch
        {
            ImpactKind.Miss => ShotOutcome.Miss,
            ImpactKind.Hit => ShotOutcome.Hit,
            ImpactKind.Sunk => ShotOutcome.Sunk,
            _ => throw new InvalidOperationException("Résultat de tir inconnu.")
        });
}
