# Journal des étapes et traçabilité

## Statut des références

Ce document récapitule les demandes fonctionnelles et leur mise en œuvre. Il ne prétend pas reproduire des échanges inexistants ni un historique déjà committé.

Lors de la rédaction initiale, `main` n’avait aucun commit. Les sept commits initiaux ont depuis été créés et publiés, jusqu’à `3e2c3d7`. Les commandes de création conservées plus bas décrivent cette phase initiale et ne doivent pas être rejouées sur la branche actuelle. Les anciens chemins sont ceux des instantanés ; consulter le README pour l’organisation actuelle par fonctionnalité. Chaque étape avait été enregistrée dans un arbre Git à l’aide d’un index séparé. Les références `refs/snapshots/battleship/step-N` préservent ces arbres localement, sans modifier l’index de travail de l’utilisateur. **Un SHA d’arbre n’est pas un SHA de commit.**

| Étape | Message de commit prévu | Arbre vérifiable |
| --- | --- | --- |
| 1 | `chore: initialize .NET 10 solution with four projects` | `1a2510cd2cfa25ce8246b2c79ff4957d36d408b2` |
| 2 | `feat(models): implement battleship engine and masked game contracts` | `1256a4ac032b5b9a191b077dbff83e6ba789955b` |
| 3 | `test(models): cover placement masking and atomic turns` | `3f54132edd14b6e5ad927618d54c7fd820ddd6ac` |
| 4 | `feat(api): add validated HTTP endpoints and integration tests` | `72d00cac37c32be8a0330e2ba9e9b48c17a37247` |
| 5 | `feat(grpc): expose masked game status over grpc-web` | `49082f875abae06662f204193099503802e33024` |
| 6 | `feat(app): add interactive Blazor game and transport clients` | `6a5cc1847ed249f0f7d9447fada79331a6422813` |
| 7 | `docs: document setup architecture and implementation reviews` | `refs/snapshots/battleship/step-7` |

L’arbre final inclut ce document : son SHA ne peut pas être inscrit dans son propre contenu sans changer cet arbre. Sa référence locale permet de le retrouver après enregistrement.

## Étape 1 — Initialiser une solution indépendante du poste

**Demande reformulée :** préparer une solution .NET 10 / C# 14 avec Models, API, App et Tests, des références explicites et des exclusions Git adaptées.

**Réalisation :** solution `.slnx`, SDK sélectionné par `global.json`, propriétés partagées et infrastructure xUnit. Le dépôt existait déjà sur `main`, sans commit : il n’a pas été réinitialisé.

**Vérification :** les quatre projets compilent ; aucun test métier n’existe encore à cette étape. Une découverte vide n’est pas présentée comme un succès fonctionnel.

## Étape 2 — Construire le moteur et les frontières de données

**Demande reformulée :** placer la flotte standard, jouer un tour complet, refuser les coups invalides et ne pas révéler les navires adverses.

**Réalisation :** `Board`, `Ship`, `Game` et contrats de données. Les candidats de placement sont énumérés avant sélection ; l’ordinateur utilise une permutation des coordonnées. `Game` synchronise lectures et tours, puis construit des DTO détachés. L’adversaire reste masqué même à la fin.

**Vérification :** compilation du domaine sans dépendance externe. Les propriétés comportementales sont testées à l’étape suivante.

## Étape 3 — Prouver les invariants du moteur

**Demande reformulée :** tester les placements, les grilles masquées et le refus d’un coup hors grille ou déjà joué.

**Réalisation :** tests xUnit déterministes, incluant aussi les deux issues, l’absence de mutation des instantanés et vingt tirs concurrents sur une même case.

**Vérification :** **21 cas réussis**. Le test de placement parcourt 200 graines, et le test de déroulement complet en parcourt 20.

**Adaptation du découpage :** les tests FluentValidation et d’intégration ne peuvent pas compiler avant leurs implémentations. Ils sont donc ajoutés avec les étapes 4 et 5, plutôt que laissés en échec de compilation dans ce commit.

## Étape 4 — Exposer les opérations HTTP validées

**Demande reformulée :** fournir les trois endpoints Minimal API, TypedResults, FluentValidation, CORS et OpenAPI.

**Réalisation :** stockage singleton borné avec expiration absolue, validators, Problem Details et limiteur par IP. Les paramètres obligatoires des constructeurs JSON sont respectés pour ne pas transformer un champ absent en coordonnée zéro.

**Retour de vérification réel :** une première exécution a produit **7 échecs sur 54 cas**. En mode Development, la liaison JSON lançait `BadHttpRequestException` et le gestionnaire d’exceptions général renvoyait 500. La configuration explicite `RouteHandlerOptions.ThrowOnBadRequest = false` restaure les 400 attendus.

**Vérification finale de l’étape :** **54 cas réussis**, notamment requêtes JSON incomplètes, refus sans mutation, CORS et document OpenAPI.

## Étape 5 — Ajouter une seconde représentation du même état

