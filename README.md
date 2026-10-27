# menu-to-order

[![ci](https://github.com/Saudade01/menu-to-order/actions/workflows/ci.yml/badge.svg)](https://github.com/Saudade01/menu-to-order/actions/workflows/ci.yml)

Turn a photo of a paper menu into a working QR order page.

A restaurant owner photographs the printed menu, the service reads it, and a
couple of minutes later the owner has an ordering page without typing anything in.

Reading the menu is the easy part. Most of this codebase deals with the cases
where the model reads it wrong.

## Why model mistakes matter here

Vision models misread menus. `180 ₺` comes back as `18 ₺`, a section header
turns into a dish, and two items with the same name get merged into one. A wrong
price on a live menu costs money on every order until someone notices it.

So the system follows one rule: nothing reaches a live page unless it has been
verified or confirmed by a human.

## How verification works

Each extracted field gets a confidence score built from three independent
signals:

1. Cross-pass agreement. The menu is extracted twice with different prompts.
   Where the passes disagree, the field is marked and both candidate values are
   kept.
2. Rule checks. A price has to parse, fall inside a plausible range, and carry a
   currency. Item names have to be unique within a category, and categories have
   to come from the set detected on the menu.
3. Model self-report. The model marks fields it read from blurred, cropped, or
   low-contrast parts of the photo.

Fields that pass all three are published. The rest go to a review queue where
the owner confirms or corrects them. Every correction is logged, so first-pass
accuracy is something the system measures over time.

## Results on the sample menus

The three sample menus are rendered by `samples/render.py`, so the right answer
for every field is known. Scored against responses recorded from
gemini-3.8-flash, before the owner has touched anything:

| Menu | Dishes | Fields | Read right | Right but flagged | Ambiguous, flagged | Misread, flagged | Reached the page unflagged |
|---|---|---|---|---|---|---|---|
| Köşe Lokantası (clean print) | 11 | 44 | 44 | 0 | 0 | 0 | 0 |
| Sahil Kafe (awkward prices) | 11 | 44 | 37 | 4 | 3 | 0 | 0 |
| Mangal Evi (glare and blur) | 10 | 40 | 37 | 3 | 0 | 0 | 0 |
| All | 32 | 128 | 118 | 7 | 3 | 0 | 0 |

The three prices the menus themselves leave open (a half and full portion,
"Sorunuz", a dollar price) were all flagged. Seven correct fields went to the
owner anyway: two dishes sharing a name, a price printed without a currency,
three prices the model marked as hard to read, and one dish name that the second
pass misread as "kiőilik" instead of "kişilik". The first pass had it right, the
passes disagreed, and the owner gets both readings to pick from. That is the two
reads doing their job: no draft value was wrong, which is why the misread column
is empty. These are clean renders, and real phone photos will be harder.

`AccuracyTests` fails CI if a wrong or ambiguous field ever gets through
unflagged. The generated table lives in `docs/accuracy.md`.

The free tier's newest models are often too busy to answer, so the app asks
gemini-3.6-flash first and moves on to gemini-3.5-flash and then
gemini-3.1-flash-lite. Recording the samples on a busy evening, all six answers
came from gemini-3.1-flash-lite. It read every price right and sent only two
correct fields to the owner, but on Sahil Kafe both reads folded the small
"Kaşarlı" note into the dish name, so "Köfte Kaşarlı" reached the draft without
a question. The price was right and nothing a guest pays changed, but it shows
the limit of the design: two reads catch a slip in one of them, not a misreading
they share. The recordings in the repo stay the gemini-3.8-flash ones, because
they still hold every trap the tests are built around.

## A real photo

The first real test was a handwritten whiteboard outside a lokanta (the photo
is not in the repo). All 33 dishes were found and every price was read
correctly. It also showed gaps that the clean samples had hidden:

- The two passes filed two dishes under different headings, and matching by
  heading and name split each of them in two. Matching now falls back to the
  name, and only the heading gets flagged.
- Dishes written outside any heading were each asked about. Now only a heading
  that is not on the menu is questioned.
- A note saying the mantı is served on Fridays only was dropped. The prompt now
  asks for notes like that, and they show up under the dish.

Re-recording the samples with the new prompt turned up two more. One pass
misread a letter in a name, which split that dish too; names that differ by a
letter or two in the same section now pair up, with the name flagged. The other
pass spelled "ş" as an "s" followed by a combining cedilla, so all text is now
normalised to NFC before it is stored.

Reading the same board again on a busy day turned up two more. The free tier
answered 503 often enough that one pass sometimes never came back, and with
nothing to compare against, the owner was asked about all 132 fields. A busy
pass is now tried on the next model, and if it still has no answer nothing is
saved and the owner is asked to read the photo again. Most of the remaining
questions were about spelling: "TEK ANA YEMEK" against "TEK ANAYEMEK", "GAZÖZ"
against "GAZOZ", a dotted İ. Names, sections and notes are now compared without
case, spacing, punctuation or accents, while prices are still compared as
amounts. On the same two answers the questions went from 13 to 2, and both left
are real doubts about which dishes a "PORSİYON" note covers.

## What it looks like

The interface is in Turkish, for the owners and guests it is built for.

| Owner review | Table menu on a phone | Incoming orders |
|---|---|---|
| ![Owner review page with flagged fields](docs/screenshots/review.png) | ![Table 4 menu on a phone](docs/screenshots/menu.png) | ![Owner order list](docs/screenshots/orders.png) |

## Stack

| Layer | Choice |
|---|---|
| API | ASP.NET Core Minimal API (.NET 10) |
| Data | PostgreSQL + Entity Framework Core |
| Vision / extraction | Gemini Flash |
| Order page | Server-rendered Razor components |
| Local dev | Docker Compose (Postgres) |

## Run it locally

You need the .NET 10 SDK and Docker.

```bash
docker compose up -d
dotnet run --project src/Api
```

Without an API key the app replays Gemini responses recorded from real calls, so
the three photos in `samples/menus` work straight away. To read your own photos,
create a free key in Google AI Studio and store it with user-secrets:

```bash
dotnet user-secrets set "Gemini:ApiKey" "<your key>" --project src/Api
```

`dotnet test` runs the whole suite, including the database tests, which start
their own Postgres container.

The `Dockerfile` builds a single image for any host. It listens on port 8080 and
needs `ConnectionStrings__Menu`, `Gemini__ApiKey` and a volume at `/data`; without
a key it refuses to start rather than quietly replaying the samples. Each address
can upload ten photos an hour and send thirty orders a minute, so a stranger
cannot spend the Gemini quota or fill the owner's order list.

## Status

The whole path works locally: upload, review, publishing, per-table QR codes, and
ordering from the table page with the total worked out on the server from the
published prices. The design is in `docs/architecture.md`.

## Why it exists

To get a small restaurant's menu online without anyone retyping it, and without
a wrong price slipping through.

Plenty of cafés and lokantas have a printed menu and nothing else. Putting it
online usually means entering every dish and price by hand, and a typo in a
price costs money on every order until someone spots it. Here the owner takes
one photo, answers the few questions the system is unsure about, and prints a
QR code for each table. Guests order from their phones, the owner sees each
order with its table and note, and nothing goes live that the owner has not had
the chance to check.
