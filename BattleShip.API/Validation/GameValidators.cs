using BattleShip.Models;
using FluentValidation;

namespace BattleShip.API.Validation;

public sealed class CreateGameRequestValidator : AbstractValidator<CreateGameRequest>
{
    public CreateGameRequestValidator()
    {
        RuleFor(request => request.PlayerName)
            .NotEmpty().WithMessage("Le nom du joueur est obligatoire.")
            .MaximumLength(40).WithMessage("Le nom ne doit pas dépasser 40 caractères.");
    }
}

public sealed class FireRequestValidator : AbstractValidator<FireRequest>
{
    public FireRequestValidator()
    {
        RuleFor(request => request.Row).InclusiveBetween(0, Board.Size - 1)
            .WithMessage("La ligne doit être comprise entre 0 et 9.");
        RuleFor(request => request.Column).InclusiveBetween(0, Board.Size - 1)
            .WithMessage("La colonne doit être comprise entre 0 et 9.");
    }
}
