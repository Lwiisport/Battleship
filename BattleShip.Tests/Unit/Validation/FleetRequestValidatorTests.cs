using BattleShip.API.Features.Games.Http.Validation;
using BattleShip.Models.Contracts;
using BattleShip.Models.Enums;
using FluentValidation.TestHelper;

namespace BattleShip.Tests.Unit.Validation;

public sealed class FleetRequestValidatorTests
{
    [Theory]
    [InlineData(ShipKind.AircraftCarrier)]
    [InlineData(ShipKind.Cruiser)]
    [InlineData(ShipKind.Destroyer)]
    [InlineData(ShipKind.Submarine)]
    [InlineData(ShipKind.PatrolBoat)]
    public void PlaceShip_accepts_every_fleet_ship(ShipKind kind)
    {
        new PlaceShipRequestValidator().TestValidate(new PlaceShipRequest(kind, 0, 0))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void PlaceShip_rejects_an_unknown_ship()
    {
        new PlaceShipRequestValidator().TestValidate(new PlaceShipRequest((ShipKind)99, 0, 0))
            .ShouldHaveValidationErrorFor(request => request.Ship);
    }

    [Theory]
    [InlineData(-1, 0, nameof(PlaceShipRequest.Row))]
    [InlineData(10, 0, nameof(PlaceShipRequest.Row))]
    [InlineData(0, -1, nameof(PlaceShipRequest.Column))]
    [InlineData(0, 10, nameof(PlaceShipRequest.Column))]
    public void PlaceShip_rejects_out_of_bounds_prows(int row, int column, string property)
    {
        new PlaceShipRequestValidator().TestValidate(new PlaceShipRequest(ShipKind.Cruiser, row, column))
            .ShouldHaveValidationErrorFor(property);
    }

    [Theory]
    [InlineData(ShipKind.AircraftCarrier)]
    [InlineData(ShipKind.PatrolBoat)]
    public void RemoveShip_accepts_fleet_ships(ShipKind kind)
    {
        new RemoveShipRequestValidator().TestValidate(new RemoveShipRequest(kind))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void RemoveShip_rejects_an_unknown_ship()
    {
        new RemoveShipRequestValidator().TestValidate(new RemoveShipRequest((ShipKind)42))
            .ShouldHaveValidationErrorFor(request => request.Ship);
    }
}
