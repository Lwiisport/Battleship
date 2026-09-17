using BattleShip.Models.Enums;

namespace BattleShip.Models.Exceptions;

public sealed class GameRuleException(GameError error, string message) : Exception(message)
{
    public GameError Error { get; } = error;
}
