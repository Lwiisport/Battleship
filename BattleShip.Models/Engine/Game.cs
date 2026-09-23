using BattleShip.Models.Contracts;
using BattleShip.Models.Enums;
using BattleShip.Models.Exceptions;

namespace BattleShip.Models.Engine;

public sealed class Game
{
    private readonly object gate = new();
    private readonly Board playerBoard;
    private readonly Board computerBoard;
    private readonly ComputerOpponent opponent;
    private readonly Random random;
    private readonly List<TurnDto> turns = [];
    private readonly HashSet<Position> mines = [];
    private GameStatus status = GameStatus.InProgress;
    private int turnNumber;
    private int skillPoints;
    private ShotDto? lastPlayerShot;
    private ShotDto? lastComputerShot;

    public Game(string playerName, Random? random = null, TimeProvider? clock = null)
        : this(playerName, Difficulty.Easy, random, clock) { }

    public Game(string playerName, Difficulty difficulty, Random? random = null, TimeProvider? clock = null,
        bool manualPlacement = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerName);
        if (playerName.Trim().Length > 40)
            throw new ArgumentException("Le nom doit contenir au maximum 40 caractères.", nameof(playerName));
        if (!Enum.IsDefined(difficulty))
            throw new ArgumentException("La difficulté est inconnue.", nameof(difficulty));
        random ??= Random.Shared;
        this.random = random;
        Id = Guid.NewGuid();
        CreatedAtUtc = (clock ?? TimeProvider.System).GetUtcNow();
        PlayerName = playerName.Trim();
        Difficulty = difficulty;
        playerBoard = manualPlacement ? Board.CreateEmpty() : Board.CreateRandom(random);
        computerBoard = Board.CreateRandom(random);
        var targets = playerBoard.AvailableTargets();
        random.Shuffle(targets);
        opponent = new ComputerOpponent(difficulty, new Queue<Position>(targets), random);
        status = manualPlacement ? GameStatus.PlacingShips : GameStatus.InProgress;
    }

    public Guid Id { get; }
    public string PlayerName { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public Difficulty Difficulty { get; }

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

    public GameStateDto PlaceShip(ShipKind kind, Position start, bool vertical)
    {
        lock (gate)
        {
            RequirePlacing();
            playerBoard.PlaceShip(kind, start, vertical);
            return Snapshot();
        }
    }

    public GameStateDto RemoveShip(ShipKind kind)
    {
        lock (gate)
        {
            RequirePlacing();
            playerBoard.RemoveShip(kind);
            return Snapshot();
        }
    }

    public GameStateDto RandomizeFleet()
    {
        lock (gate)
        {
            RequirePlacing();
            playerBoard.Randomize(random);
            return Snapshot();
        }
    }

    public GameStateDto StartBattle()
    {
        lock (gate)
        {
            RequirePlacing();
            if (!playerBoard.IsFleetComplete)
                throw new GameRuleException(GameError.FleetIncomplete, "Tous les navires doivent être posés avant le combat.");
            status = GameStatus.InProgress;
            return Snapshot();
        }
    }

    private void RequirePlacing()
    {
        if (status != GameStatus.PlacingShips)
            throw new GameRuleException(GameError.FleetAlreadyLocked, "La flotte est déjà engagée au combat.");
    }

    private GameStateDto Play(GameAction action, Position position)
    {
        lock (gate)
        {
            if (status == GameStatus.PlacingShips)
                throw new GameRuleException(GameError.GameNotStarted, "Posez votre flotte puis lancez le combat.");
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
                var computerTarget = opponent.ChooseTarget(playerBoard.ToGrid(revealShips: false));
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
        turns.Select(turn => turn with { PlayerShots = turn.PlayerShots?.ToArray() }).ToArray(), skillPoints, Difficulty,
        playerBoard.ShipsToPlace.ToArray());
}
