using System.Text.Json;
using System.Text.Json.Serialization;
using BattleShip.Models.Contracts;
using BattleShip.Models.Engine;
using BattleShip.Models.Enums;
using Microsoft.JSInterop;

namespace BattleShip.App.Features.History.Services;

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
        && state.SkillPoints is >= 0 and <= PowerRules.MaxSkillPoints
        && ValidGrid(state.PlayerGrid, false) && ValidGrid(state.OpponentGrid, true)
        && state.Turns is not null && state.Turns.Length == state.TurnNumber
        && state.Turns.Select((turn, index) => ValidTurn(turn, index, state)).All(valid => valid);

    private static bool ValidTurn(TurnDto? turn, int index, GameStateDto state)
    {
        if (turn is null || turn.Number != index + 1 || !Enum.IsDefined(turn.Action)
            || turn.SkillPointsAfter is < 0 or > PowerRules.MaxSkillPoints)
            return false;
        var shots = turn.GetPlayerShots();
        var target = turn.Target ?? turn.PlayerShot?.Position;
        if (target is null || PowerRules.Targets(turn.Action, target.Value) is not { Length: > 0 } targets)
            return false;
        if (turn.Action == GameAction.Mine)
        {
            if (turn.PlayerShot is not null || shots.Count != 0)
                return false;
        }
        else if (shots.Count == 0 || shots.Count > targets.Length || !shots.All(ValidShot)
            || shots.Select(shot => shot.Position).Distinct().Count() != shots.Count
            || shots.Any(shot => !targets.Contains(shot.Position)) || turn.PlayerShot != shots[^1])
            return false;
        if (turn.ComputerShot is null ? state.Status != GameStatus.PlayerWon || index != state.TurnNumber - 1
            : !ValidShot(turn.ComputerShot))
            return false;
        return turn.MineDetonation is not { } mine || (mine.Position.IsValid && turn.ComputerShot?.Position == mine.Position
            && (mine.ReflectedShot is null || (ValidShot(mine.ReflectedShot) && mine.ReflectedShot.Position == mine.Position)));
    }

    private static bool ValidGrid(CellDto[]? grid, bool opponent) => grid is { Length: Board.Size * Board.Size }
        && grid.Select((cell, index) => cell is not null && cell.Row == index / Board.Size && cell.Column == index % Board.Size
            && Enum.IsDefined(cell.State) && (!cell.HasMine || cell.State is CellState.Water or CellState.Ship)
            && (!opponent || (!cell.HasMine && cell.State is not (CellState.Ship or CellState.Water)))).All(valid => valid);

    private static bool ValidShot(ShotDto? shot) => shot is not null && shot.Position.IsValid && Enum.IsDefined(shot.Outcome);
}

[JsonSourceGenerationOptions(RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(GameHistoryEntry[]))]
internal partial class GameHistoryJsonContext : JsonSerializerContext { }
