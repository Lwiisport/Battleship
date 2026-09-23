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

- Suite actuelle : 169 cas xUnit, incluant les transports HTTP/gRPC-Web, les pouvoirs, les difficultés et l’archive locale ; aucun test navigateur n’est inclus dans ce nombre.
- Audit : `dotnet list BattleShip.slnx package --vulnerable --include-transitive`.
- Publication : `dotnet publish BattleShip.API/BattleShip.API.csproj --configuration Release` puis `dotnet publish BattleShip.App/BattleShip.App.csproj --configuration Release`.
- Le workload `wasm-tools` est optionnel ; l’application publiée a été vérifiée dans Chrome sans celui-ci.
- Le client utilise `BattleShip.App/wwwroot/appsettings.json` pour `ApiBaseUrl` ; cette configuration est publique.
- Le namespace Protobuf `BattleShip.Grpc` nécessite `global::Grpc.Core` dans les imports Razor concernés.
- En Development, configurer `RouteHandlerOptions.ThrowOnBadRequest = false` évite que le gestionnaire général d’exceptions transforme les erreurs de liaison JSON en 500.
- Les arbres locaux `refs/snapshots/battleship/step-N` sont des instantanés historiques antérieurs aux commits initiaux. Ne pas rejouer les anciennes commandes de matérialisation sur une branche déjà committée.
- Le stock de parties est borné, non persistant et mono-instance ; l’identifiant de partie sert de lien d’accès, pas d’authentification.
- `GameStateDto.CreatedAtUtc` et `Turns` sont partagés par HTTP et gRPC-Web ; un tir refusé ne doit jamais modifier le journal des tours.
- `GameHistoryStore` conserve les 50 dernières parties dans `localStorage` (`battleship.history.v1`), sans exposer une liste globale côté API. Les archives ne permettent pas de reprendre une partie disparue du serveur.
- Le service client d’archive est lié comme source dans `BattleShip.Tests` pour tester le stockage via un faux `IJSRuntime`, sans référence au projet WebAssembly.
- L’archive utilise un contexte System.Text.Json généré pour rester compatible avec la publication Blazor optimisée en taille. Un échec de stockage doit produire un avertissement, pas invalider un tour accepté.
- Les archives sont propres à l’origine et au profil de navigateur. Leur consultation reste possible sans API une fois l’application chargée, mais le site n’est pas une PWA hors ligne.
- Pouvoirs : seul un tir normal accepté rapporte 1 point (plafond `PowerRules.MaxSkillPoints` = 10). Coûts : mine 2, carré 2 × 2 = 4, ligne/colonne = 6. Un pouvoir consomme le tour sans rapporter de point ; un pouvoir refusé (invalide, trop coûteux ou sans nouvelle cible) ne doit ni dépenser de points ni modifier le journal.
- La mine se pose sur la grille du joueur (`CellDto.HasMine`), ne protège pas la case : le tir adverse est appliqué puis renvoyé sur la même coordonnée ; un double naufrage donne `GameStatus.Draw`.
- Le carré 2 × 2 utilise la case choisie comme coin supérieur gauche ; les zones ne touchent que les cases non visées. La géométrie est définie dans `PowerRules.Targets`, partagée par le moteur et la prévisualisation Blazor.
- Endpoint des pouvoirs : `POST /api/games/{id}/powers` avec `UsePowerRequest` ; le contrat gRPC étend `TurnMessage`/`GameStatusReply` par champs ajoutés (compatibilité proto3).
- Difficulté (`Difficulty` : `Easy`/`Normal`/`Hard`) choisie à la création via `CreateGameRequest` ; `ComputerOpponent` ne reçoit que la grille observée (`ToGrid(false)`), jamais `Board` ni les positions réelles. Facile = file pré-mélangée (conserver la séquence du constructeur : les tests reproduisent la consommation du `Random`), normal = poursuite des touches avec ~20 % d’erreurs, difficile = densité de probabilité des placements restants. Absent du JSON : `Easy` (compatibilité) ; l’interface présélectionne `Normal`.

## Organisation des sources

- Les namespaces suivent les dossiers ; placer les nouveaux fichiers avec leur fonctionnalité, pas à la racine du projet.
- Models : `Engine`, `Contracts`, `Enums`, `Exceptions`.
- API : `Features/Games/Http`, `Features/Games/Grpc`, `Features/Games/Storage`, avec les validators proches du transport concerné.
- App : `Features/Games` (pages, composants, clients), `Features/History` (composants, services), `Shared` (layout et pages communes).
- Tests : `Unit/Engine`, `Unit/Storage`, `Unit/Validation`, `Unit/History`, `Integration/Http`, `Integration/Grpc`, `Infrastructure`.
- Les tests de validators sont unitaires ; réserver `Integration` aux appels traversant le serveur HTTP ou gRPC-Web.
- Filtrer les catégories avec `--filter 'FullyQualifiedName~BattleShip.Tests.Unit.'` ou `--filter 'FullyQualifiedName~BattleShip.Tests.Integration.'`.
- Contrat Protobuf partagé : `BattleShip.API/Features/Games/Grpc/Protos/game.proto`.
- Source client liée aux tests : `BattleShip.App/Features/History/Services/GameHistoryStore.cs`, affichée sous `Infrastructure/Client`.
