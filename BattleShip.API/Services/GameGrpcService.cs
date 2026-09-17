using BattleShip.Grpc;
using BattleShip.Models;
using FluentValidation;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace BattleShip.API.Services;

public sealed class GameGrpcService(GameStore store, IValidator<GetGameStatusRequest> validator)
    : GameService.GameServiceBase
{
    public override async Task<GameStatusReply> GetGameStatus(GetGameStatusRequest request, ServerCallContext context)
    {
        var validation = await validator.ValidateAsync(request, context.CancellationToken);
        if (!validation.IsValid)
            throw new RpcException(new Status(StatusCode.InvalidArgument, validation.Errors[0].ErrorMessage));
        var game = store.Find(Guid.Parse(request.GameId));
        if (game is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Cette partie n’existe pas ou a expiré."));
        var state = game.GetState();
        var reply = new GameStatusReply
        {
            GameId = state.Id.ToString(),
            PlayerName = state.PlayerName,
            TurnNumber = state.TurnNumber,
            CreatedAtUtc = Timestamp.FromDateTimeOffset(state.CreatedAtUtc),
            Status = state.Status switch
            {
                GameStatus.InProgress => GamePhase.InProgress,
                GameStatus.PlayerWon => GamePhase.PlayerWon,
                GameStatus.ComputerWon => GamePhase.ComputerWon,
                _ => throw new InvalidOperationException("État de partie inconnu.")
            }
        };
        reply.PlayerGrid.AddRange(state.PlayerGrid.Select(MapCell));
        reply.OpponentGrid.AddRange(state.OpponentGrid.Select(MapCell));
        if (state.LastPlayerShot is { } playerShot)
            reply.LastPlayerShot = MapShot(playerShot);
        if (state.LastComputerShot is { } computerShot)
            reply.LastComputerShot = MapShot(computerShot);
        foreach (var turn in state.Turns)
        {
            var message = new TurnMessage { Number = turn.Number, PlayerShot = MapShot(turn.PlayerShot) };
            if (turn.ComputerShot is { } shot)
                message.ComputerShot = MapShot(shot);
            reply.Turns.Add(message);
        }
        return reply;
    }

    private static CellMessage MapCell(CellDto cell) => new()
    {
        Row = cell.Row,
        Column = cell.Column,
        State = cell.State switch
        {
            CellState.Unknown => CellKind.Unknown,
            CellState.Water => CellKind.Water,
            CellState.Ship => CellKind.Ship,
            CellState.Miss => CellKind.Miss,
            CellState.Hit => CellKind.Hit,
            CellState.Sunk => CellKind.Sunk,
            _ => throw new InvalidOperationException("État de case inconnu.")
        }
    };

    private static ShotMessage MapShot(ShotDto shot) => new()
    {
        Row = shot.Position.Row,
        Column = shot.Position.Column,
        Outcome = shot.Outcome switch
        {
            ShotOutcome.Miss => ImpactKind.Miss,
            ShotOutcome.Hit => ImpactKind.Hit,
            ShotOutcome.Sunk => ImpactKind.Sunk,
            _ => throw new InvalidOperationException("Résultat de tir inconnu.")
        }
    };
}
