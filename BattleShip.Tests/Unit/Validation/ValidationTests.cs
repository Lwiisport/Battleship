using BattleShip.API.Features.Games.Http.Validation;
using BattleShip.Models.Contracts;
using BattleShip.Models.Enums;
using FluentValidation.TestHelper;

namespace BattleShip.Tests.Unit.Validation;

public sealed class ValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz")]
    public void Creation_rejects_invalid_names(string? name)
    {
        new CreateGameRequestValidator().TestValidate(new CreateGameRequest(name!))
            .ShouldHaveValidationErrorFor(request => request.PlayerName);
    }

    [Theory]
    [InlineData("Alice")]
    [InlineData("Élodie")]
    public void Creation_accepts_valid_names(string name)
    {
        new CreateGameRequestValidator().TestValidate(new CreateGameRequest(name))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Creation_rejects_an_unknown_difficulty()
    {
        new CreateGameRequestValidator().TestValidate(new CreateGameRequest("Alice", (Difficulty)99))
            .ShouldHaveValidationErrorFor(request => request.Difficulty);
    }

    [Theory]
    [InlineData(Difficulty.Easy)]
    [InlineData(Difficulty.Normal)]
    [InlineData(Difficulty.Hard)]
    public void Creation_accepts_every_difficulty(Difficulty difficulty)
    {
        new CreateGameRequestValidator().TestValidate(new CreateGameRequest("Alice", difficulty))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(-1, 0, nameof(FireRequest.Row))]
    [InlineData(10, 0, nameof(FireRequest.Row))]
    [InlineData(0, -1, nameof(FireRequest.Column))]
    [InlineData(0, 10, nameof(FireRequest.Column))]
    public void Fire_rejects_out_of_bounds_coordinates(int row, int column, string property)
    {
        new FireRequestValidator().TestValidate(new FireRequest(row, column))
            .ShouldHaveValidationErrorFor(property);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(9, 9)]
    public void Fire_accepts_grid_corners(int row, int column)
    {
        new FireRequestValidator().TestValidate(new FireRequest(row, column))
            .ShouldNotHaveAnyValidationErrors();
    }
}
