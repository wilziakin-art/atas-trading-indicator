# Projet : ATAS Multi-Signal Indicator

## Contexte général
Développement d'un système d'indicateurs de trading multi-phases pour la plateforme **ATAS** (Advanced Time And Sales), en C#.
L'utilisateur est trader sur **NQ futures** (Nasdaq) et utilise une approche en 3 phases : Narration → Confirmation → Exécution.

---

## Architecture du système

### Communication inter-fenêtres
- Chaque indicateur écrit son vote dans `%APPDATA%\ATASMultiSignal\state.json`
- L'indicateur maître (Phase 3) lit tous les votes en temps réel
- Couche : `ATASMultiSignal/Communication/`

### Phase 1 — Narration (5 fenêtres)
| Graphique | Indicateur | Fichier |
|---|---|---|
| 150 000 volume | VWAP mensuelle | `Phase1_Narration/VwapNarrationIndicator.cs` |
| NQ 1min | 3 EMA (9/45/135) | `Phase1_Narration/MaNarrationIndicator.cs` |
| SP500 1min | 3 EMA (9/45/135) | idem, InstrumentId=SP |
| DJI 1min | 3 EMA (9/45/135) | idem, InstrumentId=DJI |
| 30min | Market Profile POC/VAH/VAL | `Phase1_Narration/MarketProfileIndicator.cs` |

### Phase 2 — Confirmation (4 fenêtres)
| Graphique | Indicateur | Fichier |
|---|---|---|
| Heikin Ashi 500 tick | MACD + Schaff Trend + CVD | `Phase2_Confirmation/HeikinAshiConfirmIndicator.cs` |
| Cluster 500 tick | Imbalances bid/ask | `Phase2_Confirmation/ClusterConfirmIndicator.cs` |
| 10 secondes | MACD + CCI | `Phase2_Confirmation/MacdCciConfirmIndicator.cs` |
| Footprint 10 tick reversal | Delta + Delta Change + Carnet | `Phase2_Confirmation/FootprintConfirmIndicator.cs` |

### Phase 3 — Exécution (indicateur maître)
- Graphique : Footprint 10 tick reversal scale 1
- Fichier : `Phase3_Execution/ExecutionIndicator.cs`
- Entrée : Phase 1 ET Phase 2 alignées (même direction Bull/Bear)
- Sortie : 2 facteurs sur 3 actifs — DOM Sweep + MACD 30s + CCI 30s

---

## Référentiel VWAP — Formation Easy Money Invest (EMI)

> Source : Formation "LA VWAP — Du Niveau Débutant à l'Exécution Desk" — Easy Money Invest (confidentiel)

### Définition
- VWAP = Volume Weighted Average Price = prix moyen réel payé par le marché
- Se réinitialise chaque jour en intraday
- Référence d'exécution institutionnelle (banques, hedge funds, algos HTF)
- Prix > VWAP → pression acheteuse dominante | Prix < VWAP → pression vendeuse | Retour VWAP → équilibre

### Structure statistique : VWAP + Standard Déviations (SD)

| Zone | Lecture | Statistique |
|---|---|---|
| DVA (SD-1 à SD+1) | Zone d'équilibre dynamique | ~70% des échanges |
| SD2 | Excès probable | 2–3% du temps |
| SD3 | Extension agressive | Rare |
| SD4 | Déséquilibre violent | Très rare |
| SD5 | Climax / panique | Exceptionnel |

**DVA (Dynamic Value Area)** = zone entre SD+1 et SD-1, comparable à la Value Area du Market Profile.

### Les 4 Séquences de marché

| Séquence | Caractéristiques | Narration | Stratégie |
|---|---|---|---|
| **Équilibrée** | VWAP plate, SD horizontales, prix oscillent, symétrie | Rotation | Extreme Fade |
| **Déséquilibrée** | VWAP inclinée, SD s'écartent, prix hors DVA | Continuation | Imbalance Pullback |
| **Reversale** | Transition déséquilibre→équilibre, break VWAP confirmé | Changement | Attendre confirmation |
| **Breakout** | Transition équilibre→déséquilibre, cassure DVA avec volume | Breakout | Pullback sur DVA cassée |

### Les 5 Patterns opérationnels

