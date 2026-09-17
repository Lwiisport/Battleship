using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BattleShip.Models.Contracts;
using BattleShip.Models.Enums;
using BattleShip.Tests.Infrastructure;

namespace BattleShip.Tests.Integration.Http;

public sealed class HttpGameTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Create_get_and_fire_return_masked_snapshots()
    {
        using var client = factory.CreateClient();
        using var created = await client.PostAsJsonAsync("/api/games", new CreateGameRequest("Alice"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var game = await ReadState(created);
        Assert.Equal($"/api/games/{game.Id}", created.Headers.Location?.ToString());
        Assert.All(game.OpponentGrid, cell => Assert.Equal(CellState.Unknown, cell.State));
        using var fetched = await client.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal(JsonSerializer.Serialize(game), JsonSerializer.Serialize(await ReadState(fetched)));
        using var fired = await client.PostAsJsonAsync($"/api/games/{game.Id}/fire", new FireRequest(0, 0));
        Assert.Equal(HttpStatusCode.OK, fired.StatusCode);
        var state = await ReadState(fired);
        Assert.Equal(1, state.TurnNumber);
        Assert.NotNull(state.LastComputerShot);
        Assert.Equal(99, state.OpponentGrid.Count(cell => cell.State == CellState.Unknown));
        Assert.DoesNotContain(state.OpponentGrid, cell => cell.State is CellState.Ship or CellState.Water);
    }

    [Fact]
    public async Task Get_restores_all_turns_with_the_original_creation_date()
    {
        using var client = factory.CreateClient();
        using var created = await client.PostAsJsonAsync("/api/games", new CreateGameRequest("Alice"));
        var initial = await ReadState(created);
        Assert.Empty(initial.Turns);
        var route = $"/api/games/{initial.Id}";
        using var first = await client.PostAsJsonAsync(route + "/fire", new FireRequest(0, 0));
        var firstState = await ReadState(first);
        using var second = await client.PostAsJsonAsync(route + "/fire", new FireRequest(0, 1));
        var secondState = await ReadState(second);
        using var fetched = await client.GetAsync(route);
        var restored = await ReadState(fetched);
        Assert.Equal(initial.CreatedAtUtc, restored.CreatedAtUtc);
        Assert.Equal(2, restored.Turns.Length);
        Assert.Equal(firstState.Turns[0], restored.Turns[0]);
        Assert.Equal(secondState.Turns, restored.Turns);
        Assert.DoesNotContain(restored.OpponentGrid, cell => cell.State is CellState.Ship or CellState.Water);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"playerName\":null}")]
    [InlineData("{\"playerName\":\"   \"}")]
    [InlineData("{\"playerName\":\"abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz\"}")]
    [InlineData("{invalid")]
    public async Task Invalid_creation_returns_bad_request(string body)
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/games", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"row\":0}")]
    [InlineData("{\"column\":0}")]
    [InlineData("{\"row\":-1,\"column\":0}")]
    [InlineData("{\"row\":0,\"column\":10}")]
    [InlineData("{\"row\":\"zero\",\"column\":0}")]
    public async Task Invalid_fire_returns_bad_request_and_does_not_mutate_game(string body)
    {
        using var client = factory.CreateClient();
        using var created = await client.PostAsJsonAsync("/api/games", new CreateGameRequest("Alice"));
        var game = await ReadState(created);
        using var response = await client.PostAsync($"/api/games/{game.Id}/fire",
            new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var fetched = await client.GetAsync($"/api/games/{game.Id}");
        Assert.Equal(JsonSerializer.Serialize(game), JsonSerializer.Serialize(await ReadState(fetched)));
    }

    [Fact]
    public async Task Duplicate_fire_returns_conflict_without_computer_turn()
    {
        using var client = factory.CreateClient();
        using var created = await client.PostAsJsonAsync("/api/games", new CreateGameRequest("Alice"));
        var game = await ReadState(created);
        var route = $"/api/games/{game.Id}/fire";
        using var first = await client.PostAsJsonAsync(route, new FireRequest(0, 0));
        var before = await ReadState(first);
        using var duplicate = await client.PostAsJsonAsync(route, new FireRequest(0, 0));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var fetched = await client.GetAsync($"/api/games/{game.Id}");
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(await ReadState(fetched)));
    }

    [Fact]
    public async Task Missing_games_return_not_found()
    {
        using var client = factory.CreateClient();
        var route = $"/api/games/{Guid.NewGuid()}";
        using var fetched = await client.GetAsync(route);
        using var fired = await client.PostAsJsonAsync(route + "/fire", new FireRequest(0, 0));
        Assert.Equal(HttpStatusCode.NotFound, fetched.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, fired.StatusCode);
    }

    [Theory]
    [InlineData("http://localhost:5221", true)]
    [InlineData("https://localhost:7009", true)]
    [InlineData("https://untrusted.example", false)]
    public async Task Cors_only_allows_configured_origins(string origin, bool allowed)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/games");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");
        using var response = await client.SendAsync(request);
        Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
        if (allowed)
            Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task Openapi_documents_the_game_endpoints()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        Assert.Contains(paths.EnumerateObject(), property => property.Name.TrimEnd('/') == "/api/games");
        Assert.True(paths.TryGetProperty("/api/games/{id}/fire", out _));
    }

    private static async Task<GameStateDto> ReadState(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var state = await response.Content.ReadFromJsonAsync<GameStateDto>(ApiFactory.JsonOptions);
        Assert.NotNull(state);
        return state;
    }
}
