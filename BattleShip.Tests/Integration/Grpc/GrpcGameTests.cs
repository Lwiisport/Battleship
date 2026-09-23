using System.Net;
using System.Net.Http.Json;
using BattleShip.Grpc;
using BattleShip.Models.Contracts;
using BattleShip.Models.Engine;
using BattleShip.Models.Enums;
using BattleShip.Tests.Infrastructure;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;

namespace BattleShip.Tests.Integration.Grpc;

public sealed class GrpcGameTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData(GrpcWebMode.GrpcWeb)]
    [InlineData(GrpcWebMode.GrpcWebText)]
    public async Task Grpc_web_returns_the_same_masked_state_as_http(GrpcWebMode mode)
    {
        using var http = factory.CreateClient();
        using var created = await http.PostAsJsonAsync("/api/games", new CreateGameRequest("Alice", Difficulty.Hard));
        var initial = await created.Content.ReadFromJsonAsync<GameStateDto>(ApiFactory.JsonOptions);
        Assert.NotNull(initial);
        using var channel = CreateChannel(mode);
        var client = new GameService.GameServiceClient(channel);
        var first = await client.GetGameStatusAsync(new GetGameStatusRequest { GameId = initial.Id.ToString() });
        Assert.All(first.OpponentGrid, cell => Assert.Equal(CellKind.Unknown, cell.State));
        Assert.Null(first.LastPlayerShot);
        Assert.Null(first.LastComputerShot);
        Assert.Empty(first.Turns);
        Assert.Equal(initial.CreatedAtUtc, first.CreatedAtUtc.ToDateTimeOffset());

        using var firstFire = await http.PostAsJsonAsync($"/api/games/{initial.Id}/fire", new FireRequest(0, 0));
        firstFire.EnsureSuccessStatusCode();
        using var fired = await http.PostAsJsonAsync($"/api/games/{initial.Id}/fire", new FireRequest(0, 1));
        var state = await fired.Content.ReadFromJsonAsync<GameStateDto>(ApiFactory.JsonOptions);
        Assert.NotNull(state);
        var reply = await client.GetGameStatusAsync(new GetGameStatusRequest { GameId = initial.Id.ToString() });
        Assert.Equal(state.Id.ToString(), reply.GameId);
        Assert.Equal(state.PlayerName, reply.PlayerName);
        Assert.Equal(state.TurnNumber, reply.TurnNumber);
        Assert.Equal(state.CreatedAtUtc, reply.CreatedAtUtc.ToDateTimeOffset());
        Assert.Equal(2, reply.Turns.Count);
        Assert.Equal(state.SkillPoints, reply.SkillPoints);
        Assert.Equal((int)state.Difficulty, (int)reply.Difficulty);
        Assert.Equal(DifficultyLevel.Hard, reply.Difficulty);
        foreach (var (expected, actual) in state.Turns.Zip(reply.Turns))
            AssertTurn(expected, actual);
        Assert.Equal((int)state.Status, (int)reply.Status);
        Assert.Equal(100, reply.PlayerGrid.Count);
        Assert.Equal(100, reply.OpponentGrid.Count);
        Assert.Equal(state.PlayerGrid.Select(cell => (cell.Row, cell.Column, (int)cell.State)),
            reply.PlayerGrid.Select(cell => (cell.Row, cell.Column, (int)cell.State)));
        Assert.Equal(state.OpponentGrid.Select(cell => (cell.Row, cell.Column, (int)cell.State)),
            reply.OpponentGrid.Select(cell => (cell.Row, cell.Column, (int)cell.State)));
        Assert.DoesNotContain(reply.OpponentGrid, cell => cell.State is CellKind.Ship or CellKind.Water);
        Assert.NotNull(state.LastPlayerShot);
        Assert.NotNull(state.LastComputerShot);
        Assert.Equal(state.LastPlayerShot.Position.Row, reply.LastPlayerShot.Row);
        Assert.Equal(state.LastPlayerShot.Position.Column, reply.LastPlayerShot.Column);
        Assert.Equal((int)state.LastPlayerShot.Outcome, (int)reply.LastPlayerShot.Outcome);
        Assert.Equal(state.LastComputerShot.Position.Row, reply.LastComputerShot.Row);
        Assert.Equal(state.LastComputerShot.Position.Column, reply.LastComputerShot.Column);
        Assert.Equal((int)state.LastComputerShot.Outcome, (int)reply.LastComputerShot.Outcome);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Invalid_grpc_identifiers_return_invalid_argument(string id)
    {
        using var channel = CreateChannel(GrpcWebMode.GrpcWeb);
        var client = new GameService.GameServiceClient(channel);
        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await client.GetGameStatusAsync(new GetGameStatusRequest { GameId = id }));
        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }

    [Fact]
    public async Task Unknown_grpc_game_returns_not_found()
    {
        using var channel = CreateChannel(GrpcWebMode.GrpcWeb);
        var client = new GameService.GameServiceClient(channel);
        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await client.GetGameStatusAsync(new GetGameStatusRequest { GameId = Guid.NewGuid().ToString() }));
        Assert.Equal(StatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task Grpc_web_preflight_accepts_browser_headers()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/battleship.v1.GameService/GetGameStatus");
        request.Headers.Add("Origin", "http://localhost:5221");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type,x-grpc-web,grpc-timeout,x-user-agent");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("http://localhost:5221", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Theory]
    [InlineData(GameAction.Mine, 2)]
    [InlineData(GameAction.SquareStrike, 4)]
    [InlineData(GameAction.RowStrike, 6)]
    [InlineData(GameAction.ColumnStrike, 6)]
    public async Task Grpc_web_preserves_power_history_points_and_mines(GameAction action, int cost)
    {
        using var http = factory.CreateClient();
        using var created = await http.PostAsJsonAsync("/api/games", new CreateGameRequest("Alice"));
        var state = (await created.Content.ReadFromJsonAsync<GameStateDto>(ApiFactory.JsonOptions))!;
        for (var index = 0; index < cost; index++)
        {
            using var fired = await http.PostAsJsonAsync($"/api/games/{state.Id}/fire", new FireRequest(0, index));
            state = (await fired.Content.ReadFromJsonAsync<GameStateDto>(ApiFactory.JsonOptions))!;
        }
        var target = action == GameAction.Mine ? state.PlayerGrid.Last(cell => cell.State is CellState.Water or CellState.Ship)
            : new CellDto(2, 8, CellState.Unknown);
        using var powered = await http.PostAsJsonAsync($"/api/games/{state.Id}/powers", new UsePowerRequest(action, target.Row, target.Column));
        powered.EnsureSuccessStatusCode();
        state = (await powered.Content.ReadFromJsonAsync<GameStateDto>(ApiFactory.JsonOptions))!;
        using var channel = CreateChannel(GrpcWebMode.GrpcWeb);
        var reply = await new GameService.GameServiceClient(channel).GetGameStatusAsync(new GetGameStatusRequest { GameId = state.Id.ToString() });
        Assert.Equal(state.SkillPoints, reply.SkillPoints);
        Assert.Equal(state.PlayerGrid.Select(cell => (cell.Row, cell.Column, (int)cell.State, cell.HasMine)),
            reply.PlayerGrid.Select(cell => (cell.Row, cell.Column, (int)cell.State, cell.HasMine)));
        Assert.Equal(state.OpponentGrid.Select(cell => (cell.Row, cell.Column, (int)cell.State, cell.HasMine)),
            reply.OpponentGrid.Select(cell => (cell.Row, cell.Column, (int)cell.State, cell.HasMine)));
        Assert.Equal(state.Turns.Length, reply.Turns.Count);
        foreach (var (expected, actual) in state.Turns.Zip(reply.Turns))
            AssertTurn(expected, actual);
    }

    [Fact]
    public async Task Grpc_web_exposes_the_placement_phase_and_remaining_ships()
    {
        using var http = factory.CreateClient();
        using var created = await http.PostAsJsonAsync("/api/games",
            new CreateGameRequest("Alice", Difficulty.Normal, ManualPlacement: true));
        var state = (await created.Content.ReadFromJsonAsync<GameStateDto>(ApiFactory.JsonOptions))!;
        using var channel = CreateChannel(GrpcWebMode.GrpcWeb);
        var client = new GameService.GameServiceClient(channel);

        var placing = await client.GetGameStatusAsync(new GetGameStatusRequest { GameId = state.Id.ToString() });
        Assert.Equal(GamePhase.Placing, placing.Status);
        Assert.Equal(5, placing.ShipsToPlace.Count);
        Assert.Equal(Board.Fleet.Select(spec => (int)spec.Kind).OrderBy(kind => kind),
            placing.ShipsToPlace.Select(spec => (int)spec.Kind).OrderBy(kind => kind));
        Assert.Equal(Board.Fleet.Select(spec => spec.Size).OrderBy(size => size),
            placing.ShipsToPlace.Select(spec => spec.Size).OrderBy(size => size));

        using var placed = await http.PostAsJsonAsync($"/api/games/{state.Id}/fleet/place",
            new PlaceShipRequest(global::BattleShip.Models.Enums.ShipKind.AircraftCarrier, 0, 0));
        placed.EnsureSuccessStatusCode();
        var partial = await client.GetGameStatusAsync(new GetGameStatusRequest { GameId = state.Id.ToString() });
        Assert.Equal(GamePhase.Placing, partial.Status);
        Assert.Equal(4, partial.ShipsToPlace.Count);
        Assert.DoesNotContain(partial.ShipsToPlace,
            spec => spec.Kind == global::BattleShip.Grpc.ShipKind.AircraftCarrier);

        using var random = await http.PostAsync($"/api/games/{state.Id}/fleet/randomize", null);
        random.EnsureSuccessStatusCode();
        using var confirm = await http.PostAsync($"/api/games/{state.Id}/fleet/confirm", null);
        confirm.EnsureSuccessStatusCode();
        var fighting = await client.GetGameStatusAsync(new GetGameStatusRequest { GameId = state.Id.ToString() });
        Assert.Equal(GamePhase.InProgress, fighting.Status);
        Assert.Empty(fighting.ShipsToPlace);
    }

    private static void AssertTurn(TurnDto expected, TurnMessage actual)
    {
        Assert.Equal(expected.Number, actual.Number);
        Assert.Equal((int)expected.Action, (int)actual.Action);
        Assert.Equal(expected.SkillPointsAfter, actual.SkillPointsAfter);
        Assert.Equal(expected.PlayerShot, ReadShot(actual.PlayerShot));
        Assert.Equal(expected.ComputerShot, ReadShot(actual.ComputerShot));
        Assert.Equal(expected.Target, actual.Target is null ? null : new Position(actual.Target.Row, actual.Target.Column));
        Assert.Equal(expected.GetPlayerShots(), actual.PlayerShots.Select(shot => ReadShot(shot)!));
        if (expected.MineDetonation is { } mine)
        {
            Assert.NotNull(actual.MineDetonation);
            Assert.Equal(mine.Position, new Position(actual.MineDetonation.Position.Row, actual.MineDetonation.Position.Column));
            Assert.Equal(mine.ReflectedShot, ReadShot(actual.MineDetonation.ReflectedShot));
        }
        else
            Assert.Null(actual.MineDetonation);
    }

    private static ShotDto? ReadShot(ShotMessage? shot) => shot is null ? null
        : new ShotDto(new Position(shot.Row, shot.Column), (ShotOutcome)shot.Outcome);

    private GrpcChannel CreateChannel(GrpcWebMode mode) => GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
    {
        HttpHandler = new GrpcWebHandler(mode, factory.Server.CreateHandler()),
        DisposeHttpClient = true,
        HttpVersion = HttpVersion.Version11,
        HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact
    });
}