**Demande reformulée :** fournir un contrat Protobuf, un service `GetGameStatus`, gRPC-Web et une gestion explicite des erreurs.

**Réalisation :** messages typés, validation de l’UUID, mapping depuis `Game.GetState()`, `UseGrpcWeb`, `EnableGrpcWeb` et exposition des en-têtes CORS nécessaires. Aucun objet interne du moteur n’est sérialisé.

**Vérification :** **65 cas réussis**, dont des appels via `GrpcWebHandler` en modes binaire et texte, la comparaison HTTP/gRPC et les codes `InvalidArgument` / `NotFound`.

## Étape 6 — Permettre une partie complète dans le navigateur

**Demande reformulée :** afficher deux grilles, tirer au clic, suivre les chargements et les résultats, intégrer les clients HTTP et gRPC-Web.

**Réalisation :** interface française responsive, composant de grille commun, contrôles clavier, derniers tirs et légende. Un lien de partie permet la reprise après rechargement. Les opérations utilisent des délais et un jeton d’annulation ; une erreur après un tir impose une synchronisation, sans rejeu du POST.

**Adaptation :** le namespace du contrat `BattleShip.Grpc` peut masquer le namespace de la bibliothèque depuis Razor. L’import est explicitement `global::Grpc.Core`.

**Vérification :** compilation de la solution sans avertissement, 65 cas xUnit conservés et parcours Chrome automatisé réussi sur le serveur de développement puis sur la publication. La réponse perdue après un tir accepté a été simulée : reprise correcte avec un seul tour supplémentaire. Aucun échec JavaScript non géré observé. Le scénario navigateur est un contrôle externe, pas un test inclus dans la suite xUnit.

## Étape 7 — Documenter les décisions et les limites

**Demande reformulée :** livrer un guide d’installation, ce journal, trois revues et un ADR avec des références vérifiables.

**Réalisation :** `README.md`, `PROMPTS.md`, `REVUE-IA.md` et `docs/adr/ADR-001-ARCHITECTURE-GRPC-HTTP.md`. Les limites de persistance, d’authentification, de publication et d’expiration sont explicites. Les consignes de vérification sont également conservées dans `AGENTS.md`.

## Évolutions postérieures aux sept étapes

Ces demandes ultérieures ont été commitées directement, sans instantanés intermédiaires.

### Historique des parties — `092df61`

**Demande reformulée :** conserver un historique de partie.

**Réalisation :** `GameStateDto.CreatedAtUtc` et `Turns` exposés en HTTP et gRPC-Web ; archive locale des 50 dernières parties dans `localStorage` avec consultation et reprise ; `GameHistoryStore` sérialisé via un contexte System.Text.Json généré. **81 cas réussis.**

### Réorganisation par fonctionnalité — `d3299df`

**Demande reformulée :** regrouper les composants qui vont ensemble dans chaque projet, y compris les tests.

**Réalisation :** dossiers `Engine`/`Contracts`/`Enums`/`Exceptions` dans Models ; `Features/Games/{Http,Grpc,Storage}` dans l’API ; `Features/{Games,History}` et `Shared` dans l’App ; `Unit/{Engine,Storage,Validation,History}`, `Integration/{Http,Grpc}` et `Infrastructure` dans les tests. **81 cas réussis.**

### Pouvoirs et points de compétence — `feat(game): add skill powers and target previews`

**Demande reformulée :** 1 point de compétence par tir (10 maximum) ; mine à 2 points renvoyant un tir sur la même case ; carré 2 × 2 à 4 points ; ligne ou colonne entière à 6 points ; prévisualisation des cases ciblées au survol ; mise à jour des fichiers Markdown.

**Décisions confirmées avec le demandeur :** seuls les tirs normaux rapportent des points ; poser une mine consomme le tour ; le tir adverse sur une mine est appliqué puis renvoyé ; un double naufrage par renvoi donne un match nul ; les pouvoirs invalides ou trop coûteux ne consomment ni points ni tour ; le carré utilise la case choisie comme coin supérieur gauche ; les cases déjà visées sont ignorées et une zone sans nouvelle cible est refusée.

**Réalisation :** `PowerRules`, `GameAction`, `GameStatus.Draw`, `SkillPoints`, `UsePowerRequest`, endpoint `POST /api/games/{id}/powers`, champs `TurnMessage`/`GameStatusReply` étendus, composant `PowerControls`, prévisualisation survol/focus dans `GameGrid`, historique des mines et tirs de zone, compatibilité des archives antérieures. **142 cas réussis** et parcours Chrome dédié aux pouvoirs.

### Difficulté de l’adversaire — `feat(game): add ai difficulty levels`

**Demande reformulée :** permettre de choisir la difficulté — facile joue aléatoirement, normal joue comme un humain avec des erreurs, difficile calcule à chaque coup la case la plus probable ; mettre à jour les fichiers Markdown.

