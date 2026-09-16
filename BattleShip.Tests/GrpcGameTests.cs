using System.Net;
using System.Net.Http.Json;
using BattleShip.API.Validation;
using BattleShip.Grpc;
using BattleShip.Models;
using FluentValidation.TestHelper;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;

namespace BattleShip.Tests;

public sealed class GrpcGameTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData(GrpcWebMode.GrpcWeb)]
    [InlineData(GrpcWebMode.GrpcWebText)]
    public async Task Grpc_web_returns_the_same_masked_state_as_http(GrpcWebMode mode)
    {
        using var http = factory.CreateClient();
        using var created = await http.PostAsJsonAsync("/api/games", new CreateGameRequest("Alice"));
        var initial = await created.Content.ReadFromJsonAsync<GameStateDto>(ApiFactory.JsonOptions);
        Assert.NotNull(initial);
        using var channel = CreateChannel(mode);
        var client = new GameService.GameServiceClient(channel);
        var first = await client.GetGameStatusAsync(new GetGameStatusRequest { GameId = initial.Id.ToString() });
        Assert.All(first.OpponentGrid, cell => Assert.Equal(CellKind.Unknown, cell.State));
        Assert.Null(first.LastPlayerShot);
        Assert.Null(first.LastComputerShot);

        using var fired = await http.PostAsJsonAsync($"/api/games/{initial.Id}/fire", new FireRequest(0, 0));
        var state = await fired.Content.ReadFromJsonAsync<GameStateDto>(ApiFactory.JsonOptions);
        Assert.NotNull(state);
        var reply = await client.GetGameStatusAsync(new GetGameStatusRequest { GameId = initial.Id.ToString() });
        Assert.Equal(state.Id.ToString(), reply.GameId);
        Assert.Equal(state.PlayerName, reply.PlayerName);
        Assert.Equal(state.TurnNumber, reply.TurnNumber);
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

    [Theory]
    [InlineData("")]
    [InlineData("wrong")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Grpc_validator_rejects_invalid_identifiers(string id)
    {
        new GetGameStatusRequestValidator().TestValidate(new GetGameStatusRequest { GameId = id })
            .ShouldHaveValidationErrorFor(request => request.GameId);
    }

    [Fact]
    public void Grpc_validator_accepts_valid_identifiers()
    {
        new GetGameStatusRequestValidator().TestValidate(new GetGameStatusRequest { GameId = Guid.NewGuid().ToString() })
            .ShouldNotHaveAnyValidationErrors();
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

    private GrpcChannel CreateChannel(GrpcWebMode mode) => GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
    {
        HttpHandler = new GrpcWebHandler(mode, factory.Server.CreateHandler()),
        DisposeHttpClient = true,
        HttpVersion = HttpVersion.Version11,
        HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact
    });
}
