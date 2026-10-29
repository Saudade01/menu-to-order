# menu-to-order

[![ci](https://github.com/Saudade01/menu-to-order/actions/workflows/ci.yml/badge.svg)](https://github.com/Saudade01/menu-to-order/actions/workflows/ci.yml)

Turn a photo of a paper menu into a QR order page that guests use from their table.

1. The owner uploads one photo of the printed menu, or of the board by the door.
2. The app reads the menu twice and asks the owner about whatever the two reads
   disagree on or whatever looks wrong. It never guesses a price.
3. Each table gets its own QR code. Guests order from their phones, and the
   owner sees every order with its table and note.

| Owner review | Table menu on a phone | Incoming orders |
|---|---|---|
| ![Owner review page with flagged fields](docs/screenshots/review.png) | ![Table 4 menu on a phone](docs/screenshots/menu.png) | ![Owner order list](docs/screenshots/orders.png) |

The interface is in Turkish, for the cafés and lokantas it was built for.

## Why it exists

Plenty of cafés and lokantas have a printed menu and nothing else. Putting it
online usually means typing in every dish and price, and a typo in a price
costs money on every order until someone spots it. Here nobody retypes the
menu, and nothing goes live that the owner has not had the chance to check.

The rest of this page is for developers. Reading the menu is the easy part;
most of the code deals with the cases where the model reads it wrong.

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
the owner confirms or corrects them. A doubt shared by several dishes is asked
once, except for prices, which are always asked dish by dish. Every correction
is logged, so first-pass accuracy is something the system measures over time.

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

## On a real phone

Testing on an iPhone, with the menu opened from the Google app, found three
more:

- The Google app's own browser silently drops every form post, so no answer,
  publish or order reached the server. Forms are now sent with fetch, and still
  work as plain forms without JavaScript.
- In that browser, tapping a section tab reloaded the page and emptied the
  cart. Tabs now scroll in place, and the cart is kept in the tab until the
  order is placed.
- A phone photo records where it was taken. It is now redrawn in the browser
  before upload, which drops the location, keeps the photo upright and shrinks
  it.

## Where to start reading

- `src/Verification/MenuDraft.cs` pairs the two reads and decides what to ask.
- `src/Verification/VerificationGate.cs` is the only code that can publish a menu.
- `src/Api/Intake/MenuIntake.cs` reads in the background, moves a busy read on
  to the next model and logs every call.
- `tests/MenuToOrder.Tests/Verification/AccuracyTests.cs` scores the drafts
  against the known answers and fails CI if a wrong field gets through.
- `docs/architecture.md` covers the design and the reasons behind it.

## Stack

| Layer | Choice |
|---|---|
| API | ASP.NET Core Minimal API (.NET 10) |
| Data | PostgreSQL + Entity Framework Core |
| Vision / extraction | Gemini Flash |
| Order page | Server-rendered Razor components |
| Tests | xUnit, Testcontainers (real PostgreSQL), recorded Gemini responses |
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

The whole path works locally and has been tested end to end on an iPhone:
upload, review, publishing, per-table QR codes, and ordering from the table page
with the total worked out on the server from the published prices.
