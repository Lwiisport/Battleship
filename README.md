# Bataille navale

Jeu solo contre ordinateur, développé en **.NET 10 / C# 14** : moteur indépendant, ASP.NET Core Minimal API, gRPC-Web et interface Blazor WebAssembly en français.

## Prérequis

- SDK **.NET 10.0.111** ou correctif ultérieur de la bande `10.0.1xx`, conformément à `global.json`.
- Navigateur récent prenant en charge WebAssembly.
- Git pour l’historique ; accès à NuGet lors de la première restauration.
- Aucun serveur de base de données, Node.js ou workload WebAssembly requis pour compiler et jouer.

`wasm-tools` reste optionnel pour les optimisations natives de publication. Sans ce workload, la publication fonctionne mais affiche une recommandation d’installation.

## Démarrage rapide

Exécuter les commandes à la racine du dépôt :

```bash
dotnet --version
dotnet restore BattleShip.slnx
dotnet build BattleShip.slnx --configuration Release --no-restore
```

Dans un premier terminal :

```bash
dotnet run --project BattleShip.API --configuration Release --no-build --launch-profile http
```

Dans un second terminal :

```bash
dotnet run --project BattleShip.App --configuration Release --no-build --launch-profile http
```

- Interface : <http://localhost:5221/>
- API : <http://localhost:5247/api/games>
- OpenAPI, en environnement Development : <http://localhost:5247/openapi/v1.json>

Saisir un nom, créer une partie et cliquer sur une case adverse. L’ordinateur répond automatiquement. Les boutons **Actualiser HTTP** et **Actualiser gRPC-Web** permettent de lire le même état par les deux transports.

Le lien `/?game=<id>` conserve l’identifiant courant : recharger la page reprend la partie tant qu’elle existe. Ce lien donne accès à la partie ; ne pas le partager si elle doit rester privée.

## Fonctionnalités et règles

- Deux grilles de 10 × 10, coordonnées API indexées de **0 à 9** ; affichage de A1 à J10.
- Flotte standard : porte-avions 5, croiseur 4, contre-torpilleur 3, sous-marin 3, torpilleur 2.
- Placement rectiligne, contigu, aléatoire, sans chevauchement ni dépassement. Les navires peuvent se toucher : aucune interdiction de contact n’est imposée.
- Les navires du joueur sont visibles. Toute case adverse non visée reste `Unknown`, qu’elle contienne de l’eau ou un navire, même en fin de partie.
- Un tir révèle `Miss`, `Hit` ou `Sunk`. Lorsqu’un navire est coulé, toutes ses cases déjà touchées deviennent `Sunk`.
- Un tir hors grille, répété, ou effectué après la fin de partie est refusé sans mutation.
- Un tour accepté comprend le tir du joueur puis celui de l’ordinateur, sauf victoire immédiate du joueur.
- L’ordinateur mélange les 100 coordonnées puis les consomme sans répétition. Il ne consulte pas la position des navires pour choisir ses cibles.
- Un verrou par partie protège le tour complet et les instantanés de lecture.
- Interface responsive, boutons utilisables au clavier, libellés de coordonnées, annonces de statut et erreurs lisibles.
- Après une réponse réseau incertaine à un tir, les tirs sont bloqués jusqu’à une actualisation réussie. Aucun rejeu automatique de POST.

## Architecture

```text
BattleShip.App ── HTTP / gRPC-Web ──> BattleShip.API
      │                                  │
      └──────────> BattleShip.Models <────┘
                         ^                ^
                         └── BattleShip.Tests
```

| Projet | Responsabilités |
| --- | --- |
| `BattleShip.Models` | `Board`, `Ship`, `Game`, règles, enums, DTO ; aucune dépendance NuGet ou transport. |
| `BattleShip.API` | Endpoints typés, validators FluentValidation, service gRPC, stockage des parties, CORS, OpenAPI et limitation de requêtes. |
| `BattleShip.App` | Interface, client HTTP, canal gRPC-Web et conversion des messages Protobuf en DTO. |
| `BattleShip.Tests` | xUnit, tests déterministes, `WebApplicationFactory` et appels gRPC-Web réels sur TestServer. |

