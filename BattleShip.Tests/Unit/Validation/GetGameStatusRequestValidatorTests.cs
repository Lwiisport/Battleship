using BattleShip.API.Features.Games.Grpc.Validation;
using BattleShip.Grpc;
using FluentValidation.TestHelper;

namespace BattleShip.Tests.Unit.Validation;

public sealed class GetGameStatusRequestValidatorTests
{
    [Theory]
    [InlineData("")]
    [InlineData("wrong")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Grpc_validator_rejects_invalid_identifiers(string id)
    {
        new GetGameStatusRequestValidator().TestValidate(new GetGameStatusRequest { GameId = id })
            .ShouldHaveValidationErrorFor(request => request.GameId);
    }

    [Fact]
    public void Grpc_validator_accepts_valid_identifiers()
    {
        new GetGameStatusRequestValidator().TestValidate(new GetGameStatusRequest { GameId = Guid.NewGuid().ToString() })
            .ShouldNotHaveAnyValidationErrors();
    }
}
