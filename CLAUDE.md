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
