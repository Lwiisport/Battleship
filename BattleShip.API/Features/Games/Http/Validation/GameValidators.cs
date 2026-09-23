using BattleShip.Models.Contracts;
using BattleShip.Models.Engine;
using FluentValidation;

namespace BattleShip.API.Features.Games.Http.Validation;

public sealed class CreateGameRequestValidator : AbstractValidator<CreateGameRequest>
{
    public CreateGameRequestValidator()
    {
        RuleFor(request => request.PlayerName)
            .NotEmpty().WithMessage("Le nom du joueur est obligatoire.")
            .MaximumLength(40).WithMessage("Le nom ne doit pas dépasser 40 caractères.");
        RuleFor(request => request.Difficulty).IsInEnum()
            .WithMessage("La difficulté doit valoir Easy, Normal ou Hard.");
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

public sealed class PlaceShipRequestValidator : AbstractValidator<PlaceShipRequest>
{
    public PlaceShipRequestValidator()
    {
        RuleFor(request => request.Ship).IsInEnum()
            .WithMessage("Choisissez un navire de la flotte.");
        RuleFor(request => request.Row).InclusiveBetween(0, Board.Size - 1)
            .WithMessage("La ligne doit être comprise entre 0 et 9.");
        RuleFor(request => request.Column).InclusiveBetween(0, Board.Size - 1)
            .WithMessage("La colonne doit être comprise entre 0 et 9.");
    }
}

public sealed class RemoveShipRequestValidator : AbstractValidator<RemoveShipRequest>
{
    public RemoveShipRequestValidator()
    {
        RuleFor(request => request.Ship).IsInEnum()
            .WithMessage("Choisissez un navire de la flotte.");
    }
}
