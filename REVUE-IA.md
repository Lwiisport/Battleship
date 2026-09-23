# Revues de code argumentées

Ces cinq revues évaluent le code et des alternatives de conception. Elles ne constituent ni une approbation humaine, ni une certification de sécurité. Les décisions sont reliées aux tests et aux instantanés réellement enregistrés.

Les messages de commit cités sont les sujets prévus dans [PROMPTS.md](PROMPTS.md). À la rédaction, les commits n’ont pas encore été exécutés ; les hashes ci-dessous désignent des **arbres Git**, jamais des commits inventés. Après création de l’historique, `git log --fixed-strings --grep='<sujet>'` retrouve le commit correspondant.

Les commits initiaux sont désormais présents dans l’historique Git. Les chemins ci-dessous restent ceux du code effectivement examiné dans les instantanés cités ; la réorganisation des fichiers n’altère pas ces preuves. Pour les emplacements actuels (`Engine`, `Features`, `Unit`, `Integration`), consulter l’arborescence du README.

## Revue 1 — Accepté : construire l’état adverse masqué sur le serveur

### Code examiné

- `BattleShip.Models/Board.cs` : `ToGrid(bool revealShips)`.
- `BattleShip.Models/Game.cs` : `Snapshot()` et verrou commun de lecture/écriture.
- `BattleShip.API/Services/GameGrpcService.cs` : mapping du DTO, et non du plateau interne.

### Décision

**Accepté.** Le moteur possède les navires mais ne renvoie à l’API qu’un instantané de cellules. Toute case adverse non visée reste `Unknown`, y compris lorsque la partie est terminée. Les DTO n’ont pas de liste cachée de positions ennemies ni d’identifiant permettant de reconstruire un navire non découvert.

Le client reçoit naturellement les positions de toutes les cellules de la grille ; ce ne sont pas des positions révélées de navires. L’information sensible est l’occupation d’une case non visée, jamais transmise. Une case `Sunk` ne révèle que des positions déjà touchées.

Les tableaux du DTO sont modifiables par leur destinataire mais détachés de l’état réel. Le test de mutation du DTO vérifie cette frontière. Le verrou autour du tour complet évite qu’une lecture observe le tir du joueur sans la réponse automatique de l’ordinateur.

### Preuves

- `BoardTests.Hidden_grid_only_reveals_shots_and_sunk_cells`.
- `GameTests.New_game_reveals_only_player_fleet`.
- `GameTests.Returned_arrays_cannot_mutate_the_game`.
- `GrpcGameTests.Grpc_web_returns_the_same_masked_state_as_http`, en deux modes de transport.

Références :

| Sujet prévu | Arbre |
| --- | --- |
| `feat(models): implement battleship engine and masked game contracts` | `1256a4ac032b5b9a191b077dbff83e6ba789955b` |
| `test(models): cover placement masking and atomic turns` | `3f54132edd14b6e5ad927618d54c7fd820ddd6ac` |
| `feat(grpc): expose masked game status over grpc-web` | `49082f875abae06662f204193099503802e33024` |

### Réserve

Le masquage ne fournit pas une authentification. L’identifiant de partie est un lien d’accès : un tiers qui le connaît peut jouer à la place du joueur. CORS ne corrige pas cette limite et ne doit pas être présenté comme une autorisation utilisateur.

## Revue 2 — Adapté : traiter les entrées JSON invalides comme des erreurs client

### Code examiné

- `BattleShip.API/Program.cs` : options JSON et `RouteHandlerOptions`.
- `BattleShip.API/Validation/GameValidators.cs`.
- `BattleShip.API/Endpoints/GameEndpoints.cs`.
- `BattleShip.Tests/HttpGameTests.cs`.

### Problème constaté

Deux frontières doivent être distinguées :

1. Un JSON peut être syntaxiquement illisible ou manquer un paramètre de constructeur obligatoire. FluentValidation ne reçoit alors aucun DTO exploitable.
2. Un DTO valide au sens JSON peut contenir un nom vide ou des coordonnées hors grille. FluentValidation doit le refuser.

Sans exigence des paramètres de constructeur, `{ "row": 0 }` risquerait d’utiliser la valeur par défaut de `column` et de tirer en `(0, 0)`. La configuration `RespectRequiredConstructorParameters = true` empêche cette interprétation.

La première exécution de l’intégration a réellement révélé **7 échecs sur 54 tests** : en mode Development, `BadHttpRequestException` remontait au gestionnaire général et devenait une réponse 500. Un journal détaillé a confirmé l’origine dans `RequestDelegateFactory` lors de la lecture du corps JSON.

### Décision

