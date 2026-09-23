namespace BattleShip.Models.Enums;

public enum ShipKind { AircraftCarrier, Cruiser, Destroyer, Submarine, PatrolBoat }
public enum CellState { Unknown, Water, Ship, Miss, Hit, Sunk }
public enum ShotOutcome { Miss, Hit, Sunk }
public enum GameStatus { InProgress, PlayerWon, ComputerWon, Draw, PlacingShips }
public enum Difficulty { Easy, Normal, Hard }
public enum GameAction { NormalShot, Mine, SquareStrike, RowStrike, ColumnStrike }
public enum GameError { InvalidPosition, DuplicateShot, GameFinished, InvalidPower, InsufficientSkillPoints, InvalidMinePlacement, NoNewTargets, GameNotStarted, FleetAlreadyLocked, InvalidShipPlacement, ShipNotPlaced, FleetIncomplete }
