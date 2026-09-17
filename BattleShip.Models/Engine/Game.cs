using BattleShip.Models.Contracts;
using BattleShip.Models.Enums;
using BattleShip.Models.Exceptions;

namespace BattleShip.Models.Engine;

public sealed class Game
{
    private readonly object gate = new();
    private readonly Board playerBoard;
    private readonly Board computerBoard;
    private readonly Queue<Position> computerTargets;
    private readonly List<TurnDto> turns = [];
    private GameStatus status = GameStatus.InProgress;
    private int turnNumber;
    private ShotDto? lastPlayerShot;
    private ShotDto? lastComputerShot;

    public Game(string playerName, Random? random = null, TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerName);
        if (playerName.Trim().Length > 40)
            throw new ArgumentException("Le nom doit contenir au maximum 40 caractères.", nameof(playerName));
        random ??= Random.Shared;
        Id = Guid.NewGuid();
        CreatedAtUtc = (clock ?? TimeProvider.System).GetUtcNow();
        PlayerName = playerName.Trim();
        playerBoard = Board.CreateRandom(random);
        computerBoard = Board.CreateRandom(random);
        var targets = playerBoard.AvailableTargets();
        random.Shuffle(targets);
        computerTargets = new Queue<Position>(targets);
    }

    public Guid Id { get; }
    public string PlayerName { get; }
    public DateTimeOffset CreatedAtUtc { get; }

    public GameStateDto GetState()
    {
        lock (gate)
            return Snapshot();
    }

    public GameStateDto Fire(Position position)
    {
        lock (gate)
        {
            if (status != GameStatus.InProgress)
                throw new GameRuleException(GameError.GameFinished, "La partie est terminée.");

            lastPlayerShot = computerBoard.Fire(position);
            turnNumber++;
            lastComputerShot = null;
            if (computerBoard.AllShipsSunk)
            {
                status = GameStatus.PlayerWon;
            }
            else
            {
                lastComputerShot = playerBoard.Fire(computerTargets.Dequeue());
                if (playerBoard.AllShipsSunk)
                    status = GameStatus.ComputerWon;
            }
            turns.Add(new TurnDto(turnNumber, lastPlayerShot, lastComputerShot));
            return Snapshot();
        }
    }

    private GameStateDto Snapshot() => new(Id, PlayerName, status, turnNumber,
        playerBoard.ToGrid(revealShips: true), computerBoard.ToGrid(revealShips: false),
        lastPlayerShot, lastComputerShot, CreatedAtUtc, turns.ToArray());
}