**Réalisation :** enum `Difficulty` (`Easy`/`Normal`/`Hard`) choisi à la création ; `ComputerOpponent` dans le moteur — facile conserve la file pré-mélangée historique, normal poursuit les cases touchées en privilégiant le prolongement des lignes avec ~20 % de tirs au hasard, difficile évalue tous les placements valides des navires restants avec forte pondération des cases touchées. L’IA n’observe que `ToGrid(false)` : jamais la position des navires. `GameStateDto.Difficulty` est un paramètre optionnel (archives antérieures → `Easy`, fidèle à leur IA réelle) ; `CreateGameRequest.Difficulty` absent → `Easy` pour compatibilité. Champ `difficulty` ajouté au proto, sélecteur à trois options dans le formulaire (présélection Normal), difficulté affichée dans l’en-tête et l’historique. **169 cas réussis** et parcours Chrome dédié.

### Placement manuel de la flotte — `feat(game): add interactive ship placement phase`

**Demande reformulée :** au début d’une partie, permettre de tourner et de placer ses bateaux.

**Réalisation :** nouveau statut `GameStatus.PlacingShips` ; `CreateGameRequest.ManualPlacement` (absent → flotte automatique, compatibilité). Moteur : `Board.CreateEmpty`, `PlaceShip` (horizontal/vertical, dépassement et chevauchement refusés, repositionnement du même type), `RemoveShip`, `Randomize` (complète les navires restants), `StartBattle` (exige une flotte complète) ; tirs et pouvoirs refusés avant le combat (`GameNotStarted`). Endpoints POST `/fleet/place|remove|randomize|confirm` (POST pour rester dans la politique CORS GET/POST). `GameStateDto.ShipsToPlace` optionnel ; proto étendu (`GamePhase.PLACING`, `ShipKind`, `ShipSpecMessage`). Interface : composant `FleetPlacement` — liste des navires, rotation au bouton et à la touche R, prévisualisation de l’emprise au survol/focus sans révéler l’adversaire, reprise d’un navire posé, complétion aléatoire, bouton Commencer. Le test navigateur a détecté puis corrigé une réinitialisation de sélection lors de la reprise d’un navire posé (race de re-rendu). **207 cas réussis** et parcours Chrome dédié (22 contrôles).

## Créer les sept commits, dans l’ordre

Ces commandes sont prévues pour **ce dépôt local encore sans commit**. Les références d’instantanés personnalisées ne sont pas transférées par un clone ordinaire. Après création des commits, l’historique normal suffit et peut être partagé comme tout dépôt Git.

Avant de commencer, vérifier que l’index normal ne contient aucun changement personnel :

```bash
git status --short --branch
git diff --cached --stat
git for-each-ref --format='%(refname) %(objecttype) %(objectname)' refs/snapshots/battleship/
```

`git read-tree` remplace le contenu de l’index, **sans toucher aux fichiers de travail**. Ne pas ajouter `-u`. Ne pas exécuter ces commandes si vous avez préparé d’autres changements dans l’index. Entre les premiers commits, `git status` montrera les étapes futures encore présentes dans les fichiers : c’est attendu. Ne pas utiliser `git add .` entre les étapes, car cela regrouperait toute la version finale.

```bash
git read-tree refs/snapshots/battleship/step-1 &&
git commit -m "chore: initialize .NET 10 solution with four projects" &&
git read-tree refs/snapshots/battleship/step-2 &&
git commit -m "feat(models): implement battleship engine and masked game contracts" &&
git read-tree refs/snapshots/battleship/step-3 &&
git commit -m "test(models): cover placement masking and atomic turns" &&
git read-tree refs/snapshots/battleship/step-4 &&
git commit -m "feat(api): add validated HTTP endpoints and integration tests" &&
git read-tree refs/snapshots/battleship/step-5 &&
git commit -m "feat(grpc): expose masked game status over grpc-web" &&
git read-tree refs/snapshots/battleship/step-6 &&
git commit -m "feat(app): add interactive Blazor game and transport clients" &&
git read-tree refs/snapshots/battleship/step-7 &&
git commit -m "docs: document setup architecture and implementation reviews"
```

Arrêter la séquence si un commit échoue. Ne pas passer à l’étape suivante avant d’avoir résolu l’erreur ; ne pas contourner les hooks. Aucune commande de push n’est nécessaire pour créer cet historique.

### Retrouver les véritables SHA des commits

Après exécution :

```bash
git log --reverse --format='%h %s'
git status --short
```

Pour relier un sujet à un SHA exact et vérifier les preuves de revue :

```bash
git log --format='%H %s' --fixed-strings --grep='feat(api): add validated HTTP endpoints and integration tests'
git diff-tree --stat refs/snapshots/battleship/step-3 refs/snapshots/battleship/step-4
git show refs/snapshots/battleship/step-4:BattleShip.API/Program.cs
```

Les messages identifient les commits correspondants après leur création ; les arbres fournissent une preuve de contenu dès maintenant. Aucun auteur, date de commit ou SHA de commit n’a été fabriqué.
