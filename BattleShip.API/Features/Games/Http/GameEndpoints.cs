using BattleShip.API.Features.Games.Storage;
using BattleShip.Models.Contracts;
using BattleShip.Models.Engine;
using BattleShip.Models.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace BattleShip.API.Features.Games.Http;

public static class GameEndpoints
{
    public static void MapGameEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/games").WithTags("Games");
        group.MapPost("/", Create).WithName("CreateGame");
        group.MapGet("/{id:guid}", Get).WithName("GetGame");
        group.MapPost("/{id:guid}/fire", Fire).WithName("Fire");
        group.MapPost("/{id:guid}/powers", UsePower).WithName("UsePower");
    }

    private static async Task<Results<Created<GameStateDto>, ValidationProblem, ProblemHttpResult>> Create(
        CreateGameRequest request, IValidator<CreateGameRequest> validator, GameStore store,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return TypedResults.ValidationProblem(validation.ToDictionary());
        if (!store.TryCreate(request.PlayerName, request.Difficulty, out var game))
            return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Serveur occupé", detail: "Le nombre maximal de parties actives est atteint.");
        return TypedResults.Created($"/api/games/{game.Id}", game.GetState());
    }

    private static Results<Ok<GameStateDto>, NotFound<ProblemDetails>> Get(Guid id, GameStore store)
    {
        var game = store.Find(id);
        return game is null ? MissingGame() : TypedResults.Ok(game.GetState());
    }

    private static async Task<Results<Ok<GameStateDto>, ValidationProblem, NotFound<ProblemDetails>, Conflict<ProblemDetails>>> Fire(
        Guid id, FireRequest request, IValidator<FireRequest> validator, GameStore store,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        return validation.IsValid ? Execute(id, store, game => game.Fire(new Position(request.Row, request.Column)))
            : TypedResults.ValidationProblem(validation.ToDictionary());
    }

    private static async Task<Results<Ok<GameStateDto>, ValidationProblem, NotFound<ProblemDetails>, Conflict<ProblemDetails>>> UsePower(
        Guid id, UsePowerRequest request, IValidator<UsePowerRequest> validator, GameStore store,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        return validation.IsValid ? Execute(id, store, game => game.UsePower(request.Action, new Position(request.Row, request.Column)))
            : TypedResults.ValidationProblem(validation.ToDictionary());
    }

    private static Results<Ok<GameStateDto>, ValidationProblem, NotFound<ProblemDetails>, Conflict<ProblemDetails>> Execute(
        Guid id, GameStore store, Func<Game, GameStateDto> action)
    {
        var game = store.Find(id);
        if (game is null)
            return MissingGame();
        try
        {
            return TypedResults.Ok(action(game));
        }
        catch (GameRuleException exception)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Action refusée",
                Detail = exception.Message,
                Extensions = { ["code"] = exception.Error.ToString() }
            });
        }
    }

    private static NotFound<ProblemDetails> MissingGame() => TypedResults.NotFound(new ProblemDetails
    {
        Status = StatusCodes.Status404NotFound,
        Title = "Partie introuvable",
        Detail = "Cette partie n’existe pas ou a expiré."
    });
}