#### 1. Imbalance Pullback
- Contexte : Sortie DVA + Pullback SD1 + VWAP pentifiée + SD ouvertes et pentifiées
- Si VWAP plate → ce N'EST PAS un Imbalance Pullback
- Structure : Identifier imbalance (cassure DVA, volume > moy, delta directionnel) → Pullback SD1 (progressif, faible volume, delta se neutralise) → Reprise (delta trend, agressifs, pas d'absorption opposée)
- Objectifs : Nouveau high/low → Extension SD suivante → Poursuite rythme
- Stop : Sous structure du pullback | Pas sous le VWAP
- NE PAS trader si : Retour profond vers VWAP, absorption forte opposée, delta neutralisé, rythme cassé

#### 2. Imbalance Pullback Fake
- Pullback qui échoue à reprendre le déséquilibre initial
- Signal d'alerte sur la solidité de la tendance

#### 3. Springbox Imbalance Pullback
- Version renforcée : déséquilibre explosif + volume exceptionnel + pullback court
- Structure : Sortie brutale DVA + bougie large + VWAP se pentifie + delta explosif → Pullback SD2 ou SD1, volume en baisse, delta calme → Reprise agressive (delta repart, rythme préservé)
- Entrée idéale : Pullback SD2 | Stop : sous structure du pullback
- Échec si : Pullback trop profond / Delta neutralisé / Absorption opposée / VWAP s'aplatit

#### 4. Springbox Fake
- Échec avec fort volume suivi d'une accélération opposée
- Le fort volume peut indiquer une absorption institutionnelle dans le sens opposé

#### 5. Extreme Fade
- Contexte : VWAP plate + SD horizontales + rejet SD2/SD3 + marché en rotation
- Si VWAP pentifiée → ce N'EST PAS un Extreme Fade
- Zone d'entrée : SD1 ou SD2 (excès statistique SD3/SD4/SD5)
- Confirmation Order Flow : Absorption (prix stagne mais delta pousse) + Divergence Delta/Prix + Manque de participation + Stop Run proche du 0
- Objectifs : 1) VWAP (principal) | 2) SD opposée
- Stop : Au-dessus de l'extrême (structurel, pas émotionnel)
- NE PAS trader : Fast Trend / Delta Trend fort sans absorption / Breakout avec volume / Déséquilibre préservé

### Module 5 — Qualifier le Rythme

| Rythme | Zone VWAP | Pullback | Retour VWAP | Type de trade |
|---|---|---|---|---|
| **Slow Trend** | VWAP à SD 0.5 / -0.5 | Profond | Fréquent | Pullback VWAP |
| **Normal Trend** | SD 0.5 à SD1 / SD-1 | Modéré | Rare | Continuation |
| **Fast Trend** | SD1 à SD1.5 / -1.5 | Très court | Très rare | Springbox |
| **Speed Trend** | Au-delà de SD 1.5 / -1.5 | Quasi nul | Exceptionnel | Scalps flux |

**Règle : Plus le rythme est rapide → moins le marché revient au VWAP.**
**Erreur fatale : fader un Fast Trend propre.**

### Module 6 — Order Flow & Delta
- Delta = ordres marché acheteurs − vendeurs = agressivité directionnelle réelle
- Delta > 0 → agressifs acheteurs | Delta < 0 → agressifs vendeurs
- **Le Delta ne construit pas la narration. Il valide l'exécution.**
- Ce qu'on cherche : Delta Trend fort | Absorption (divergence prix/delta) | Stop Run proche du 0 | Manque de participation

### Module 7 — Matrice Décisionnelle Desk

| VWAP | Contexte | Narration | Trade |
|---|---|---|---|
| Plate | Extrême SD | Rotation | **Extreme Fade** |
| Plate | Break DVA | Breakout | **Pullback DVA** |
| Pentifiée | Pullback SD2 | Continuation | **Imbalance PB** |
| Pentifiée | Retour VWAP | Reversal | **Cassure rythme** |

### Module 8 — Checklist Trade 4 étoiles (6 critères)

| # | Critère | Question |
|---|---|---|
| 1 | VWAP plate ou pentifiée ? | Équilibre ou déséquilibre ? |
| 2 | Séquence identifiée ? | Équilibrée / Déséquilibrée / Reversale / Breakout ? |
| 3 | Position dans SD ? | SD1 / SD2 / SD3+ ? DVA ou hors DVA ? |
| 4 | Pullback ou excès ? | Imbalance Pullback ou Extreme Fade ? |
| 5 | Rythme qualifié ? | Slow / Normal / Fast / Speed ? |
| 6 | Delta confirmé ? | Agressifs dans le bon sens ? Absorption ? Stop Run ? |

