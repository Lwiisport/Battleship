using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BattleShip.Models.Contracts;
using BattleShip.Models.Enums;
using BattleShip.Tests.Infrastructure;

namespace BattleShip.Tests.Integration.Http;

public sealed class HttpPowerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData(GameAction.Mine, 2, 0)]
    [InlineData(GameAction.SquareStrike, 4, 4)]
    [InlineData(GameAction.RowStrike, 6, 10)]
    [InlineData(GameAction.ColumnStrike, 6, 10)]
    public async Task Powers_spend_points_and_record_a_complete_turn(GameAction action, int cost, int affected)
    {
        using var client = factory.CreateClient();
        using var created = await client.PostAsJsonAsync("/api/games", new CreateGameRequest("Alice"));
        var state = await Read(created);
        for (var index = 0; index < cost; index++)
        {
            using var fired = await client.PostAsJsonAsync($"/api/games/{state.Id}/fire", new FireRequest(0, index));
            state = await Read(fired);
        }
        var target = action == GameAction.Mine ? state.PlayerGrid.Last(cell => cell.State is CellState.Water or CellState.Ship)
            : new CellDto(2, 8, CellState.Unknown);
        using var response = await client.PostAsJsonAsync($"/api/games/{state.Id}/powers",
            new UsePowerRequest(action, target.Row, target.Column), ApiFactory.JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await Read(response);
        Assert.Equal(0, result.SkillPoints);
        Assert.Equal(cost + 1, result.TurnNumber);
        Assert.Equal(action, result.Turns[^1].Action);
        Assert.Equal(affected, result.Turns[^1].GetPlayerShots().Count);
        Assert.NotNull(result.LastComputerShot);
        Assert.DoesNotContain(result.OpponentGrid, cell => cell.HasMine || cell.State is CellState.Ship or CellState.Water);
    }

    [Fact]
    public async Task Insufficient_points_return_conflict_without_mutation()
    {
        using var client = factory.CreateClient();
        using var created = await client.PostAsJsonAsync("/api/games", new CreateGameRequest("Alice"));
        var initial = await Read(created);
        using var response = await client.PostAsJsonAsync($"/api/games/{initial.Id}/powers", new UsePowerRequest(GameAction.Mine, 0, 0));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("InsufficientSkillPoints", problem.RootElement.GetProperty("code").GetString());
        using var fetched = await client.GetAsync($"/api/games/{initial.Id}");
        Assert.Equal(JsonSerializer.Serialize(initial), JsonSerializer.Serialize(await Read(fetched)));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"row\":0,\"column\":0}")]
    [InlineData("{\"action\":\"Mine\",\"row\":0}")]
    [InlineData("{\"action\":\"Mine\",\"column\":0}")]
    [InlineData("{\"action\":\"NormalShot\",\"row\":0,\"column\":0}")]
    [InlineData("{\"action\":999,\"row\":0,\"column\":0}")]
    [InlineData("{\"action\":\"SquareStrike\",\"row\":9,\"column\":0}")]
    [InlineData("{\"action\":\"ColumnStrike\",\"row\":0,\"column\":10}")]
    public async Task Invalid_power_payloads_return_bad_request(string json)
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsync($"/api/games/{Guid.NewGuid()}/powers", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_game_returns_not_found()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync($"/api/games/{Guid.NewGuid()}/powers", new UsePowerRequest(GameAction.Mine, 0, 0));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<GameStateDto> Read(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GameStateDto>(ApiFactory.JsonOptions)
            ?? throw new InvalidOperationException("Réponse vide.");
    }
}
