using System.Text.Json;
using System.Text.Json.Serialization;
using BattleShip.Models;
using Microsoft.JSInterop;

namespace BattleShip.App.Services;

public sealed record GameHistoryEntry(GameStateDto State, DateTimeOffset SavedAtUtc);

public sealed class GameHistoryStore(IJSRuntime js)
{
    public const string StorageKey = "battleship.history.v1";
    public const int Capacity = 50;
    private readonly SemaphoreSlim gate = new(1, 1);

    public IReadOnlyList<GameHistoryEntry> Entries { get; private set; } = [];
    public string? Warning { get; private set; }

    public async Task LoadAsync()
    {
        await gate.WaitAsync();
        try
        {
            Entries = Merge(Entries, await ReadAsync());
            Warning = null;
        }
        catch (Exception exception) when (exception is JSException or JsonException)
        {
            Warning = "L’historique local est indisponible ou illisible. Les parties restent accessibles pendant cette session.";
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task RememberAsync(GameStateDto state)
    {
        await gate.WaitAsync();
        try
        {
            Entries = Merge(Entries, [new GameHistoryEntry(state, DateTimeOffset.UtcNow)]);
            Entries = Merge(Entries, await ReadAsync());
            var json = JsonSerializer.Serialize(Entries.ToArray(), GameHistoryJsonContext.Default.GameHistoryEntryArray);
            await js.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
            Warning = null;
        }
        catch (Exception exception) when (exception is JSException or JsonException)
        {
            Warning = "La sauvegarde locale a échoué. Cette partie reste dans l’historique de la session, mais sa conservation après fermeture n’est pas garantie.";
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<GameHistoryEntry[]> ReadAsync()
    {
        var json = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        if (json is null)
            return [];
        if (json.Length > 4_000_000)
            throw new JsonException("Historique trop volumineux.");
        var entries = JsonSerializer.Deserialize(json, GameHistoryJsonContext.Default.GameHistoryEntryArray)
            ?? throw new JsonException("Historique vide ou invalide.");
        if (entries.Any(entry => entry is null || !IsValid(entry.State) || entry.SavedAtUtc == default))
            throw new JsonException("Une partie archivée est invalide.");
        return entries;
    }

    private static GameHistoryEntry[] Merge(IEnumerable<GameHistoryEntry> current, IEnumerable<GameHistoryEntry> incoming) =>
        current.Concat(incoming).GroupBy(entry => entry.State.Id)
            .Select(group => group.OrderByDescending(entry => entry.State.TurnNumber)
                .ThenByDescending(entry => entry.SavedAtUtc).First())
            .OrderByDescending(entry => entry.State.CreatedAtUtc)
            .ThenBy(entry => entry.State.Id)
            .Take(Capacity).ToArray();

    private static bool IsValid(GameStateDto? state) => state is not null
        && state.Id != Guid.Empty && !string.IsNullOrWhiteSpace(state.PlayerName) && state.PlayerName.Length <= 40
        && state.CreatedAtUtc != default && Enum.IsDefined(state.Status)
        && state.TurnNumber is >= 0 and <= Board.Size * Board.Size
        && ValidGrid(state.PlayerGrid, false) && ValidGrid(state.OpponentGrid, true)
        && state.Turns is not null && state.Turns.Length == state.TurnNumber
        && state.Turns.Select((turn, index) => turn is not null && turn.Number == index + 1
            && ValidShot(turn.PlayerShot) && (turn.ComputerShot is null
                ? state.Status == GameStatus.PlayerWon && index == state.TurnNumber - 1
                : ValidShot(turn.ComputerShot))).All(valid => valid);

    private static bool ValidGrid(CellDto[]? grid, bool opponent) => grid is { Length: Board.Size * Board.Size }
        && grid.Select((cell, index) => cell is not null && cell.Row == index / Board.Size && cell.Column == index % Board.Size
            && Enum.IsDefined(cell.State) && (!opponent || cell.State is not (CellState.Ship or CellState.Water))).All(valid => valid);

    private static bool ValidShot(ShotDto? shot) => shot is not null && shot.Position.IsValid && Enum.IsDefined(shot.Outcome);
}

[JsonSourceGenerationOptions(RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(GameHistoryEntry[]))]
internal partial class GameHistoryJsonContext : JsonSerializerContext { }