- 6/6 critères → Trade 4 étoiles
- 5/6 → Trade 3 étoiles
- 4/6 → Trade 2 étoiles (prudence)

### Application sur le VwapNarrationIndicator (à implémenter)
- Afficher VWAP journalière + 5 SD (SD1, SD2, SD3, SD4, SD5) dans les 2 sens
- Détecter automatiquement la séquence (plate/pentifiée → équilibrée/déséquilibrée)
- Qualifier le rythme (Slow/Normal/Fast/Speed) via la position du prix dans les SD
- Vote Phase 1 : Bull si prix > VWAP + séquence déséquilibrée haussière, Bear si inverse, Neutral si équilibrée

---

## Référentiel STC (Schaff Trend Cycle) — Formation EMI

> Source : Formation "STC — Du Niveau Débutant au Niveau Opérationnel" — Easy Money Invest (confidentiel)

### Définition
- STC = oscillateur de momentum dérivé du MACD et du cycle stochastique
- Mesure la vitesse et la phase d'un cycle de tendance
- Fonction principale : détecter début de tendance, continuation, essoufflement
- Paramètres ATAS : Fast Length=23, Slow Length=50, Cycle=10

### Valeurs
- 0–25 → zone basse | 25–75 → zone neutre | 75–100 → zone haute

### Lecture simple
| STC | Lecture |
|---|---|
| Monte | momentum acheteur |
| Baisse | momentum vendeur |
| Plateau haut | tendance mature |
| Plateau bas | pression vendeuse mature |

### Organisation Multi-Timeframe
- Fenêtre 1 minute → Narration : comprendre direction dominante, lire cycles, identifier retournements
- Fenêtre 500 ticks → Décision : détecter début d'impulsion, confirmer breakout, valider absorption
- Fenêtre 100 ticks → Confirmation : précision d'entrée, micro-timing, confirmation d'accélération

### Cycle complet du STC (4 phases)
1. **Compression** : STC bas, marché en pause → préparation d'un mouvement
2. **Impulsion** : STC traverse 25→50 → début du momentum → **meilleure zone d'entrée**
3. **Expansion** : STC proche 75–100 → tendance mature → entrée plus risquée
4. **Épuisement** : STC redescend → momentum diminue → risque pullback/retournement

### 3 Setups opérationnels
- **Setup 1 — Impulsion haussière** : STC 1min monte + STC 500T traverse 25 + STC 100T accélère → entrée long
- **Setup 2 — Impulsion vendeuse** : STC 1min baisse + STC 500T passe sous 75 + STC 100T accélère vers le bas → entrée short
- **Setup 3 — Essoufflement** : prix monte mais STC ne monte plus + STC commence à descendre → perte momentum, prise de profit/retournement possible

### Divergences STC
| Prix | STC | Lecture |
|---|---|---|
| Prix monte | STC baisse | épuisement |
| Prix baisse | STC monte | absorption |
- Divergences apparaissent souvent sur POC, VWAP, high/low du jour

### Combinaison avec autres outils
| Situation | Lecture |
|---|---|
| STC monte + CVD monte | impulsion réelle |
| STC monte + CVD baisse | absorption |
| STC baisse + delta vendeur | accélération |

### Erreurs classiques
1. Trader STC seul
2. Entrer quand STC est déjà à 100
3. Ignorer la structure du marché

### Routine avant trade
1. Lire narration 1 minute → Observer cycle STC
2. Attendre impulsion 500 ticks
3. Entrer avec 100 ticks

---

## Référentiel MACD + CCI (10 secondes) — Formation EMI

> Source : Formation "MACD + CCI 10s" — Easy Money Invest (confidentiel)

### Hiérarchie des timeframes
- **500T** = direction réelle (structure)
- **100T** = préparation / alignement
- **10s** = EXÉCUTION UNIQUEMENT — **Le 10s ne décide JAMAIS du sens**

### Paramètres
- MACD : 26 / 12 / 9
- CCI : période 10