**Adapté.** `RouteHandlerOptions.ThrowOnBadRequest = false` laisse la liaison Minimal API renvoyer 400 pour les erreurs de corps, quel que soit l’environnement. Les validators restent responsables des valeurs métier et renvoient `TypedResults.ValidationProblem`. Les tirs répétés ou après la fin renvoient 409, distinct d’une erreur de coordonnées.

Le moteur conserve ses propres gardes : un client qui contourne la validation HTTP n’obtient pas un moteur permissif.

### Preuves

- `HttpGameTests.Invalid_creation_returns_bad_request` : corps absent de champs, `null`, JSON mal formé, nom invalide.
- `HttpGameTests.Invalid_fire_returns_bad_request_and_does_not_mutate_game` : comparaison de l’état avant et après le refus.
- `ValidationTests.Fire_rejects_out_of_bounds_coordinates`.
- Suite passée de 47 réussites / 7 échecs à **54 réussites / 0 échec** après la correction.

Référence : `feat(api): add validated HTTP endpoints and integration tests`, arbre `72d00cac37c32be8a0330e2ba9e9b48c17a37247`.

### Réserve

La correction intermédiaire défaillante n’a pas été enregistrée dans un commit distinct : elle a été corrigée avant la capture atomique de l’étape 4. Le résultat de test ci-dessus décrit une exécution de développement, pas un ancien commit fictif.

## Revue 3 — Rejeté : rejouer automatiquement un tir après une erreur réseau

### Alternative examinée

Une stratégie générique de nouvelle tentative pourrait réémettre `POST /fire` dès qu’une requête expire ou que sa réponse est perdue. C’est une alternative évaluée et non retenue, **pas un code prétendument retiré d’un ancien commit**.

### Décision

**Rejeté.** Une absence de réponse ne prouve pas que le serveur n’a pas appliqué le tir. Un rejeu automatique masque cette incertitude et peut produire des refus 409 ou des messages incohérents. Changer de coordonnée lors d’une nouvelle tentative serait plus grave, car cela pourrait jouer un second tour non demandé.

L’implémentation conserve un seul POST, puis active `needsSync` en cas d’erreur. Les cases sont désactivées jusqu’à une lecture HTTP ou gRPC-Web réussie. Le serveur refuse de son côté les coordonnées déjà visées et verrouille l’ensemble du tour.

### Code retenu en remplacement

- `BattleShip.App/Pages/Home.razor` : `Fire`, `Run`, `Refresh`, `needsSync`.
- `BattleShip.App/Services/GameHttpClient.cs` : une seule requête de tir, avec délai d’attente.
- `BattleShip.Models/Game.cs` : exclusion mutuelle et refus métier avant progression du tour.

### Preuves

- `GameTests.Duplicate_turn_does_not_trigger_a_computer_shot`.
- `GameTests.Concurrent_identical_shots_only_apply_one_turn` : vingt appels, un seul tour accepté.
- `HttpGameTests.Duplicate_fire_returns_conflict_without_computer_turn`.
- Contrôle Chrome externe : interception d’un tir, transfert réel au serveur, puis suppression de la réponse côté navigateur. La grille s’est bloquée ; l’actualisation gRPC-Web a retrouvé exactement deux cases adverses visées après deux tirs, sans troisième tour. Le contrôle a réussi en développement et sur la publication.

Références :

| Sujet prévu | Arbre |
| --- | --- |
| `test(models): cover placement masking and atomic turns` | `3f54132edd14b6e5ad927618d54c7fd820ddd6ac` |
| `feat(api): add validated HTTP endpoints and integration tests` | `72d00cac37c32be8a0330e2ba9e9b48c17a37247` |
| `feat(app): add interactive Blazor game and transport clients` | `6a5cc1847ed249f0f7d9447fada79331a6422813` |

### Réserve

Le contrôle navigateur n’est pas inclus dans les 65 cas xUnit du dépôt. Pour une évolution vers des commandes métier réessayables à grande échelle, une clé d’idempotence explicite, persistée avec le résultat du tour, serait préférable à un simple mécanisme de nouvelle tentative.

## Revue 4 — Adapté : résoudre la mine dans l’ordre adverse puis renvoi, avec match nul possible

### Code examiné

- `BattleShip.Models/Engine/Game.cs` : `UsePower`, détonation et `GameStatus.Draw`.
- `BattleShip.Models/Engine/PowerRules.cs` : coûts et zones cibles.
- `BattleShip.Models/Engine/Board.cs` : mines posées et tirs de zone.

### Problème à trancher