Le fichier unique `BattleShip.API/Protos/game.proto` est compilé côté serveur et lié comme source côté Blazor. L’application ne référence pas l’assembly serveur. Les messages générés restent hors du domaine ; les fichiers générés dans `obj/` ne sont pas versionnés.

Décision détaillée : [ADR-001](docs/adr/ADR-001-ARCHITECTURE-GRPC-HTTP.md).

## Contrats HTTP

| Méthode | Chemin | Réponse normale | Principaux refus |
| --- | --- | --- | --- |
| POST | `/api/games` | `201 Created`, `Location`, `GameStateDto` | 400 validation, 503 capacité atteinte |
| GET | `/api/games/{id}` | `200 OK`, `GameStateDto` | 404 inconnue ou expirée |
| POST | `/api/games/{id}/fire` | `200 OK`, état après le tour complet | 400 validation, 404 inconnue, 409 tir répété ou partie terminée |

Une route avec un identifiant non convertible en UUID ne correspond pas à la contrainte `{id:guid}` et renvoie 404. Les erreurs sont structurées avec Problem Details ; les erreurs FluentValidation contiennent un dictionnaire `errors`. Un corps JSON absent, mal formé ou privé d’un paramètre obligatoire renvoie 400, jamais un tir implicite en `(0, 0)`.

Création :

```bash
curl -i http://localhost:5247/api/games \
  -H 'Content-Type: application/json' \
  -d '{"playerName":"Alice"}'
```

Remplacer la valeur suivante par l’`id` reçu :

```bash
GAME_ID="identifiant-retourne-par-la-creation"
curl "http://localhost:5247/api/games/$GAME_ID"
curl -i "http://localhost:5247/api/games/$GAME_ID/fire" \
  -H 'Content-Type: application/json' \
  -d '{"row":0,"column":0}'
```

`GameStateDto` contient `id`, `playerName`, `status`, `turnNumber`, deux tableaux de 100 cellules et les derniers tirs. Une cellule contient uniquement `row`, `column` et `state`. La réponse n’expose ni objet `Board`, ni liste de positions des navires adverses. Les enums JSON sont des chaînes.

## Contrat gRPC-Web

- Service : `battleship.v1.GameService`.
- Méthode unaire : `GetGameStatus`.
- Requête : `GetGameStatusRequest { game_id }`.
- Réponse : `GameStatusReply`, avec grilles typées, phase, numéro du tour et derniers tirs.
- UUID invalide ou vide : `RpcException` / `InvalidArgument`.
- Partie inconnue ou expirée : `RpcException` / `NotFound`.

`UseGrpcWeb()` et `EnableGrpcWeb()` activent le protocole dans l’API. Le client emploie `GrpcChannel` et `GrpcWebHandler` en mode binaire. Les tests couvrent aussi le mode texte. Le serveur expose les en-têtes `Grpc-Status`, `Grpc-Message`, `Grpc-Encoding` et `Grpc-Accept-Encoding` via CORS.

Il n’y a pas de méthode de tir gRPC : les mutations passent par HTTP et la consultation supplémentaire passe par gRPC-Web.

## Configuration

### API

`BattleShip.API/appsettings.json` :

- `Cors:AllowedOrigins` : `http://localhost:5221` et `https://localhost:7009`, sans autorisation globale d’origine.
- `Games:Capacity` : 1 000 parties maximum.
- `Games:LifetimeMinutes` : expiration absolue après 120 minutes ; configurable de 1 à 1 440.

La capacité et la durée sont validées au démarrage. Les parties expirées sont supprimées lors d’une lecture correspondante ou avant une création ; aucun minuteur de nettoyage n’est nécessaire. Le stock reste borné même en l’absence d’activité.

Un limiteur global accepte **120 requêtes par minute et par adresse IP**, sans file d’attente, puis répond 429. Derrière un proxy, la clé utilise l’adresse observée par ASP.NET Core : configurer explicitement les proxys de confiance avant d’exploiter les en-têtes transférés. La limitation peut être partagée entre utilisateurs derrière une même adresse.

### Blazor

`BattleShip.App/wwwroot/appsettings.json` contient `ApiBaseUrl`, par défaut `http://localhost:5247/`. Il s’agit d’une configuration publique : ne jamais y mettre de secret.

