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

Saisir un nom, choisir la difficulté de l’adversaire, créer une partie et cliquer sur une case adverse. L’ordinateur répond automatiquement. Chaque tir normal accepté rapporte un point de compétence, dépensable en pouvoirs (mine, carré 2 × 2, ligne ou colonne entière). Les boutons **Actualiser HTTP** et **Actualiser gRPC-Web** permettent de lire le même état par les deux transports.

Le lien `/?game=<id>` conserve l’identifiant courant : recharger la page reprend la partie tant qu’elle existe. Ce lien donne accès à la partie ; ne pas le partager si elle doit rester privée.

## Historique des parties et des tirs

- **Historique des tirs** affiche chaque tour accepté, les coordonnées et le résultat des tirs du joueur et de l’ordinateur, du plus récent au plus ancien. Le résultat est celui observé au moment du tir ; un impact ancien ne devient pas rétroactivement « Coulé » dans le journal.
- **Historique des parties** conserve les **50 dernières parties** dans le navigateur, avec la date de création, le nom, le dernier résultat connu et le nombre de tours. La sauvegarde est actualisée après création, tir ou lecture HTTP/gRPC réussie, sans doublon de partie.
- **Consulter** ouvre le journal archivé sans remplacer la partie en cours et sans appeler l’API.
- **Reprendre** récupère l’état actuel du serveur avant de permettre de nouveaux tirs. Une partie expirée ou supprimée au redémarrage de l’API ne peut plus être reprise, mais son archive reste consultable.

Les archives utilisent `localStorage`, sous la clé `battleship.history.v1`. Elles survivent au rechargement et à la fermeture du navigateur, sous réserve de ses règles de stockage. Elles sont propres au navigateur et à l’origine utilisée (protocole, hôte et port), non synchronisées entre appareils, et accessibles aux personnes partageant ce profil de navigateur. Changer d’origine, utiliser une fenêtre privée ou effacer les données du site peut masquer ou supprimer cet historique.

La consultation sans connexion à l’API suppose que l’application Blazor soit déjà chargée : ce n’est pas une installation PWA entièrement hors ligne. Les archives contiennent seulement les instantanés masqués reçus par le joueur ; aucune liste globale des parties d’autres utilisateurs n’est exposée. Le résultat d’une partie non terminée reste son **dernier état connu**, pas une vérification en direct du serveur.

Si le stockage est plein, interdit ou illisible, un avertissement s’affiche et le jeu continue avec un historique temporaire en mémoire. Un contenu corrompu n’est pas écrasé automatiquement. Les parties antérieures à cette fonctionnalité ne peuvent pas être reconstituées si elles ont déjà disparu du serveur.

## Pouvoirs et points de compétence

- Chaque **tir normal accepté** rapporte **1 point**, jusqu’à **10 maximum**. Les pouvoirs ne rapportent aucun point.
- Les pouvoirs indisponibles ou trop coûteux sont refusés **sans dépenser de points ni consommer de tour** : l’ordinateur ne répond pas et l’historique n’est pas modifié.
- **Mine (2 points)** : posée sur une case non visée de sa propre grille, avec ou sans navire. Elle consomme le tour. Si l’ordinateur tire dessus, son tir est appliqué normalement **et** un tir est renvoyé sur la même coordonnée de sa grille. Si ce renvoi coule les deux dernières cases en jeu, la partie est un **match nul**.
- **Carré 2 × 2 (4 points)** : la case choisie est le coin supérieur gauche (A1 à I9). Seules les cases encore non visées sont touchées ; une zone entièrement déjà jouée est refusée.
- **Ligne ou colonne (6 points)** : vise respectivement toute la ligne ou toute la colonne de la case choisie, avec la même règle sur les cases déjà visées.
- Sur la grille cible, le **survol** et le **focus clavier** prévisualisent les cases concernées : zone disponible, zone invalide ou cases déjà jouées. La prévisualisation ne révèle jamais les navires adverses cachés.
- Les mines armées apparaissent sur la grille du joueur ; les détonations et les tirs renvoyés figurent dans l’historique des tours et dans les archives locales.

## Difficulté de l’adversaire

La difficulté est choisie à la création de la partie et affichée dans l’en-tête ainsi que dans l’historique des parties.

