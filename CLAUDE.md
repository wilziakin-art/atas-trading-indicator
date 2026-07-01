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

## Paramètres VWAP
> ⚠️ À compléter — en attente de la notice de setup VWAP de l'utilisateur

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
