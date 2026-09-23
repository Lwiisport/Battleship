using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BattleShip.Models.Contracts;
using BattleShip.Models.Engine;
using BattleShip.Models.Enums;

namespace BattleShip.App.Features.Games.Clients;

public sealed class GameHttpClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<GameStateDto> CreateAsync(string playerName, Difficulty difficulty, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("api/games",
            new CreateGameRequest(playerName, difficulty), JsonOptions, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    public async Task<GameStateDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync($"api/games/{id}", cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    public async Task<GameStateDto> FireAsync(Guid id, Position position, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync($"api/games/{id}/fire",
            new FireRequest(position.Row, position.Column), cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    public async Task<GameStateDto> UsePowerAsync(Guid id, GameAction action, Position position, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync($"api/games/{id}/powers",
            new UsePowerRequest(action, position.Row, position.Column), JsonOptions, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    private static async Task<GameStateDto> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return await response.Content.ReadFromJsonAsync<GameStateDto>(JsonOptions, cancellationToken)
                ?? throw new JsonException("La réponse du serveur est vide.");

        var message = response.StatusCode switch
        {
            HttpStatusCode.NotFound => "La partie n’existe plus. Vous pouvez en créer une nouvelle.",
            HttpStatusCode.Conflict => "L’action a été refusée. Actualisez la partie avant de continuer.",
            HttpStatusCode.TooManyRequests => "Trop de requêtes. Patientez une minute avant de réessayer.",
            HttpStatusCode.ServiceUnavailable => "Le serveur est occupé. Réessayez plus tard.",
            _ => "Le serveur a refusé la requête."
        };
        if (response.Content.Headers.ContentType?.MediaType is "application/problem+json" or "application/json")
        {
            var problem = await response.Content.ReadFromJsonAsync<ApiProblem>(JsonOptions, cancellationToken);
            message = problem?.Errors is { Count: > 0 } errors
                ? string.Join(" ", errors.Values.SelectMany(value => value))
                : problem?.Detail ?? message;
        }
        throw new GameApiException(response.StatusCode, message);
    }

    private sealed record ApiProblem(string? Detail, Dictionary<string, string[]>? Errors);
}

public sealed class GameApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
