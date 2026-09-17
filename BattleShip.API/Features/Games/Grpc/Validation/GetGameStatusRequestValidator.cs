using BattleShip.Grpc;
using FluentValidation;

namespace BattleShip.API.Features.Games.Grpc.Validation;

public sealed class GetGameStatusRequestValidator : AbstractValidator<GetGameStatusRequest>
{
    public GetGameStatusRequestValidator()
    {
        RuleFor(request => request.GameId)
            .Must(value => Guid.TryParse(value, out var id) && id != Guid.Empty)
            .WithMessage("L’identifiant de partie doit être un UUID valide et non vide.");
    }
}
