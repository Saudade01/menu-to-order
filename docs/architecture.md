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

A field is published only above the configured threshold. The threshold starts
high and comes down only when the correction log shows it can.

## Why two passes instead of one

A single model call sounds equally confident whether it is right or wrong. Two
calls with different prompts tend to disagree exactly where the input is
ambiguous, and that disagreement is the signal. It costs one extra call per
menu, which is fine because a menu is extracted once and then served on every
order.

## Data model (first cut)

- `Restaurant`: owner, slug, contact
- `MenuVersion`: one extraction run, immutable once published
- `MenuItem`: name, description, price (decimal), category, confidence fields
- `ExtractionRun`: prompt version, input hash, raw response, timing, cost
- `Correction`: field, old value, new value, who, when

`Correction` makes accuracy measurable. From it we get first-pass field
accuracy, the most common error types, and whether a prompt change helped.

## Open questions

- Payment is out of scope for v1. The order page produces an order, and how it
  gets paid is left for later.
- Menus with Turkish and English side by side are common in tourist areas. v1
  does not handle them.
- Deskewing or boosting contrast before extraction might lower the flagged rate.
  Measure that before building it.
