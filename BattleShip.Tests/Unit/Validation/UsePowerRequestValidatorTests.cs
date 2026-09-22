using BattleShip.API.Features.Games.Http.Validation;
using BattleShip.Models.Contracts;
using BattleShip.Models.Enums;
using FluentValidation.TestHelper;

namespace BattleShip.Tests.Unit.Validation;

public sealed class UsePowerRequestValidatorTests
{
    [Theory]
    [InlineData(GameAction.NormalShot, 0, 0)]
    [InlineData((GameAction)99, 0, 0)]
    [InlineData(GameAction.Mine, -1, 0)]
    [InlineData(GameAction.RowStrike, 10, 0)]
    [InlineData(GameAction.ColumnStrike, 0, 10)]
    [InlineData(GameAction.SquareStrike, 9, 0)]
    [InlineData(GameAction.SquareStrike, 0, 9)]
    public void Invalid_powers_are_rejected(GameAction action, int row, int column)
    {
        Assert.False(new UsePowerRequestValidator().TestValidate(new UsePowerRequest(action, row, column)).IsValid);
    }

    [Theory]
    [InlineData(GameAction.Mine, 9, 9)]
    [InlineData(GameAction.SquareStrike, 8, 8)]
    [InlineData(GameAction.RowStrike, 9, 9)]
    [InlineData(GameAction.ColumnStrike, 9, 9)]
    public void Valid_power_targets_are_accepted(GameAction action, int row, int column)
    {
        new UsePowerRequestValidator().TestValidate(new UsePowerRequest(action, row, column)).ShouldNotHaveAnyValidationErrors();
    }
}
