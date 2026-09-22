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
    private readonly HashSet<Position> mines = [];
    private GameStatus status = GameStatus.InProgress;
    private int turnNumber;
    private int skillPoints;
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

    public GameStateDto Fire(Position position) => Play(GameAction.NormalShot, position);

    public GameStateDto UsePower(GameAction action, Position position)
    {
        if (!Enum.IsDefined(action) || action == GameAction.NormalShot)
            throw new GameRuleException(GameError.InvalidPower, "Ce pouvoir n’existe pas.");
        return Play(action, position);
    }

    private GameStateDto Play(GameAction action, Position position)
    {
        lock (gate)
        {
            if (status != GameStatus.InProgress)
                throw new GameRuleException(GameError.GameFinished, "La partie est terminée.");
            var targets = PowerRules.Targets(action, position);
            if (targets.Length == 0)
                throw new GameRuleException(GameError.InvalidPosition, "La zone doit rester entièrement dans la grille de 10 × 10.");
            var cost = PowerRules.Cost(action);
            if (skillPoints < cost)
                throw new GameRuleException(GameError.InsufficientSkillPoints, $"Ce pouvoir nécessite {cost} points de compétence.");

            if (action == GameAction.Mine)
            {
                if (playerBoard.HasBeenShot(position) || mines.Contains(position))
                    throw new GameRuleException(GameError.InvalidMinePlacement, "Une mine nécessite une case non visée et sans autre mine.");
            }
            else
            {
                targets = targets.Where(target => !computerBoard.HasBeenShot(target)).ToArray();
                if (targets.Length == 0)
                    throw new GameRuleException(action == GameAction.NormalShot ? GameError.DuplicateShot : GameError.NoNewTargets,
                        "Toutes les cases de cette cible ont déjà été visées.");
            }

            ShotDto[] playerShots = [];
            if (action == GameAction.Mine)
                mines.Add(position);
            else
                playerShots = targets.Select(computerBoard.Fire).ToArray();
            skillPoints = action == GameAction.NormalShot ? Math.Min(skillPoints + 1, PowerRules.MaxSkillPoints) : skillPoints - cost;
            turnNumber++;
            lastPlayerShot = playerShots.LastOrDefault();
            lastComputerShot = null;
            MineDetonationDto? detonation = null;
            if (computerBoard.AllShipsSunk)
            {
                status = GameStatus.PlayerWon;
            }
            else
            {
                var computerTarget = computerTargets.Dequeue();
                lastComputerShot = playerBoard.Fire(computerTarget);
                if (mines.Remove(computerTarget))
                    detonation = new MineDetonationDto(computerTarget,
                        computerBoard.HasBeenShot(computerTarget) ? null : computerBoard.Fire(computerTarget));
                status = (playerBoard.AllShipsSunk, computerBoard.AllShipsSunk) switch
                {
                    (true, true) => GameStatus.Draw,
                    (true, false) => GameStatus.ComputerWon,
                    (false, true) => GameStatus.PlayerWon,
                    _ => GameStatus.InProgress
                };
            }
            turns.Add(new TurnDto(turnNumber, lastPlayerShot, lastComputerShot, action, position, playerShots, detonation, skillPoints));
            return Snapshot();
        }
    }

    private GameStateDto Snapshot() => new(Id, PlayerName, status, turnNumber,
        playerBoard.ToGrid(revealShips: true).Select(cell => cell with { HasMine = mines.Contains(new Position(cell.Row, cell.Column)) }).ToArray(),
        computerBoard.ToGrid(revealShips: false), lastPlayerShot, lastComputerShot, CreatedAtUtc,
        turns.Select(turn => turn with { PlayerShots = turn.PlayerShots?.ToArray() }).ToArray(), skillPoints);
}
