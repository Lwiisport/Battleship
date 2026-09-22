# ADR-001 — Combiner Minimal API HTTP et gRPC-Web

- **Statut :** accepté pour cette version.
- **Date :** 2026-09-16.
- **Portée :** communication entre Blazor WebAssembly et l’API de bataille navale.
- **Références :** étapes 4, 5 et 6 du [journal de réalisation](../../PROMPTS.md).

## Contexte

L’application doit permettre de créer une partie, consulter son état et jouer un tir suivi de la réponse automatique de l’ordinateur. Le navigateur ne doit jamais recevoir la position des navires adverses non découverts. Le projet impose à la fois des endpoints Minimal API documentés avec OpenAPI et un service gRPC utilisable depuis Blazor WebAssembly.

Un navigateur ne fournit pas toutes les primitives HTTP/2 et de trailers nécessaires au protocole gRPC natif. Un appel gRPC-Web est donc distinct d’un simple appel gRPC HTTP/2 depuis un client .NET de bureau.

Les objectifs prioritaires sont la cohérence des règles, la clarté des contrats, la vérifiabilité et une exploitation locale simple. Aucun objectif chiffré de performance ni besoin de streaming n’a été démontré.

## Décision

### 1. Garder le domaine indépendant

`BattleShip.Models` ne référence ni ASP.NET Core, ni FluentValidation, ni les bibliothèques gRPC. Il possède les règles, les enums et les DTO communs. L’API et Blazor le référencent, mais seule l’API héberge l’instance autoritaire de chaque partie.

Un `Game` protège son état avec un verrou. La résolution du tir du joueur, l’éventuelle réponse de l’ordinateur et la création du DTO retourné forment une opération atomique. Les lectures passent par le même verrou.

### 2. Utiliser HTTP pour les opérations métier courantes

- `POST /api/games` crée une partie.
- `GET /api/games/{id}` lit l’état courant.
- `POST /api/games/{id}/fire` applique un tir normal puis le tour complet.
- `POST /api/games/{id}/powers` applique un pouvoir (mine, carré 2 × 2, ligne ou colonne) puis le tour complet.

Les handlers retournent des `TypedResults`. FluentValidation vérifie les DTO exploitables ; la liaison JSON refuse séparément les corps mal formés ou incomplets. Les erreurs HTTP restent conventionnelles : 400, 404, 409, 429 ou 503 selon le cas. OpenAPI est exposé en Development.

Ce choix facilite le diagnostic avec `curl`, la documentation et les tests `WebApplicationFactory`. Il ne suppose pas que HTTP soit intrinsèquement plus rapide que gRPC-Web.

### 3. Ajouter gRPC-Web pour une consultation typée

`GameService.GetGameStatus` reçoit un UUID et retourne un `GameStatusReply` Protobuf typé. Il lit le même `GameStore` et appelle le même `Game.GetState()` que le GET HTTP. Le contrat a été étendu sans rupture : champs numérotés ajoutés (`skill_points`, `has_mine`, `action`, `target`, `player_shots`, `mine_detonation`, `skill_points_after`, `DRAW`) plutôt que modification des existants, ce que proto3 tolère pour les anciens clients.

Le `.proto` unique est placé dans l’API et lié comme source au projet Blazor. Le code généré serveur inclut aussi le client utilisé par les tests ; Blazor génère son client sans référencer l’assembly API. Les messages Protobuf ne contaminent pas les modèles du domaine.

`app.UseGrpcWeb()` et `MapGrpcService<...>().EnableGrpcWeb()` assurent la prise en charge côté serveur. Blazor utilise `GrpcChannel` avec `GrpcWebHandler` en mode binaire. Le mode texte est également couvert par les tests.

Il n’existe pas de `FireGrpc` ni de `UsePowerGrpc` dans cette version. Ajouter des points d’entrée de mutation sans besoin utilisateur compliquerait inutilement la surface de validation et de test : l’historique complet des tours (y compris mines et tirs de zone) transite déjà en lecture. L’exigence d’au moins une méthode gRPC est satisfaite par la lecture.

### 4. Partager une seule politique de masquage

La réponse gRPC est construite depuis le DTO déjà masqué, jamais depuis `Board.Ships`. Toute cellule adverse non visée vaut `Unknown` dans les deux contrats. La conversion explicite des enums évite de dépendre silencieusement de leur ordre numérique.

Les tests comparent les réponses HTTP et gRPC-Web, y compris les grilles et les derniers tirs. Le masque reste actif après victoire ou défaite.