### Lecture MACD (10s)
- Croisement = micro shift momentum | Histogramme = force/ralentissement
- MACD 10s = **filtre**

### Lecture CCI (10s)
- CCI > 0 → biais haussier | CCI < 0 → biais baissier
- CCI > +100 ou < -100 → excès court terme
- Ultra sensible → CCI 10s = **timing**

### Setup opérationnel (3 étapes)
1. **Étape 1** : Direction 500T confirmée (MACD aligné + momentum clair)
2. **Étape 2** : Préparation 100T (alignement en cours)
3. **Étape 3** : Exécution 10s — Direction 500T confirmée + MACD 10s dans le sens + CCI 10s dans le même sens

### Sortie
- MACD se retourne + CCI diverge ou spike inverse → sortie rapide

### Checklist entrée
- Direction 500T claire + Direction 100T claire + Zone valide + Pas de range + MACD 10s aligné + CCI confirme + Impulsion réelle

### Checklist sortie
- Perte momentum + MACD 10s contre + CCI contre

---

## Référentiel Footprint & Cluster Statistique — Formation EMI

> Source : Formation "Footprint & Cluster Statistique — Microstructure, Delta, Imbalances, Absorption, Setups" — Easy Money Invest (confidentiel)

### Footprint vs Carnet d'ordres (DOM)
| Critère | Footprint | Carnet d'ordres |
|---|---|---|
| Type de données | Volumes exécutés | Ordres en attente |
| Temporalité | Passé immédiat | Futur potentiel |
| Mesure | Agressivité | Liquidité |
| Fiabilité | **Plus robuste** | **Manipulable (spoofing)** |
| Utilité | Confirmer absorption/déséquilibre | Anticiper réaction sur niveau |

**Règle pro** : DOM = carte des intentions. Footprint = preuve des combats réellement gagnés.
**En pratique** : DOM pour l'anticipation, Footprint pour la validation.

### Construction d'une barre
- Format : **Bid × Ask** → ex : 45×120 = 45 vendus agressivement, 120 achetés agressivement, Delta = +75
- **Tick Reversal** : barre se clôture quand retracement défini (ex: 5 ticks) → élimine bruit, mieux voir impulsions réelles

### Delta
- **Delta = Ask – Bid** → Positif = acheteurs agressifs dominants | Négatif = vendeurs agressifs dominants
- **Le delta mesure l'agression, pas une direction garantie**
- **Delta Change** = Delta actuel – Delta précédent → détecte accélération, stop run, essoufflement
- Exemple : Prix monte + Delta Change diminue → absorption vendeuse probable

### Imbalances
- **Imbalance** : Ask ≥ 3× Bid **ou** Bid ≥ 3× Ask
- **Single imbalance** : isolée → peut être du bruit, prudence
- **Stacked imbalance** : 3 niveaux consécutifs → pression réelle, signal fort

### Barres Agressives vs Passives
- **Barre agressive** : domination claire d'un côté — zero côté opposé (barre "achevée")
- **Barre passive** : absorption dominante — volume présent sans progression du prix (barre "inachevée")
- **Règle** : Ne pas confondre gros volume et domination réelle

### Absorption
- **Définition** : Delta Fort MAIS prix ne progresse pas → acteur passif absorbe l'agression
- **Signal** : Quand le prix ne suit pas l'agression, il se prépare souvent un mouvement inverse

### Les 5 Patterns d'absorption
| Pattern | Signal clé | Interprétation |
|---|---|---|
| 1 — Absorption | Delta fort + prix statique | Acteur passif absorbe l'agression |
| 2 — Imbalance | Barre basse + Zero + imbalance | Entrée au-dessus du POC bougie |
| 3 — Stack Imbalance | 2-3 imbalances consécutives | Pression réelle — re-test probable |
| 4 — Reprise après absorp. | Fort volume + prix bloqué | Range → achat au-dessus POC |
| 5 — Piège acheteurs | Delta+ persistant + prix qui régresse | Bracket → liquidation → plongeon |

**Pattern 2 (Imbalance)** : 1) Barre plus basse que précédente 2) Zero côté vendeur 3) Imbalance au niveau au-dessus max niveau 3 → Entrée barre suivante au-dessus du POC

