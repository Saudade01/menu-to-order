# menu-to-order

[![ci](https://github.com/Saudade01/menu-to-order/actions/workflows/ci.yml/badge.svg)](https://github.com/Saudade01/menu-to-order/actions/workflows/ci.yml)

Turn a photo of a paper menu into a working QR order page.

A restaurant owner photographs the printed menu, the service reads it, and within
a minute the owner has an ordering page without typing anything in.

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

## Status

The whole path works locally: upload, review, publishing, per-table QR codes, and
ordering from the table page with the total worked out on the server from the
published prices. The design is in `docs/architecture.md`.

## Why it exists

A portfolio project, built in the open. The focus is production LLM work where
most of the effort goes into catching the model's mistakes.
