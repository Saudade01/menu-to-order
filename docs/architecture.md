# Architecture

## Flow

```
photo upload
    │
    ▼
┌─────────────────┐
│  Extraction     │   two passes, different prompts
│  (Gemini)       │   raw responses stored verbatim
└────────┬────────┘
         │  two candidate menus
         ▼
┌─────────────────┐
│  Verification   │   cross-pass diff
│                 │   rule checks (price, currency, uniqueness, category)
│                 │   model self-reported legibility
└────────┬────────┘
         │  per-field confidence
         ▼
    ┌────┴─────┐
    │          │
  clear      flagged
    │          │
    │          ▼
    │   ┌─────────────┐
    │   │ Review queue│  owner confirms or corrects
    │   └──────┬──────┘
    │          │  correction logged
    ▼          ▼
┌─────────────────┐
│ VerificationGate│   the only writer to published menus
└────────┬────────┘
         ▼
   published order page  (QR)
```

## Confidence model

Each field gets a score in `[0, 1]`, combined from three signals. The weights
live in configuration so they can be tuned against the correction log.

| Signal | What it catches |
|---|---|
| Cross-pass agreement | Hallucinated items, OCR slips on digits |
| Rule checks | Impossible prices, missing currency, duplicate names |
| Self-reported legibility | Blur, glare, cropped edges, handwriting |

A field is published only above the configured threshold. The starting weights
are 0.4 for agreement, 0.4 for rules and 0.2 for legibility, against a threshold
of 0.9, so any single failing signal sends the field to the owner. The threshold
comes down only when the correction log shows it can.

Prices printed without a currency are common on Turkish menus. Rather than ask
about each one, the owner answers once per menu whether its prices are in lira.

Names, sections and notes are compared without case, spacing, punctuation or
accents, so "TEK ANA YEMEK" and "TEK ANAYEMEK" count as the same reading.
Prices are compared as parsed amounts.

## Why two passes instead of one

A single model call sounds equally confident whether it is right or wrong. Two
calls with different prompts tend to disagree exactly where the input is
ambiguous, and that disagreement is the signal. It costs one extra call per
menu, which is fine because a menu is extracted once and then served on every
order.

A pass that Gemini answers with "busy" (503, 429, 500) or a network error is
tried three more times, 5, 15 and 30 seconds apart, and every try is logged. On
the free tier each model runs out of capacity and daily quota on its own, so
each retry goes to the next model in `Gemini:Models` (by default
gemini-3.6-flash, then gemini-3.5-flash, then gemini-3.1-flash-lite). All three
read every price on the real whiteboard right, and gemini-3.1-flash-lite did on
the sample menus too. The lite models sometimes fold a printed note into the
dish name ("Köfte Kaşarlı"); when both reads do it, nothing flags it. If a pass
still has no answer, nothing is saved: with one read there is nothing to
compare, and asking the owner about every field of every dish is more work than
taking the photo again.

## Data model (first cut)

- `Restaurant`: name, slug, owner token hash, table count
- `MenuVersion`: a draft built from two extraction passes, immutable once published
- `MenuItem`: name, category, description, price (decimal, empty when unreadable)
- `ExtractionRun`: pass, prompt version, model, input hash, raw response, timing, token counts
- `Flag`: field, reason, both pass values, confidence, when it was resolved
- `Correction`: field, old value, new value, when

`Correction` makes accuracy measurable. From it we get first-pass field
accuracy, the most common error types, and whether a prompt change helped.

## Open questions

- Payment is out of scope for v1. The order page produces an order, and how it
  gets paid is left for later.
- Menus with Turkish and English side by side are common in tourist areas. v1
  does not handle them.
- Deskewing or boosting contrast before extraction might lower the flagged rate.
  Measure that before building it.