### 4 Setups structurés
1. **Retournement par Imbalance** : Test extrême + imbalance dominante + delta extrême + absence de continuation
2. **Stack Imbalance Momentum** : Zone clé + stacked imbalances + delta expansion + pas d'absorption opposée
3. **Divergence Prix/Delta** : Prix en sens inverse du delta dominant (haussière si prix bas mais delta positif)
4. **Divergence Prix/Volume** : Prix plus haut / volume plus faible (ou inverse)

### Hiérarchie de lecture (5 étapes — Module 7)
1. Contexte (range ou tendance ?)
2. Zone clé (VWAP, VAH, VAL, extrême…)
3. Qui frappe ? (Delta)
4. Accélération ? (Delta change)
5. Absorption présente ?
→ **Si un élément manque → pas d'entrée**

### 6 Erreurs classiques
1. Trader le delta seul | 2. Ignorer la structure supérieure | 3. Confondre absorption et faiblesse
4. Entrer sur single imbalance | 5. Oublier la hiérarchie | 6. Ne pas respecter l'ordre Contexte→Zone→Exécution

**Synthèse** : Le contexte décide. Le Footprint déclenche.

---

## Référentiel Matrice de Biais Journalier — Formation EMI

> Source : Formation "Matrice de Biais Journalier — Stratégie complète Niveau Desk" — Easy Money Invest (confidentiel)

### Logique générale
**Narration Journalière → Zone d'intervention + Lecture Directe → Validation Order Flow → Timing → Entrée → Application du risque**

La narration repose sur deux piliers : **Structure du marché (TPO)** + **Gravité institutionnelle (VWAP)**

### BLOC 1 — Narration Intraday

#### Module 1 — Market Profile (TPO)
| Zone | Signification |
|---|---|
| VAH (Value Area High) | Borne haute de la Value Area — résistance majeure |
| VAL (Value Area Low) | Borne basse de la Value Area — support majeur |
| POC (Point of Control) | Niveau avec le plus de TPO — gravité centrale |
| High/Low Range (veille) | Extrêmes de la séance précédente — zones de réaction |
| CVAH/CVAL | Bornes du composite multi-sessions |

- **Prix dans la Value** → marché équilibré | **Prix hors Value** → découverte de prix | **Prix autour du POC** → gravité du marché
- **IB étroite** → potentiel directionnel fort | **IB large** → journée rotationnelle probable

**Types d'ouverture** :
- Open Drive → potentiel Trend Day | Open Test Drive → confirmation direction | Open Rejection Reverse (ORR) → inversion durable | Open Auction → rotation probable

**Patterns Market Profile** :
- FRTS (Fake Range/Trend Setup) → fausse cassure de range → retournement en tendance
- BPB (Breaking Value Pullback) → cassure value + retour → entrée dans sens de la cassure
- RPB (Return Value Pullback) → retour dans la value → rebond sur zone
- BPB Fake → fausse cassure, piège breakout traders | RPB Fake → faux retour, accélération sens initial

#### Module 2 — Dissonances Market Profile
3 types de dissonances :
1. **Value / Composite** : journalière haussière + composite baissier → ambiguïté → réduire taille ou ne pas trader
2. **Coincé entre deux zones** : prix sandwich entre VAH jour et VAH composite → compression → attendre résolution
3. **Profil calme mais rupture ailleurs** : profil en D local mais composite casse niveau important → marché en transition

#### Module 3 — VWAP Gravité Institutionnelle
| VWAP | Rôle | Lecture |
|---|---|---|
| Monthly | Référence structurelle principale | Au-dessus → biais acheteur / En dessous → biais vendeur / Autour → équilibre macro |
| Weekly | Référence intermédiaire | Direction semaine / Zones pullback institutionnel |
| Daily | Lecture intraday | Flux intraday / Timing d'exécution |

**Hiérarchie** : Monthly > Weekly > Daily

**Dissonances VWAP** :
- Monthly et Weekly alignées → tendance claire, trader dans le sens
- Weekly opposée au Monthly → compression, attendre résolution
- Daily opposée au Weekly → pullback intraday, réentrée potentielle

### BLOC 2 — Trading en Lecture Directe

#### Module 4 — Fenêtre 1 Minute (4 stratégies combinées)
1. **Stratégie VWAP** : VWAP Daily — gravité institutionnelle directe
   - Prix au-dessus des VWAP → biais acheteur | En dessous → biais vendeur | Entre → zone d'équilibre
