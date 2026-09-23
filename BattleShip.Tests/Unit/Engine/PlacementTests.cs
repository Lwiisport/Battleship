using BattleShip.Models.Engine;
using BattleShip.Models.Enums;
using BattleShip.Models.Exceptions;

namespace BattleShip.Tests.Unit.Engine;

public sealed class PlacementTests
{
    private static Game ManualGame() => new("Alice", Difficulty.Easy, new Random(7), manualPlacement: true);

    [Fact]
    public void Manual_game_starts_in_placement_with_the_whole_fleet_to_place()
    {
        var state = ManualGame().GetState();
        Assert.Equal(GameStatus.PlacingShips, state.Status);
        Assert.Equal(0, state.TurnNumber);
        Assert.Equal(Board.Fleet.OrderBy(spec => spec.Kind), state.ShipsToPlace!.OrderBy(spec => spec.Kind));
        Assert.All(state.PlayerGrid, cell => Assert.Equal(CellState.Water, cell.State));
        Assert.All(state.OpponentGrid, cell => Assert.Equal(CellState.Unknown, cell.State));
    }

    [Fact]
    public void Automatic_game_starts_in_progress_with_nothing_to_place()
    {
        var state = new Game("Alice", Difficulty.Easy, new Random(7)).GetState();
        Assert.Equal(GameStatus.InProgress, state.Status);
        Assert.Empty(state.ShipsToPlace!);
        Assert.Equal(17, state.PlayerGrid.Count(cell => cell.State == CellState.Ship));
    }

    [Fact]
    public void PlaceShip_reveals_the_footprint_and_shrinks_the_remaining_fleet()
    {
        var state = ManualGame().PlaceShip(ShipKind.AircraftCarrier, new Position(0, 0), vertical: false);
        Assert.Equal(GameStatus.PlacingShips, state.Status);
        for (var column = 0; column < 5; column++)
            Assert.Equal(CellState.Ship, state.PlayerGrid[column].State);
        Assert.DoesNotContain(state.ShipsToPlace!, spec => spec.Kind == ShipKind.AircraftCarrier);
        Assert.Equal(4, state.ShipsToPlace!.Length);
    }

    [Fact]
    public void Vertical_placement_occupies_a_column()
    {
        var state = ManualGame().PlaceShip(ShipKind.PatrolBoat, new Position(2, 3), vertical: true);
        Assert.Equal(CellState.Ship, state.PlayerGrid[2 * Board.Size + 3].State);
        Assert.Equal(CellState.Ship, state.PlayerGrid[3 * Board.Size + 3].State);
        Assert.Equal(CellState.Water, state.PlayerGrid[4 * Board.Size + 3].State);
    }

    [Fact]
    public void Out_of_bounds_or_overlapping_placements_are_rejected_without_mutation()
    {
        var game = ManualGame();
        game.PlaceShip(ShipKind.Cruiser, new Position(0, 0), vertical: false);
        var outOfBounds = Assert.Throws<GameRuleException>(
            () => game.PlaceShip(ShipKind.Destroyer, new Position(0, 8), vertical: false));
        Assert.Equal(GameError.InvalidShipPlacement, outOfBounds.Error);
        var overlap = Assert.Throws<GameRuleException>(
            () => game.PlaceShip(ShipKind.Destroyer, new Position(0, 2), vertical: true));
        Assert.Equal(GameError.InvalidShipPlacement, overlap.Error);
        var state = game.GetState();
        Assert.Equal(4, state.PlayerGrid.Count(cell => cell.State == CellState.Ship));
        Assert.Equal(4, state.ShipsToPlace!.Length);
    }

    [Fact]
    public void Placing_the_same_kind_again_repositions_the_ship()
    {
        var game = ManualGame();
        game.PlaceShip(ShipKind.PatrolBoat, new Position(0, 0), vertical: false);
        var state = game.PlaceShip(ShipKind.PatrolBoat, new Position(5, 5), vertical: true);
        Assert.Equal(2, state.PlayerGrid.Count(cell => cell.State == CellState.Ship));
        Assert.Equal(CellState.Ship, state.PlayerGrid[5 * Board.Size + 5].State);
        Assert.Equal(CellState.Ship, state.PlayerGrid[6 * Board.Size + 5].State);
    }

