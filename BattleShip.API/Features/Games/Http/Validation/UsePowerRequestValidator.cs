using BattleShip.Models.Contracts;
using BattleShip.Models.Engine;
using BattleShip.Models.Enums;
using FluentValidation;

namespace BattleShip.API.Features.Games.Http.Validation;

public sealed class UsePowerRequestValidator : AbstractValidator<UsePowerRequest>
{
    public UsePowerRequestValidator()
    {
        RuleFor(request => request.Action).IsInEnum().NotEqual(GameAction.NormalShot)
            .WithMessage("Choisissez un pouvoir : mine, carré, ligne ou colonne.");
        RuleFor(request => request.Row).InclusiveBetween(0, Board.Size - 1);
        RuleFor(request => request.Column).InclusiveBetween(0, Board.Size - 1);
        When(request => request.Action == GameAction.SquareStrike, () =>
        {
            RuleFor(request => request.Row).LessThan(Board.Size - 1)
                .WithMessage("Le carré 2 × 2 ne peut pas commencer sur la dernière ligne.");
            RuleFor(request => request.Column).LessThan(Board.Size - 1)
                .WithMessage("Le carré 2 × 2 ne peut pas commencer sur la dernière colonne.");
        });
    }
}
