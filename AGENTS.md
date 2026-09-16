# Consignes du projet

## Environnement et architecture

- Solution : `BattleShip.slnx`, .NET 10 et C# 14.
- SDK sélectionné par `global.json` ; paramètres C# partagés dans `Directory.Build.props`.
- `BattleShip.Models` reste indépendant de HTTP, gRPC et des frameworks de validation.
- `BattleShip.API` et `BattleShip.App` référencent `BattleShip.Models`.
- `BattleShip.Tests` utilise xUnit et référence les modèles ainsi que l’API.

## Vérification

- Restauration : `dotnet restore BattleShip.slnx`.
- Compilation : `dotnet build BattleShip.slnx --configuration Release --no-restore`.
- Tests : `dotnet test BattleShip.Tests/BattleShip.Tests.csproj --configuration Release --no-build`.
- À l’étape d’initialisation, aucun test métier n’est encore implémenté ; une découverte vide ne constitue pas une validation fonctionnelle.

## Exécution locale

- API : `dotnet run --project BattleShip.API --launch-profile http` (port 5247).
- Blazor : `dotnet run --project BattleShip.App --launch-profile http` (port 5221).

## Progression

- Procéder étape par étape et fournir la commande de commit à l’utilisateur sans l’exécuter.
- Ne pas inventer de hashs de commits pour les livrables de traçabilité.