| Niveau | Comportement |
| --- | --- |
| **Facile** | Tirs purement aléatoires, sans tenir compte des résultats (comportement historique du jeu). |
| **Normal** | Joue comme un humain : après une case touchée, il vise ses voisines et prolonge les alignements, mais environ un tir sur cinq retombe au hasard — il fait des erreurs. |
| **Difficile** | Recalcule à chaque tour la case la plus probable : il énumère tous les placements valides des navires restants compatibles avec les tirs observés et pondère fortement ceux qui couvrent les cases touchées. |

L’adversaire ne connaît que ce qu’un joueur réel verrait : la grille observée (`Unknown`, `Miss`, `Hit`, `Sunk`), jamais la position des navires. Les navires coulés sont retirés des tailles restantes dans le calcul probabiliste ; deux navires coulés accolés sont déduits par combinaison gloutonne des tailles. La difficulté se règle dans le formulaire de création (présélection **Normal**) ou via `CreateGameRequest.Difficulty` ; un champ absent conserve `Easy` pour compatibilité avec les clients existants.

## Fonctionnalités et règles

- Deux grilles de 10 × 10, coordonnées API indexées de **0 à 9** ; affichage de A1 à J10.
- Flotte standard : porte-avions 5, croiseur 4, contre-torpilleur 3, sous-marin 3, torpilleur 2.
- Placement rectiligne, contigu, aléatoire, sans chevauchement ni dépassement. Les navires peuvent se toucher : aucune interdiction de contact n’est imposée.
- Les navires du joueur sont visibles. Toute case adverse non visée reste `Unknown`, qu’elle contienne de l’eau ou un navire, même en fin de partie.
- Un tir révèle `Miss`, `Hit` ou `Sunk`. Lorsqu’un navire est coulé, toutes ses cases déjà touchées deviennent `Sunk`.
- Un tir hors grille, répété, ou effectué après la fin de partie est refusé sans mutation.
- Un tour accepté comprend l’action du joueur puis le tir de l’ordinateur, sauf victoire immédiate ou match nul.
- L’ordinateur ne consulte jamais la position des navires pour choisir ses cibles : en facile il consomme une permutation des coordonnées, en normal et difficile il exploite uniquement les résultats de tirs observés.
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

Le fichier unique `BattleShip.API/Features/Games/Grpc/Protos/game.proto` est compilé côté serveur et lié comme source côté Blazor. L’application ne référence pas l’assembly serveur. Les messages générés restent hors du domaine ; les fichiers générés dans `obj/` ne sont pas versionnés.

Décision détaillée : [ADR-001](docs/adr/ADR-001-ARCHITECTURE-GRPC-HTTP.md).

### Organisation des dossiers

```text
BattleShip.Models/
├── Engine/                    Board, Game, Ship, Position, ShipSpecification, PowerRules, ComputerOpponent
├── Contracts/                 Requêtes et DTO de réponse
├── Enums/                     États, types de navire et résultats
└── Exceptions/                Erreurs métier

BattleShip.API/
└── Features/Games/
    ├── Http/                  Endpoints Minimal API
    │   └── Validation/        Validators des requêtes HTTP
    ├── Grpc/                  Service gRPC-Web
    │   ├── Protos/            Contrat game.proto
    │   └── Validation/        Validator de la requête gRPC
    └── Storage/               Stockage mémoire et options d’expiration

BattleShip.App/
├── Features/
│   ├── Games/
│   │   ├── Pages/             Page de jeu Home
│   │   ├── Components/        Grille interactive et commandes de pouvoirs
│   │   └── Clients/           Clients HTTP et gRPC-Web
│   └── History/
│       ├── Components/        Liste des parties et journal des tirs
│       └── Services/          Archivage local et sérialisation
├── Shared/
│   ├── Layout/                Mise en page commune
│   └── Pages/                 Page introuvable
└── wwwroot/                   Ressources statiques et styles communs

BattleShip.Tests/
├── Infrastructure/            Fabrique de serveur et source client liée
├── Unit/
│   ├── Engine/                Placement, masquage, tours, pouvoirs, difficultés et concurrence
│   ├── Storage/               Capacité et expiration du stockage API
│   ├── Validation/            Validators HTTP et gRPC sans serveur
│   └── History/               Archivage local via un faux IJSRuntime
└── Integration/
    ├── Http/                  Endpoints, pouvoirs, erreurs, CORS et OpenAPI
    └── Grpc/                  Transport gRPC-Web et équivalence des états
```

