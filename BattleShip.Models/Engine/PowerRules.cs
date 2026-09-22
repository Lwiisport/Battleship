using BattleShip.Models.Enums;

namespace BattleShip.Models.Engine;

public static class PowerRules
{
    public const int MaxSkillPoints = 10;

    public static int Cost(GameAction action) => action switch
    {
        GameAction.NormalShot => 0,
        GameAction.Mine => 2,
        GameAction.SquareStrike => 4,
        GameAction.RowStrike or GameAction.ColumnStrike => 6,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    public static Position[] Targets(GameAction action, Position anchor)
    {
        if (!anchor.IsValid)
            return [];
        return action switch
        {
            GameAction.NormalShot or GameAction.Mine => [anchor],
            GameAction.SquareStrike when anchor.Row < Board.Size - 1 && anchor.Column < Board.Size - 1 =>
                [anchor, new(anchor.Row, anchor.Column + 1), new(anchor.Row + 1, anchor.Column), new(anchor.Row + 1, anchor.Column + 1)],
            GameAction.RowStrike => Enumerable.Range(0, Board.Size).Select(column => new Position(anchor.Row, column)).ToArray(),
            GameAction.ColumnStrike => Enumerable.Range(0, Board.Size).Select(row => new Position(row, anchor.Column)).ToArray(),
            _ => []
        };
    }
}
