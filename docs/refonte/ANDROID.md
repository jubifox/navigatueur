# Android : ergonomie mobile et personnalisation

Deuxième étape pour l'application Android (GeckoView), après sa reconstruction
en v0.22.0. Même méthode que pour la refonte Windows : audit, plan,
implémentation, tests.

## 1. Audit (v0.22.0)

Ce qui existe et fonctionne : onglets (grille, groupes, épinglage, veille,
réouverture), barre d'outils en bas ou en haut qui s'efface au défilement,
glissement horizontal sur la barre pour changer d'onglet, suggestions locales
(onglets, favoris, historique), menu ⋯, extensions, lecture en arrière-plan,
image dans l'image, mode sombre des sites, mises à jour intégrées.

Ce qui manque pour une navigation vraiment confortable au téléphone :

| Domaine | Constat |
|---|---|
| Navigation | Pas de « tirer pour actualiser ». Retour/Avancer seulement dans le menu ; aucun moyen de revenir plusieurs pages en arrière d'un coup. |
| Barre d'outils | Toujours la même : adresse, onglets, menu. Impossible d'y mettre Retour, Accueil, Nouvel onglet… Appui long sur le compteur d'onglets = nouvel onglet immédiat, sans choix. |
| Onglets | Fermer un onglet ne se fait qu'avec la petite croix ; pas d'annulation ; pas de recherche parmi les onglets ; pas de vue en liste (le réglage `tabLayout` existe mais n'est pas utilisé) ; « Fermer tous les onglets » sans confirmation. |
| Saisie d'adresse | Aucune suggestion du moteur de recherche ; pas de recherche vocale ; taper « .com » ou « www. » au clavier de téléphone est pénible. |
| Menu ⋯ | Longue liste à faire défiler ; les interrupteurs (version ordinateur, mode sombre) sont noyés dedans. |
| Page d'accueil | Favoris seulement ; pas de sites fréquents, pas d'horloge. |
| Favoris | Impossible de les renommer ou de corriger leur adresse. |
| Fluidité | Les écrans (onglets, paramètres, historique…) apparaissent sans transition. Aucun retour haptique. |
| Accessibilité | Pas de réglage de la taille du texte des pages ni de zoom forcé. |
| Personnalisation | Thème sombre/clair, 7 couleurs d'accent et une image de fond pour la page d'accueil. C'est tout. |

## 2. Plan

### Personnalisation (nouvel écran « Personnaliser », menu ⋯ et Paramètres)

- **Thèmes prêts à l'emploi** : un toucher applique couleur, fond et teinte d'un coup.
- **Couleurs** : sombre / clair / auto, **noir pur** (écrans OLED), couleur d'accent parmi une palette **ou n'importe quelle couleur** (sélecteur teinte / saturation / luminosité, code hexadécimal), **teinte de l'interface** (les panneaux prennent un peu de la couleur d'accent).
- **Fond d'écran** : aucun, lueur (l'actuel), couleur unie, **dégradé** (modèles ou dégradé sur mesure : 2 ou 3 couleurs, angle, linéaire ou radial, **animé** en option) ou image (assombrissement et flou réglables).
- **Fond dans toute l'application** : vue des onglets, paramètres, listes et saisie d'adresse sur le fond choisi, panneaux translucides.
- **Barre d'outils** : position, style opaque ou « verre » (translucide), taille, **boutons au choix** (Retour, Avancer, Accueil, Nouvel onglet, Partager, Favori).
- **Formes** : coins carrés, arrondis ou très arrondis.
- **Page d'accueil** : titre personnalisable (ou masqué), horloge et salutation, favoris, sites fréquents, nombre de colonnes.
- Aperçu en direct en haut de l'écran, et retour à l'apparence d'origine en un toucher.

### Ergonomie et navigation

- **Tirer pour actualiser** en haut de page (en s'appuyant sur ce que le moteur dit de la page, pour ne pas gêner les cartes ou les pages qui défilent elles-mêmes).
- **Gestes sur la barre** : glisser vers le haut (ou vers le bas si la barre est en haut) ouvre la vue des onglets ; gauche/droite change d'onglet, la barre suit le doigt.
- **Appui long sur Retour** : liste des pages précédentes et suivantes de l'onglet.
- **Appui long sur le compteur d'onglets** : nouvel onglet, onglet privé, fermer l'onglet.
- **Onglets** : glisser une vignette pour la fermer, **« Annuler »** après chaque fermeture, recherche, vue grille ou liste, confirmation avant de tout fermer.
- **Saisie d'adresse** : suggestions du moteur de recherche (désactivables, jamais en privé), **recherche vocale**, raccourcis « www. », « .com », « .fr », « / » au-dessus du clavier.
- **Menu ⋯ en grille** : tout visible d'un coup d'œil, interrupteurs mis en évidence, copier le lien.
- **Page d'accueil** : sites fréquents (calculés depuis l'historique, masquables).
- **Favoris** : modifier le titre et l'adresse, copier, partager.
- **Fluidité** : transitions animées entre écrans, retour haptique sur les appuis longs et les gestes.
- **Accessibilité** : taille du texte des pages, zoom autorisé sur tous les sites.