Les namespaces suivent ces dossiers. Les points d’entrée (`Program.cs`, `App.razor`), les fichiers projet et les configurations standards restent à leur emplacement habituel. Les routes HTTP, le package Protobuf `battleship.v1` et le format des archives locales ne changent pas avec cette organisation.

Pour exécuter seulement une catégorie de tests après compilation :

```bash
dotnet test BattleShip.Tests/BattleShip.Tests.csproj --configuration Release --no-build --filter 'FullyQualifiedName~BattleShip.Tests.Unit.'
dotnet test BattleShip.Tests/BattleShip.Tests.csproj --configuration Release --no-build --filter 'FullyQualifiedName~BattleShip.Tests.Integration.'
```

## Contrats HTTP

| Méthode | Chemin | Réponse normale | Principaux refus |
| --- | --- | --- | --- |
| POST | `/api/games` | `201 Created`, `Location`, `GameStateDto` | 400 validation, 503 capacité atteinte |
| GET | `/api/games/{id}` | `200 OK`, `GameStateDto` | 404 inconnue ou expirée |
| POST | `/api/games/{id}/fire` | `200 OK`, état après le tour complet | 400 validation, 404 inconnue, 409 tir répété ou partie terminée |
| POST | `/api/games/{id}/powers` | `200 OK`, état après le tour complet | 400 validation, 404 inconnue, 409 pouvoir invalide, trop coûteux ou sans nouvelle cible |

Une route avec un identifiant non convertible en UUID ne correspond pas à la contrainte `{id:guid}` et renvoie 404. Les erreurs sont structurées avec Problem Details ; les erreurs FluentValidation contiennent un dictionnaire `errors`. Un corps JSON absent, mal formé ou privé d’un paramètre obligatoire renvoie 400, jamais un tir implicite en `(0, 0)`.

Création :

```bash
curl -i http://localhost:5247/api/games \
  -H 'Content-Type: application/json' \
  -d '{"playerName":"Alice","difficulty":"Normal"}'
```

Remplacer la valeur suivante par l’`id` reçu :

```bash
GAME_ID="identifiant-retourne-par-la-creation"
curl "http://localhost:5247/api/games/$GAME_ID"
curl -i "http://localhost:5247/api/games/$GAME_ID/fire" \
  -H 'Content-Type: application/json' \
  -d '{"row":0,"column":0}'
curl -i "http://localhost:5247/api/games/$GAME_ID/powers" \
  -H 'Content-Type: application/json' \
  -d '{"action":"SquareStrike","row":0,"column":0}'
```

`GameStateDto` contient `id`, `playerName`, `status`, `difficulty`, `turnNumber`, `createdAtUtc`, `skillPoints`, deux tableaux de 100 cellules, les derniers tirs et `turns`. Chaque `TurnDto` contient `number`, `action`, `target`, `playerShot`, `playerShots` (tirs de zone éventuels), `computerShot` (nul si le joueur vient de gagner), `mineDetonation` et `skillPointsAfter`. Les actions refusées ne sont pas ajoutées à ce journal. Une cellule contient `row`, `column`, `state` et `hasMine` (visible uniquement sur la grille du joueur). La réponse n’expose ni objet `Board`, ni liste de positions des navires adverses. Les enums JSON sont des chaînes.

Un pouvoir se déclenche avec `UsePowerRequest { action, row, column }`, où `action` vaut `Mine`, `SquareStrike`, `RowStrike` ou `ColumnStrike` :

## Contrat gRPC-Web

- Service : `battleship.v1.GameService`.
- Méthode unaire : `GetGameStatus`.
- Requête : `GetGameStatusRequest { game_id }`.
- Réponse : `GameStatusReply`, avec grilles typées, phase (dont `DRAW`), difficulté (`DifficultyLevel`), numéro du tour, points de compétence, derniers tirs, date UTC de création (`Timestamp`) et historique complet (`TurnMessage` : action, cible, tirs de zone, détonation de mine et points après le tour).
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

À la livraison : **169 cas xUnit réussis**, aucun test ignoré, compilation sans avertissement ni erreur et aucune vulnérabilité connue signalée par les sources NuGet consultées. Ce dernier résultat dépend de la date et du catalogue d’avis disponibles, et n’est pas une garantie d’absence de vulnérabilité.

