using BattleShip.App;
using BattleShip.App.Services;
using BattleShip.Grpc;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var address = builder.Configuration["ApiBaseUrl"] ?? builder.HostEnvironment.BaseAddress;
if (!Uri.TryCreate(address.TrimEnd('/') + "/", UriKind.Absolute, out var apiUri)
    || apiUri.Scheme is not ("http" or "https"))
    throw new InvalidOperationException("ApiBaseUrl doit être une URL HTTP ou HTTPS absolue.");

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = apiUri, Timeout = TimeSpan.FromSeconds(15) });
builder.Services.AddScoped<GameHttpClient>();
builder.Services.AddScoped(_ => GrpcChannel.ForAddress(apiUri, new GrpcChannelOptions
{
    HttpHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, new HttpClientHandler()),
    DisposeHttpClient = true
}));
builder.Services.AddScoped(services => new GameService.GameServiceClient(services.GetRequiredService<GrpcChannel>()));
builder.Services.AddScoped<GameGrpcClient>();

await builder.Build().RunAsync();
