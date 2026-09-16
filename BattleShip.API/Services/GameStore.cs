using System.Diagnostics.CodeAnalysis;
using BattleShip.Models;
using Microsoft.Extensions.Options;

namespace BattleShip.API.Services;

public sealed class GameStoreOptions
{
    public int Capacity { get; set; } = 1000;
    public int LifetimeMinutes { get; set; } = 120;
}

public sealed class GameStore(IOptions<GameStoreOptions> options, TimeProvider clock)
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, Entry> games = [];

    public bool TryCreate(string playerName, [NotNullWhen(true)] out Game? game)
    {
        lock (gate)
        {
            var now = clock.GetUtcNow();
            foreach (var id in games.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
                games.Remove(id);
            if (games.Count >= options.Value.Capacity)
            {
                game = null;
                return false;
            }
            game = new Game(playerName);
            games.Add(game.Id, new Entry(game, now.AddMinutes(options.Value.LifetimeMinutes)));
            return true;
        }
    }

    public Game? Find(Guid id)
    {
        lock (gate)
        {
            if (!games.TryGetValue(id, out var entry))
                return null;
            if (entry.ExpiresAt > clock.GetUtcNow())
                return entry.Game;
            games.Remove(id);
            return null;
        }
    }

    private sealed record Entry(Game Game, DateTimeOffset ExpiresAt);
}