Une mine renvoie un tir sur la même coordonnée adverse. Deux ordres étaient possibles : intercepter le tir ennemi avant application (la mine « protège » la case), ou appliquer le tir puis renvoyer. La première option aurait fait de la mine un bouclier et aurait compliqué le cas où la case minée contient un navire.

### Décision

**Adapté**, selon la règle confirmée avec le demandeur : le tir adverse est appliqué normalement, puis le renvoi est résolu. La mine est une riposte, pas une protection. Si le renvoi détruit la dernière case des deux flottes, le statut `Draw` évite de déclarer un vainqueur arbitraire ; l’interface et les deux transports exposent ce quatrième résultat.

Le renvoi n’est pas un tir normal : il ne rapporte aucun point et ne peut pas lui-même déclencher de mine (l’ordinateur n’en pose pas). Les cases déjà visées par une zone sont ignorées, et une zone entièrement rejouée est refusée avant tout paiement — ainsi un pouvoir refusé ne consomme ni points ni tour, conformément à l’invariant historique des tirs refusés.

### Preuves

- `PowerTests` : coûts, plafond de 10 points, refus sans mutation, zones, détonation et match nul.
- `HttpPowerTests` : le même contrat via `POST /api/games/{id}/powers`, y compris les codes 409 métier.
- `GameHistoryStoreTests.Powers_and_points_survive_archiving` et rejet des archives aux points impossibles.
- Contrôle Chrome externe : 26 vérifications, dont la prévisualisation au survol et au focus clavier sans fuite des navires cachés.

Référence : `feat(game): add skill powers and target previews`, commit créé après ces revues.

### Réserve

La prévisualisation calcule les zones côté client via `PowerRules`, dupliquant la géométrie du moteur dans l’interface. Un décalage futur entre les deux copies serait possible ; les tests d’intégration couvrent le serveur, pas le rendu, d’où le contrôle navigateur dédié.

## Revue 5 — Accepté : une IA difficile calculée uniquement sur les tirs observés

### Code examiné

- `BattleShip.Models/Engine/ComputerOpponent.cs` : `ChooseTarget`, `Placements`, `RemainingSizes`, `SunkRuns`.
- `BattleShip.Models/Engine/Game.cs` : l’adversaire reçoit `playerBoard.ToGrid(revealShips: false)`.

### Alternatives considérées

1. Donner à l’IA difficile un accès direct au `Board` interne, plus simple à coder mais tricheur : elle connaîtrait les positions réelles des navires.
2. Ne transmettre que la grille observée (`Unknown`, `Miss`, `Hit`, `Sunk`), comme le verrait un joueur humain, et en déduire les probabilités.

### Décision

**Accepté : option 2.** `ChooseTarget` ne reçoit que des `CellDto` masqués : l’impossibilité de tricher est structurelle, pas une convention. La densité de probabilité énumère les placements des navires restants compatibles avec les tirs observés ; les navires coulés sont déduits des lignes de cases `Sunk`. Le niveau normal réutilise la même vue pour poursuivre les touches avec un taux d’erreur, ce qui reproduit un jeu humain sans sophistication probabiliste.

### Preuves

- `DifficultyTests.Hard_shoots_beside_an_isolated_hit` et `Hard_extends_collinear_hits_before_anything_else` : le calcul privilégie les placements couvrant les touches.
- `DifficultyTests.Hard_damages_the_player_fleet_faster_than_easy` : supériorité mesurée sur 12 graines.
- `DifficultyTests.Normal_pursues_hit_neighbours_but_still_makes_mistakes` : poursuite majoritaire avec erreurs.
- `HttpGameTests.Creation_accepts_and_echoes_the_difficulty` et `GrpcGameTests` : la valeur choisie traverse HTTP et gRPC.

Référence : `feat(game): add ai difficulty levels`, commit créé après ces revues.

### Réserve

Deux navires coulés accolés sont indiscernables dans la grille observée : une ligne de `Sunk` de longueur 5 peut être un porte-avions ou un sous-marin collé à un torpilleur. Le calcul retire alors les plus grandes tailles possibles — approximation assumée, rare et sans fuite d’information. Le mode difficile reste plus lent à raisonner qu’un humain sur les fins de partie à deux cases, car il ne privilégie pas la parité du plus petit navire restant.

## Bilan

Le code accepté protège la frontière des données ; le code adapté corrige un défaut reproduit dans les tests ; l’alternative rejetée évite de confondre une réponse perdue avec une commande non exécutée. Les limites restantes sont assumées et documentées : stockage mémoire mono-instance, absence de comptes utilisateurs, duplication de la géométrie des pouvoirs côté client, ambiguïté des navires coulés accolés dans le calcul probabiliste et contrôle navigateur externe.