    [Fact]
    public void Failed_replacement_keeps_the_original_ship()
    {
        var game = ManualGame();
        game.PlaceShip(ShipKind.PatrolBoat, new Position(0, 0), vertical: false);
        Assert.Throws<GameRuleException>(() => game.PlaceShip(ShipKind.PatrolBoat, new Position(9, 9), vertical: true));
        var state = game.GetState();
        Assert.Equal(CellState.Ship, state.PlayerGrid[0].State);
        Assert.Equal(CellState.Ship, state.PlayerGrid[1].State);
    }

    [Fact]
    public void RemoveShip_returns_the_kind_to_the_remaining_fleet()
    {
        var game = ManualGame();
        game.PlaceShip(ShipKind.Submarine, new Position(0, 0), vertical: false);
        var state = game.RemoveShip(ShipKind.Submarine);
        Assert.Contains(state.ShipsToPlace!, spec => spec.Kind == ShipKind.Submarine);
        Assert.All(state.PlayerGrid, cell => Assert.Equal(CellState.Water, cell.State));
        var missing = Assert.Throws<GameRuleException>(() => game.RemoveShip(ShipKind.Submarine));
        Assert.Equal(GameError.ShipNotPlaced, missing.Error);
    }

    [Fact]
    public void Confirming_an_incomplete_fleet_is_rejected()
    {
        var game = ManualGame();
        game.PlaceShip(ShipKind.PatrolBoat, new Position(0, 0), vertical: false);
        var exception = Assert.Throws<GameRuleException>(() => game.StartBattle());
        Assert.Equal(GameError.FleetIncomplete, exception.Error);
        Assert.Equal(GameStatus.PlacingShips, game.GetState().Status);
    }

    [Fact]
    public void Randomize_completes_the_remaining_ships_without_overlap()
    {
        var game = ManualGame();
        game.PlaceShip(ShipKind.AircraftCarrier, new Position(0, 0), vertical: false);
        var state = game.RandomizeFleet();
        Assert.Empty(state.ShipsToPlace!);
        Assert.Equal(17, state.PlayerGrid.Count(cell => cell.State == CellState.Ship));
        Assert.Equal(CellState.Ship, state.PlayerGrid[0].State);
        state = game.StartBattle();
        Assert.Equal(GameStatus.InProgress, state.Status);
    }

    [Fact]
    public void Once_started_the_fleet_is_locked_and_turns_can_be_played()
    {
        var game = ManualGame();
        game.RandomizeFleet();
        game.StartBattle();
        var locked = Assert.Throws<GameRuleException>(() => game.PlaceShip(ShipKind.PatrolBoat, new Position(0, 0), vertical: false));
        Assert.Equal(GameError.FleetAlreadyLocked, locked.Error);
        Assert.Throws<GameRuleException>(() => game.RemoveShip(ShipKind.PatrolBoat));
        Assert.Throws<GameRuleException>(() => game.RandomizeFleet());
        Assert.Throws<GameRuleException>(() => game.StartBattle());
        var state = game.Fire(new Position(0, 0));
        Assert.Equal(1, state.TurnNumber);
    }

    [Fact]
    public void Shots_and_powers_are_rejected_during_placement()
    {
        var game = ManualGame();
        var shot = Assert.Throws<GameRuleException>(() => game.Fire(new Position(0, 0)));
        Assert.Equal(GameError.GameNotStarted, shot.Error);
        var power = Assert.Throws<GameRuleException>(() => game.UsePower(GameAction.Mine, new Position(0, 0)));
        Assert.Equal(GameError.GameNotStarted, power.Error);
        Assert.Empty(game.GetState().Turns);
    }
}