Couverture fonctionnelle :

- 200 placements déterministes, tailles, contiguïté et absence de chevauchement.
- Masquage initial et après impacts, refus sans mutation, impossibilité de modifier l’état via les tableaux DTO.
- Victoire, défaite, absence de réponse après le tir gagnant, cibles uniques et concurrence.
- Validators HTTP/gRPC, champs JSON manquants, erreurs de transport, CORS et OpenAPI.
- Capacité et expiration avec horloge contrôlée.
- Équivalence des réponses HTTP et gRPC-Web, modes binaire et texte, dates et historique complet des tours.
- Pouvoirs : gains et plafond de points, coûts, refus sans mutation, zones 2 × 2 et ligne/colonne, pose et détonation de mine, tir renvoyé, match nul et validator `UsePowerRequest`.
- Difficultés : file mélangée en facile, poursuite des touches avec erreurs en normal, densité de probabilité en difficile (voisinage des touches, prolongement des lignes, navires coulés retirés), comparaison de vitesse de destruction et validation de la valeur reçue.
- Archivage local : restauration, déduplication, protection contre les instantanés plus anciens, borne de 50 parties, archives invalides ou avec points impossibles, compatibilité des archives antérieures aux pouvoirs et à la difficulté, et reprise après quota dépassé.

Le service client `Features/History/Services/GameHistoryStore.cs` est lié comme source sous `Infrastructure/Client` dans les tests pour vérifier son implémentation réelle sans charger l’application WebAssembly ni dupliquer les types Protobuf. Un faux `IJSRuntime` simule le stockage ; aucune dépendance NuGet supplémentaire n’est nécessaire.

Un parcours Chrome automatisé a également été exécuté sur le serveur de développement puis sur les fichiers publiés : création, tir, actualisations par les deux transports, rechargement, affichage à 375 px, réponse de tir perdue, resynchronisation, fin de partie et redémarrage. Un second parcours couvre les pouvoirs : jauge de points, activation des boutons, prévisualisation des zones au survol et au focus clavier sans révéler les navires, carré 2 × 2, pose de mine, ligne entière et conservation de l’historique après rechargement. Un troisième vérifie le choix de difficulté : trois options, présélection Normal, libellé dans l’en-tête et création en difficile. Ces contrôles navigateur sont externes au dépôt et ne sont pas inclus dans les tests xUnit.

Le parcours de l’historique a également été vérifié dans Chrome, en développement et sur publication : plusieurs parties, consultation sans remplacer la partie active, reprise, rechargement, API inaccessible, réponse 404 simulant une partie expirée, récupération d’un tir accepté dont la réponse a été perdue, stockage plein puis rétabli et contenu local corrompu. Aucun échec navigateur non géré n’a été observé.

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

Les parties jouables côté serveur sont **en mémoire, non persistées et propres à une instance**. Redémarrer l’API les supprime, sans supprimer les archives locales consultables dans le navigateur ; plusieurs instances nécessiteraient un stockage partagé et une stratégie de concurrence distribuée. Le GUID aléatoire sert de lien d’accès, pas d’authentification utilisateur. Toute personne qui le connaît peut lire ou jouer cette partie. Ajouter authentification et autorisation avant d’exiger une séparation forte entre comptes.

## Historique et livrables

- [PROMPTS.md](PROMPTS.md) : étapes, références d’instantanés et commandes des sept commits atomiques.
- [REVUE-IA.md](REVUE-IA.md) : trois revues argumentées, décisions et preuves.
- [ADR-001](docs/adr/ADR-001-ARCHITECTURE-GRPC-HTTP.md) : choix de l’architecture mixte.

L’historique initial est désormais présent dans Git ; `git log --oneline` permet de consulter les commits réels. Les références locales `refs/snapshots/battleship/step-1` à `step-7` sont les anciens instantanés de préparation, de type **tree**, et ne sont pas nécessaires pour utiliser le dépôt cloné. Les commandes de matérialisation conservées dans `PROMPTS.md` sont historiques : ne pas les rejouer sur cette branche. Les anciens chemins cités dans les revues correspondent au code des instantanés examinés ; l’arborescence ci-dessus décrit les fichiers actuels.