Pour utiliser HTTPS localement, configurer le certificat de développement .NET pour le système, choisir `https://localhost:7268/` comme `ApiBaseUrl`, reconstruire puis lancer les deux projets avec `--launch-profile https`. Ne pas appeler une API HTTP depuis une page HTTPS : le navigateur bloque le contenu mixte.

## Vérifications

```bash
dotnet restore BattleShip.slnx
dotnet build BattleShip.slnx --configuration Release --no-restore
dotnet test BattleShip.Tests/BattleShip.Tests.csproj --configuration Release --no-build
dotnet list BattleShip.slnx package --vulnerable --include-transitive
```

À la livraison : **65 cas xUnit réussis**, aucun test ignoré, compilation sans avertissement ni erreur et aucune vulnérabilité connue signalée par les sources NuGet consultées. Ce dernier résultat dépend de la date et du catalogue d’avis disponibles, et n’est pas une garantie d’absence de vulnérabilité.

Couverture fonctionnelle :

- 200 placements déterministes, tailles, contiguïté et absence de chevauchement.
- Masquage initial et après impacts, refus sans mutation, impossibilité de modifier l’état via les tableaux DTO.
- Victoire, défaite, absence de réponse après le tir gagnant, cibles uniques et concurrence.
- Validators HTTP/gRPC, champs JSON manquants, erreurs de transport, CORS et OpenAPI.
- Capacité et expiration avec horloge contrôlée.
- Équivalence des réponses HTTP et gRPC-Web, modes binaire et texte.

Un parcours Chrome automatisé a également été exécuté sur le serveur de développement puis sur les fichiers publiés : création, tir, actualisations par les deux transports, rechargement, affichage à 375 px, réponse de tir perdue, resynchronisation, fin de partie et redémarrage. Ce contrôle navigateur était externe au dépôt et n’est pas inclus dans les 65 tests xUnit.

Pour le reproduire manuellement : effectuer ces actions dans cet ordre, vérifier qu’une case visée est désactivée, que la flotte adverse reste cachée et que les boutons de tir sont tous désactivés en fin de partie. Couper le réseau pendant un tir doit déclencher une erreur et imposer une actualisation avant de continuer.

## Publication et limites

```bash
dotnet publish BattleShip.API/BattleShip.API.csproj --configuration Release
dotnet publish BattleShip.App/BattleShip.App.csproj --configuration Release
```

Sorties :

- API : `BattleShip.API/bin/Release/net10.0/publish/`.
- Site statique : `BattleShip.App/bin/Release/net10.0/publish/wwwroot/`.

Héberger le site avec le type MIME WebAssembly approprié et un repli des routes client vers `index.html`. L’API nécessite le runtime ASP.NET Core 10 pour cette publication dépendante du framework. En déploiement public, utiliser HTTPS, des origines CORS explicites et une configuration adaptée au proxy.

Les parties sont **en mémoire, non persistées et propres à une instance**. Redémarrer l’API les supprime ; plusieurs instances nécessiteraient un stockage partagé et une stratégie de concurrence distribuée. Le GUID aléatoire sert de lien d’accès, pas d’authentification utilisateur. Toute personne qui le connaît peut lire ou jouer cette partie. Ajouter authentification et autorisation avant d’exiger une séparation forte entre comptes.

## Historique et livrables

- [PROMPTS.md](PROMPTS.md) : étapes, références d’instantanés et commandes des sept commits atomiques.
- [REVUE-IA.md](REVUE-IA.md) : trois revues argumentées, décisions et preuves.
- [ADR-001](docs/adr/ADR-001-ARCHITECTURE-GRPC-HTTP.md) : choix de l’architecture mixte.

À la rédaction, aucun commit n’avait encore été créé. Les étapes sont conservées sous `refs/snapshots/battleship/step-1` à `step-7`. Ce sont des objets **tree**, pas des commits. Les commandes de `PROMPTS.md` permettent de construire l’historique sans écraser les fichiers de travail ; les références documentaires utilisent les messages prévus et les identifiants réels des arbres, sans inventer de SHA de commit.