### 5. Configurer explicitement les frontières de transport

La politique CORS n’autorise que les origines Blazor configurées, les méthodes GET/POST et les en-têtes de requête nécessaires. Elle expose les en-têtes gRPC de statut et d’encodage. Les erreurs de contrat gRPC utilisent `RpcException` avec `InvalidArgument` ou `NotFound` ; les détails d’erreurs internes gRPC sont désactivés.

CORS est une restriction du navigateur, pas un contrôle d’autorisation. Les transports utilisent HTTP uniquement pour le développement local ; un déploiement public doit employer HTTPS et des proxys explicitement approuvés.

Le client ne réessaie pas automatiquement les mutations après une erreur réseau. Il demande une lecture de synchronisation avant d’autoriser le tir suivant.

## Options considérées

| Option | Avantages | Motif de non-sélection ou limite |
| --- | --- | --- |
| HTTP uniquement | Moins de dépendances et un seul format de contrat. | Ne satisfait pas l’exigence gRPC-Web ; resterait un choix raisonnable sans cette contrainte. |
| gRPC natif directement depuis le navigateur | Contrats Protobuf uniques. | Le navigateur ne fournit pas le support requis pour un client gRPC natif complet. |
| gRPC-Web uniquement | Contrats typés de bout en bout. | Ne satisfait pas les trois endpoints HTTP attendus ; diagnostic manuel moins immédiat. |
| HTTP + gRPC-Web partageant le domaine | Satisfait les exigences tout en gardant une seule logique autoritaire. | Retenu, avec un coût de mapping et de dépendances supplémentaire. |
| Deux moteurs indépendants selon le transport | Implémentations initialement isolées. | Rejeté : risque de divergences de tours, validation et masquage. |
| SignalR ou streaming | Adapté à des notifications temps réel et au multijoueur. | Pas de besoin dans un jeu solo où chaque tour est résolu par une requête unaire. |

## Conséquences positives

- Les invariants métier sont testables sans serveur ou navigateur.
- Les transports produisent des états cohérents et masqués.
- Les requêtes HTTP sont documentées et facilement reproductibles.
- Le contrat Protobuf est vérifié à la compilation et testable sur gRPC-Web réel.
- Le client peut vérifier la partie avec l’un ou l’autre protocole, sans dupliquer la logique de jeu.

## Coûts et limites

- JSON et Protobuf représentent des concepts proches : leurs mappings et leur compatibilité doivent être maintenus.
- gRPC-Web ajoute des packages, du code généré et de la configuration CORS. Le mode texte ajoute en outre l’encodage base64 ; le client utilise donc le mode binaire pour les appels unaires.
- Le gain de performance n’a pas été mesuré ; il ne constitue pas une justification de cette décision.
- Le store est borné et expire ses entrées, mais reste en mémoire et mono-instance. L’expiration est absolue, même si le joueur est actif.
- Le GUID sert de lien d’accès. Une isolation entre comptes nécessiterait authentification et vérification de propriété sur les deux transports.
- Une future architecture distribuée nécessiterait une transaction ou une coordination partagée, et non seulement un verrou dans le processus.

## Validation

- 142 cas xUnit réussis dans la version livrée.
- Comparaison d’états HTTP/gRPC et tests `InvalidArgument` / `NotFound`.
- CORS vérifié pour les origines permises et une origine refusée, avec prévol gRPC-Web.
- Parcours Chrome exécuté en développement et sur publication, y compris réponse de tir perdue, resynchronisation et parcours dédié aux pouvoirs (points, prévisualisation survol/focus, mine, zones).
- Publication de l’API et du client réussie ; aucune vulnérabilité connue détectée par l’audit NuGet effectué à la livraison.

## Traçabilité

| Sujet de commit prévu | Arbre de l’étape |
| --- | --- |
| `feat(api): add validated HTTP endpoints and integration tests` | `72d00cac37c32be8a0330e2ba9e9b48c17a37247` |
| `feat(grpc): expose masked game status over grpc-web` | `49082f875abae06662f204193099503802e33024` |
| `feat(app): add interactive Blazor game and transport clients` | `6a5cc1847ed249f0f7d9447fada79331a6422813` |
| `docs: document setup architecture and implementation reviews` | `refs/snapshots/battleship/step-7` |
| `feat(game): add skill powers and target previews` | commit ultérieur sur `main` |

Ces références sont des instantanés, pas des commits préexistants. Le journal donne les commandes pour créer l’historique correspondant puis retrouver les SHA réels.
