using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BattleShip.Models.Contracts;
using BattleShip.Models.Engine;
using BattleShip.Models.Enums;
using BattleShip.Tests.Infrastructure;

namespace BattleShip.Tests.Integration.Http;

public sealed class HttpFleetTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Manual_placement_flow_reaches_in_progress_and_allows_firing()
    {
        using var client = factory.CreateClient();
        using var created = await client.PostAsJsonAsync("/api/games",
            new CreateGameRequest("Alice", Difficulty.Normal, ManualPlacement: true));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var state = await ReadState(created);
        Assert.Equal(GameStatus.PlacingShips, state.Status);
        Assert.Equal(5, state.ShipsToPlace!.Length);
        var route = $"/api/games/{state.Id}";

        using var firedEarly = await client.PostAsJsonAsync(route + "/fire", new FireRequest(0, 0));
        Assert.Equal(HttpStatusCode.Conflict, firedEarly.StatusCode);

        using var placed = await client.PostAsJsonAsync(route + "/fleet/place",
            new PlaceShipRequest(ShipKind.AircraftCarrier, 0, 0));
        state = await ReadState(placed);
        Assert.Equal(GameStatus.PlacingShips, state.Status);
        Assert.Equal(4, state.ShipsToPlace!.Length);
        Assert.Equal(5, state.PlayerGrid.Count(cell => cell.State == CellState.Ship));

        using var random = await client.PostAsync(route + "/fleet/randomize", null);
        state = await ReadState(random);
        Assert.Empty(state.ShipsToPlace!);
        Assert.Equal(17, state.PlayerGrid.Count(cell => cell.State == CellState.Ship));

        using var confirmed = await client.PostAsync(route + "/fleet/confirm", null);
        state = await ReadState(confirmed);
        Assert.Equal(GameStatus.InProgress, state.Status);

        using var locked = await client.PostAsJsonAsync(route + "/fleet/place",
            new PlaceShipRequest(ShipKind.PatrolBoat, 5, 5));
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);

        using var fired = await client.PostAsJsonAsync(route + "/fire", new FireRequest(0, 0));
        state = await ReadState(fired);
        Assert.Equal(1, state.TurnNumber);
    }

    [Fact]
    public async Task Creation_without_the_flag_keeps_the_automatic_fleet()
    {
        using var client = factory.CreateClient();
        using var created = await client.PostAsJsonAsync("/api/games", new CreateGameRequest("Alice"));
        var state = await ReadState(created);
        Assert.Equal(GameStatus.InProgress, state.Status);
        Assert.Empty(state.ShipsToPlace!);
    }

    [Fact]
    public async Task Overlapping_or_out_of_bounds_placements_return_conflict_without_mutation()
    {
        using var client = factory.CreateClient();
        using var created = await client.PostAsJsonAsync("/api/games",
            new CreateGameRequest("Alice", ManualPlacement: true));
        var state = await ReadState(created);
        var route = $"/api/games/{state.Id}/fleet/place";
        using var placed = await client.PostAsJsonAsync(route, new PlaceShipRequest(ShipKind.Cruiser, 0, 0));
        state = await ReadState(placed);

        using var overlap = await client.PostAsJsonAsync(route, new PlaceShipRequest(ShipKind.Destroyer, 0, 1));
        Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);
        using var bounds = await client.PostAsJsonAsync(route, new PlaceShipRequest(ShipKind.Destroyer, 9, 9, Vertical: true));
        Assert.Equal(HttpStatusCode.Conflict, bounds.StatusCode);

        using var fetched = await client.GetAsync($"/api/games/{state.Id}");
        var restored = await ReadState(fetched);
        Assert.Equal(4, restored.ShipsToPlace!.Length);
        Assert.Equal(4, restored.PlayerGrid.Count(cell => cell.State == CellState.Ship));
    }

    [Fact]
    public async Task Remove_returns_the_ship_and_incomplete_confirm_is_rejected()
    {
        using var client = factory.CreateClient();
        using var created = await client.PostAsJsonAsync("/api/games",
            new CreateGameRequest("Alice", ManualPlacement: true));
        var state = await ReadState(created);
        var route = $"/api/games/{state.Id}";

        using var confirm = await client.PostAsync(route + "/fleet/confirm", null);
        Assert.Equal(HttpStatusCode.Conflict, confirm.StatusCode);

        using var placed = await client.PostAsJsonAsync(route + "/fleet/place",
            new PlaceShipRequest(ShipKind.PatrolBoat, 0, 0, Vertical: true));
        (await ReadState(placed)).ShipsToPlace!.ToList().ForEach(spec => Assert.NotEqual(ShipKind.PatrolBoat, spec.Kind));

        using var removed = await client.PostAsJsonAsync(route + "/fleet/remove", new RemoveShipRequest(ShipKind.PatrolBoat));
        state = await ReadState(removed);
        Assert.Contains(state.ShipsToPlace!, spec => spec.Kind == ShipKind.PatrolBoat);
        Assert.All(state.PlayerGrid, cell => Assert.Equal(CellState.Water, cell.State));

        using var missing = await client.PostAsJsonAsync(route + "/fleet/remove", new RemoveShipRequest(ShipKind.PatrolBoat));
        Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"ship\":\"Bateau\",\"row\":0,\"column\":0}")]
    [InlineData("{\"ship\":\"Cruiser\",\"row\":-1,\"column\":0}")]
    [InlineData("{\"ship\":\"Cruiser\",\"row\":0,\"column\":10}")]
    public async Task Invalid_placement_requests_return_bad_request(string body)
    {
        using var client = factory.CreateClient();
        using var created = await client.PostAsJsonAsync("/api/games",
            new CreateGameRequest("Alice", ManualPlacement: true));
        var state = await ReadState(created);
        using var response = await client.PostAsync($"/api/games/{state.Id}/fleet/place",
            new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Fleet_endpoints_return_not_found_for_missing_games()
    {
        using var client = factory.CreateClient();
        var route = $"/api/games/{Guid.NewGuid()}/fleet";
        using var placed = await client.PostAsJsonAsync(route + "/place", new PlaceShipRequest(ShipKind.Cruiser, 0, 0));
        using var removed = await client.PostAsJsonAsync(route + "/remove", new RemoveShipRequest(ShipKind.Cruiser));
        using var random = await client.PostAsync(route + "/randomize", null);
        using var confirm = await client.PostAsync(route + "/confirm", null);
        Assert.Equal(HttpStatusCode.NotFound, placed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, random.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, confirm.StatusCode);
    }

    private static async Task<GameStateDto> ReadState(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var state = await response.Content.ReadFromJsonAsync<GameStateDto>(ApiFactory.JsonOptions);
        Assert.NotNull(state);
        return state;
    }
}