### Compatibilité

Les nouveaux réglages ont tous une valeur par défaut : un `settings.json` de la
v0.22.0 se lit tel quel. L'image de fond déjà choisie (`newTabBackgroundPath`)
est reprise comme fond « Image ».

## 3. Tests

- **Tests unitaires** (`android/app/src/test`) : conversions de couleurs
  (hexadécimal ↔ teinte/saturation/luminosité), validité des thèmes et
  dégradés prédéfinis, géométrie des dégradés selon l'angle, calcul des sites
  fréquents, lecture des suggestions des cinq moteurs, lecture d'un
  `settings.json` de la v0.22.0 et reprise de son image de fond.
- **Scénario sur émulateur** (`tests/android/smoke.sh`, CI Android) : les
  boutons sont trouvés par leur nom d'accessibilité plutôt que par leurs
  coordonnées. Le scénario couvre les nouveaux écrans (Personnaliser, thème
  appliqué, dégradé, menu en grille, vue en liste, historique de l'onglet,
  suggestions) et prend une capture à chaque étape. Les captures sont publiées
  sur la branche `ci/android-captures`.

## 4. Résultats

Tout a été vérifié sur la CI Android (`.github/workflows/android.yml`) : la
compilation, 36 tests unitaires (dont 21 nouveaux), puis le scénario complet
sur un émulateur Pixel 6 (Android 14). Les captures de chaque étape sont sur
la branche `ci/android-captures`.

Ce que les captures montrent, dans l'ordre du scénario :

| Étape | Vérifié |
|---|---|
| Page d'accueil au premier lancement | Lueur bleue d'origine, barre d'outils en bas |
| Tirer pour actualiser (Wikipédia, puis example.com) | Correction en cours de vérification (voir plus bas) |
| Défilement | La barre s'efface en lisant et revient en remontant |
| Saisie d'adresse | Suggestions de Bing (« meteo france », « meteociel »…), choix ponctuel du moteur, touches www. / .com / .fr / .org / / / https:// au-dessus du clavier |
| Menu ⋯ | Page et étoile, Retour / Avancer / Recharger / Accueil, extensions, grille de 16 tuiles |
| Appui long sur Retour | « Pages de cet onglet » : example.com (affichée) et Wikipédia |
| Glissement vers le haut sur la barre | Vue des onglets ; passage grille ↔ liste |
| Personnaliser | Aperçu en direct, thèmes, couleurs, palette d'accent |
| Thème « Aurore » | Dégradé, accent corail, teinte des panneaux, appliqués partout |
| Fond dans toute l'application | Vue des onglets, Personnaliser et Paramètres sur le dégradé |
| Boutons Accueil et Partager ajoutés | Visibles dans la barre ; le bouton Accueil ouvre la page d'accueil |
| Page d'accueil personnalisée | Horloge, « Bonsoir · Vendredi 6 novembre », titre, sites fréquents (Example, Wikipedia) |
| Glisser un onglet | Onglet fermé, message « Onglet fermé · Annuler », l'onglet revient |
| Paramètres | Section Apparence (Personnaliser, thème), Interface (gestes, tirer pour actualiser), Accessibilité |

Ce que les passages successifs ont permis de corriger :

- Une fenêtre système « Pixel Launcher isn't responding » recouvrait toutes
  les captures de l'ancien scénario sans le faire échouer ; elle est
  maintenant fermée avant chaque capture.
- Activer « Fond dans toute l'application » ramenait Personnaliser tout en
  haut : le fond change désormais sous le même contenu.
- Le glissement pour fermer un onglet ne réagissait pas : il utilise
  maintenant le composant de Material (`SwipeToDismissBox`).
- Tirer pour actualiser ne démarrait jamais : le moteur signale qu'une page
  tout en haut peut encore « défiler vers le haut » (à cause de la barre
  coulissante). C'est la position de défilement de la page qui décide
  maintenant ; le journal `NvPull` des versions de test a servi à le voir.
- « Radial » et « Nouvel onglet » étaient coupés dans Personnaliser ; la croix
  sous une carte d'onglet se voyait à travers les panneaux translucides.

Non couvert par l'émulateur : la recherche vocale (pas de reconnaissance
vocale sur l'image de test), le flou de l'image de fond (Android 12+, image
choisie dans la galerie), le retour haptique.
