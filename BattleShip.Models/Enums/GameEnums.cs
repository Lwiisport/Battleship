namespace BattleShip.Models.Enums;

public enum ShipKind { AircraftCarrier, Cruiser, Destroyer, Submarine, PatrolBoat }
public enum CellState { Unknown, Water, Ship, Miss, Hit, Sunk }
public enum ShotOutcome { Miss, Hit, Sunk }
public enum GameStatus { InProgress, PlayerWon, ComputerWon }
public enum GameError { InvalidPosition, DuplicateShot, GameFinished }