2. **Stratégie MBK (Icebergs)** : ICEBERG M30/M150/M450 + MM209
   - M30 > M150 > M450 → tendance acheteuse | M30 < M150 < M450 → tendance vendeuse
3. **Points Pivots (PP/R1-R2-R3/S1-S2-S3)** : calculés depuis H/B/C précédent — mis à jour chaque matin
   - Afficher Journalier + Hebdomadaire + Mensuel pour maximiser confluence
4. **Niveaux Institutionnels** : 25/50/75/100/500/1000 (et multiples) → ordres institutionnels, stops, liquidités
   - 1000/500/100 → Majeurs | 50 → Forts | 25/75 → Moyens

**Règles de confluence** :
- Minimum 3/4 confluences alignées → Trade valide
- 4/4 alignées → Trade premium (risque/récompense exceptionnel)
- DVA cassée + PP cassé + Niveau institutionnel cassé = signal le plus puissant

#### Confirmation 1ère ligne
| Indicateur | Signal ACHAT | Signal VENTE |
|---|---|---|
| MACD | Croisement en bas + histogramme positif | Croisement en haut + histogramme négatif |
| Schaff Trend Cycle | Ligne jaune franchit la limite verte | Ligne jaune franchit la limite rouge |

### Check-list opérationnelle avant chaque trade
**A — Narration** : Contexte équilibré/déséquilibré ? Scénario principal/alternatif/invalidant ?
**B — Autorisation** : Fenêtre 1 alignée ? Biais VWAP validé ? Moyennes mobiles alignées ? MACD et Schaff alignés ?
**C — Zone** : Prix sur zone utile Fenêtre 2 ? Confluence VWAP/SD + niveau institutionnel + pivot ?
**D — Order Flow** : Fenêtre 3 confirme ? Fenêtre 4 montre imbalance/stack/absorption/accélération valide ?
**E — Flux global** : Fenêtre 5 confirme avec CVD/OBV/Delta Bars ?
**F — Précision** : Fenêtre 7 corrèle MACD 500T/100T ? Fenêtre 6 timing propre ?
**G — Gestion** : Stop derrière niveau/structure ? Objectif défini sur niveau/SD/pivot/VWAP suivant ?

---

## Référentiel Stratégie COT / Protocole d'Entrée — Formation EMI

> Source : Formation "Stratégie Easy Money — Paramètres d'entrée & sortie de trade" — Easy Money Invest (confidentiel)

### Architecture de décision : 5 fenêtres en cascade
| Fenêtre | Indicateurs | Rôle |
|---|---|---|
| 1 — 500 Ticks | MACD + CVD + STC + Heiken Ashi | Contexte directionnel — identifier biais + confirmer direction |
| 2 — Cluster 500T | Imbalance / Stack Imbalance | Validation order flow — Imbalance & Stack Imbalance |
| 3 — 10 Secondes | MACD + CCI | Timing final — synchronisation entrée + gestion sortie immédiate |
| 4 — Footprint 5TR | Delta / Delta Change / Volume | Déclenchement réel — Delta, Absorption, Imbalance, Volume |
| 5 — Carnet d'ordres (DOM) | Matelas / Battle / Sweep / Pay off / Nouvelle dynamique | Validation liquidité |

**Règle absolue** : Chaque fenêtre doit valider avant de passer à la suivante. Un seul signal contradictoire = PAS DE TRADE.

### Paramètres des indicateurs
- MACD : 26 / 12 / 9
- STC : Fast=23, Slow=50, Cycle=10
- Zones ZS MACD : Rouge=-0.20, Vert=+0.20, Orange pointillé=±0.5, Blanc pointillé=±0.9

### Configuration VENTE (SHORT)
**Fenêtre 1 (500T)** :
- Mouvement baissier visible, Doji + bougie pleine vendeuse (rouge), Heiken Ashi rouge
- MACD : croisement des lignes + histogramme en expansion dépassant ZS rouge (-0.20)
- CVD baisse → vendeurs dominants | Divergence → absorption possible
- STC : ligne jaune franchit la limite rouge 75.0 → validation vente confirmée

**Fenêtre 2 (Cluster 500T)** :
- Imbalance vendeuse (Ask dominant sur bougie)
- Stack Imbalance vendeur → continuation baissière probable
- Si imbalance/stack imbalance HAUSSIER → PAS DE TRADE

