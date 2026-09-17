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

## Vérifications complémentaires

- Suite actuelle : 81 cas xUnit, incluant les transports HTTP/gRPC-Web et l’archive locale ; aucun test navigateur n’est inclus dans ce nombre.
- Audit : `dotnet list BattleShip.slnx package --vulnerable --include-transitive`.
- Publication : `dotnet publish BattleShip.API/BattleShip.API.csproj --configuration Release` puis `dotnet publish BattleShip.App/BattleShip.App.csproj --configuration Release`.
- Le workload `wasm-tools` est optionnel ; l’application publiée a été vérifiée dans Chrome sans celui-ci.
- Le client utilise `BattleShip.App/wwwroot/appsettings.json` pour `ApiBaseUrl` ; cette configuration est publique.
- Le namespace Protobuf `BattleShip.Grpc` nécessite `global::Grpc.Core` dans les imports Razor concernés.
- En Development, configurer `RouteHandlerOptions.ThrowOnBadRequest = false` évite que le gestionnaire général d’exceptions transforme les erreurs de liaison JSON en 500.
- Les arbres locaux `refs/snapshots/battleship/step-N` préservent les étapes sans commits ; les commandes de matérialisation figurent dans `PROMPTS.md`.
- Le stock de parties est borné, non persistant et mono-instance ; l’identifiant de partie sert de lien d’accès, pas d’authentification.
- `GameStateDto.CreatedAtUtc` et `Turns` sont partagés par HTTP et gRPC-Web ; un tir refusé ne doit jamais modifier le journal des tours.
- `GameHistoryStore` conserve les 50 dernières parties dans `localStorage` (`battleship.history.v1`), sans exposer une liste globale côté API. Les archives ne permettent pas de reprendre une partie disparue du serveur.
- Le service client d’archive est lié comme source dans `BattleShip.Tests` pour tester le stockage via un faux `IJSRuntime`, sans référence au projet WebAssembly.
- L’archive utilise un contexte System.Text.Json généré pour rester compatible avec la publication Blazor optimisée en taille. Un échec de stockage doit produire un avertissement, pas invalider un tour accepté.
- Les archives sont propres à l’origine et au profil de navigateur. Leur consultation reste possible sans API une fois l’application chargée, mais le site n’est pas une PWA hors ligne.