**Fenêtre 3 (10s)** :
- MACD : croisement en haut (MACD passe sous signal) + histogramme négatif
- CCI négatif → momentum vendeur
- Si MACD contre la direction → attendre repositionnement

**Fenêtre 4 (Footprint 5TR)** :
- Bougie agressive vendeuse avec ZÉRO côté acheteur
- Delta VENDEUR (rouge) — chiffre supérieur en négatif au delta précédent
- Delta Change VENDEUR (rouge)
- Delta et Delta Change en COULEUR VIVE et NON grisée

**Fenêtre 5 (DOM)** :
- Battle = confrontation Va et Vient → ne pas entrer
- Matelas (Mattress) = gros bloc liquidité → actit comme mur → Si matelas TIENT → short | S'il FUIT → ne pas vendre
- Sweep du matelas : Fake sweep (prix revient) → signal d'inversion | Vrai sweep (prix continue) → sortie immédiate si en position
- Pay off : carnet se remplit dans ta direction → signal de gestion sortie
- Nouvelle dynamique = signal OBLIGATOIRE avant entrée — flux doit flipper clairement vers le bas

### Configuration ACHAT (LONG)
**Fenêtre 1 (500T)** :
- Mouvement haussier visible, Doji + bougie pleine acheteuse (verte), Heiken Ashi vert
- MACD : histogramme en expansion dépassant ZS vert (+0.20)
- CVD monte → acheteurs dominants
- STC : ligne jaune franchit la limite verte 25.0 → validation achat confirmée

**Fenêtre 2 (Cluster 500T)** :
- Imbalance acheteuse (Bid dominant), Stack acheteur → continuation haussière probable
- Si imbalance/stack BAISSIER (rouge) → PAS DE TRADE

**Fenêtre 3 (10s)** :
- MACD : croisement en bas (passe au-dessus signal) + histogramme positif
- CCI positif → momentum acheteur

**Fenêtre 4 (Footprint 5TR)** :
- Bougie agressive acheteuse avec ZÉRO côté vendeur
- Delta ACHETEUR (vert) — chiffre supérieur en positif au delta précédent
- Delta Change ACHETEUR (vert)

### Synthèse globale — Protocole d'entrée
| Fenêtre | VENTE (SHORT) | ACHAT (LONG) |
|---|---|---|
| 1 — 500T | Doji + rouge. MACD < -0.20. CVD baisse. STC > 75 | Doji + verte. MACD > +0.20. CVD monte. STC < 25 |
| 2 — Cluster | Imbalance vendeuse + Stack vendeur | Imbalance acheteuse + Stack acheteur |
| 3 — 10s | Croisement en haut. Histo négatif. CCI < 0 | Croisement en bas. Histo positif. CCI > 0 |
| 4 — Footprint | Delta rouge + vif. Delta Change rouge. Zéro côté bid | Delta vert + vif. Delta Change vert. Zéro côté ask |
| 5 — DOM | Seller point + matelas + nouvelle dynamique baissière | Buyer point + matelas + nouvelle dynamique haussière |

### Règles absolues
- Un seul signal contradictoire dans l'une des 5 fenêtres = PAS DE TRADE
- MACD 10s contre le trade = aucune entrée. Attendre le repositionnement
- MACD croise contre position pendant le trade = sortie immédiate
- Delta grisé = signal invalide. Toujours attendre une couleur vive
- Jamais entrer sans matelas derrière ni sans nouvelle dynamique visible

### Gestion des sorties — Secret du carnet
- Ballayage rapide du carnet + prix ne bouge pas ou presque pas = le carnet fait de la place pour un nouveau mouvement
- Si matelas fuit → sortie immédiate
- Pay off déclenché → gérer la sortie

---

## Branche de développement
`claude/wizardly-heisenberg-4dzrw9`

## Dépôt
`wilziakin-art/atas-trading-indicator`

---

## Notes importantes
- L'utilisateur parle **français** — répondre toujours en français
- Instrument principal : **NQ** (Nasdaq Futures)
- Plateforme : **ATAS** (indicateurs C#, SDK ATAS)
- Le `.csproj` n'est pas encore mis à jour pour les nouveaux dossiers Phase1/Phase2/Phase3
- Aucun test unitaire pour l'instant
