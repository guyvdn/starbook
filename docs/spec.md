# Starbook — spec

A self-hosted start page for bookmarks, split into **spaces** so each browser profile shows only its own
subset. It replaces a Speed Dial 2 setup: its tab/tile model, plus spaces above and optional sections within a tab, and without the
features that went unused.

## Goals

- **Fast.** The page opens on every new tab. One small HTML response, images from cache, no framework.
- **Maintainable from anywhere.** Add, edit, reorder and move tiles from any device that can reach the server.
- **One space per browser profile.** A personal profile, a work profile and a customer profile each open their
  own space by URL.
- **Self-contained.** The server stores every image itself; the page never loads from a third party.

## Non-goals

- Visit counters, time-of-day statistics, launcher, sidebar, browser-bookmark sync.
- Page screenshots as thumbnails.
- One tile shared across spaces. A tile needed in two spaces is duplicated.
- Multiple users, accounts or sharing.
- A browser extension of our own. The new tab is pointed at a space URL with an existing
  "custom new tab URL" extension (see [Open questions](#open-questions)).

## Model

```mermaid
erDiagram
    SPACE ||--o{ TAB : contains
    TAB ||--|{ SECTION : contains
    SECTION ||--o{ TILE : contains
    TILE }o--o| IMAGE : shows
```

| Entity | Fields | Notes |
|---|---|---|
| Space | `slug`, `name`, `version`, `tabs[]`, `color?` | One JSON file per space. `slug` is the URL segment. One per browser profile. `color` is one of 12 palette hexes (`SpaceValidator.Colors`, `web/src/theme.ts`); absent means the default blue. |
| Tab | `id`, `title`, `sections[]` | One tab visible at a time. |
| Section | `id`, `title`, `tiles[]` | A titled block of tiles within a tab. `title` may be `null`. |
| Tile | `id`, `title`, `url`, `image` | `image` is an image reference or `null`. |
| Image | content hash + extension | Stored once, shared by any tile that references it. |

**Sections are optional in practice.** Every tab has at least one section, but a tab whose only section has no
title renders as a plain grid of tiles, with no heading. A small tab stays flat; a large one can be split into
titled sections without changing anything else.

**Order is array order.** Tabs, sections and tiles are stored in display order; there is no `position` field.
Reordering, or moving a tile to another section, tab or space, saves the affected arrays.

Example `data/spaces/personal.json`:

```json
{
  "slug": "personal",
  "name": "Personal",
  "version": 42,
  "tabs": [
    {
      "id": "b_7k2m",
      "title": "Home",
      "sections": [
        {
          "id": "s_4hd0",
          "title": null,
          "tiles": [
            { "id": "t_9xq1", "title": "Example", "url": "https://example.com/", "image": "3f2a…c1.png" }
          ]
        }
      ]
    }
  ]
}
```

## Storage

JSON files on disk, not a database. The access pattern is "read a whole space, write a whole space"; a few
hundred tiles per space is well under a megabyte. SQLite would add a dependency, a backup hook and friction
with Native AOT without buying anything at this size. Revisit if edit history, multiple users or cross-space
queries become requirements; migrating is a one-off import.

```
data/
  spaces.json            # space order and names
  spaces/<slug>.json     # one file per space
  images/<sha256>.<ext>  # content-addressed, immutable
```

- **Atomic writes:** write to a temp file in the same folder, then rename over the original.
- **Optimistic concurrency:** each space carries a `version`. A save sends the version it was based on; a
  mismatch is rejected with `409 Conflict` so two devices cannot silently overwrite each other.
- `data/` is a mounted volume, never committed, and is what the backup covers.

## Architecture

```mermaid
flowchart LR
    B[Browser profile<br/>new tab → /spaces/slug] -->|HTML with space JSON embedded| S[ASP.NET Core<br/>Native AOT]
    B -->|images, cached immutable| S
    S --> D[(data/ volume<br/>JSON + images)]
    S -->|fetch icon on add| W[Bookmarked site]
```

- **Backend:** ASP.NET Core minimal API on .NET 10, published with Native AOT. Single small binary, fast
  start, low memory. `System.Text.Json` with source generation.
- **Frontend:** plain TypeScript, built with Vite, no UI framework. Served as static files by the backend.
- **Deploy:** one Docker image, `data/` as a volume.
- **Access:** the server is reachable only on a private network (e.g. a Tailscale tailnet, with
  `tailscale serve` providing HTTPS). There is no login in the app; network access is the authorisation.
  Deployment specifics live outside this repo.

### Endpoints

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/` | Redirect to the first space. |
| `GET` | `/spaces/{slug}/{tab?}` | The start page, with the space JSON embedded in the HTML — no second request. The page picks the tab; an unknown one shows the first. The prefix keeps slugs clear of `/api`, `/img` and `/assets`. |
| `GET` | `/s/{slug}` | Permanent redirect to `/spaces/{slug}` (the original address). |
| `GET` | `/api/spaces` | Space list (slug, name, order). |
| `GET` | `/api/spaces/{slug}` | One space. |
| `PUT` | `/api/spaces/{slug}` | Save a space; requires the base `version`, `409` when stale. |
| `POST` | `/api/spaces` · `DELETE` `/api/spaces/{slug}` | Create / remove a space. |
| `POST` | `/api/spaces/{slug}/missing-icons` | Fetch icons for every tile without an image (6 at a time) and save them onto the latest version. |
| `POST` | `/api/icons` | Fetch the best icon and the page title for a URL; store the icon, return both. |
| `POST` | `/api/images` | Upload or paste a custom image (raw body, max 1 MB), store it, return the image reference. |
| `GET` | `/img/{hash}.{ext}` | Serve an image, cached as immutable, with `nosniff` and a sandboxing CSP so an SVG opened directly cannot run script. |

## Images

Every image is stored on the server, never hot-linked. This also keeps third parties from learning what is
bookmarked.

- **On add**, the server reads the page and collects candidates from `<link rel="icon">`,
  `apple-touch-icon`, the web app manifest (skipping maskable/monochrome icons) and `/favicon.ico`. An SVG
  wins, then the largest bitmap; the first candidate that downloads as a real image is kept. The page title
  is returned too and fills an empty tile title. If nothing usable is found the tile shows a letter tile.
- **Sign-in redirects are ignored.** Signed out, many sites bounce to an identity provider on another host
  (`calendar.google.com` to `accounts.google.com`). That page's icon and title belong to the provider, so
  the fetch drops it and only tries `/favicon.ico` on the requested host. The tile gets a letter tile
  instead of a wrong logo; upload a custom image for these.
- The image format is sniffed from the bytes (PNG, JPEG, GIF, WebP, ICO, SVG); file names and Content-Type
  headers are not trusted.
- **Search logos** in the tile dialog, for when the fetched icon is wrong or missing. The server queries the
  Iconify API, limited to the open-licensed brand sets SVG Logos, theSVG and selfh.st icons: vector, transparent
  background, brand colours. The query is prefilled from the title or host, and the user picks a result, which
  is stored like an upload. Only the typed query leaves the server, never the bookmarked URL. Previews and the
  picked SVG go through `GET /api/logos` and `GET /api/logos/svg`, which validate the id and cap the size.
- **Custom image** per tile, by upload or paste, overrides the fetched one. This is how several tiles for the
  same site are told apart.
- **Re-fetch** on demand per tile.
- **Fetch missing icons** for a whole space, from the space menu in edit mode. Also what the import uses.
- Images are content-addressed (`sha256`), so the same icon used by many tiles is stored once. Unreferenced
  images can be swept.

The icon fetch is server-side request handling of arbitrary URLs. Guard it: `http`/`https` only, a short
timeout, a response size cap, and an image content-type check. Fetching LAN addresses is allowed on purpose
(intranet bookmarks), which is acceptable only because the server is not publicly reachable.

## Start page

- Midnight Blue / Cyan theme, dark only: deep navy with glowing blue waves along the bottom, edge to edge, drifting slowly (two transform-only layers, still under reduced motion). The
  header holds the bold space name, a short divider, then the tabs as plain text; the active tab is white and
  bold with a glowing cyan underline. Section headings are bold and cyan. Tiles are fixed-size (132 x 110)
  gradient cards with a 52px logo on top and the title inside, under it; dark logos get a faint light edge so
  they stay visible. A hovered tile lifts and its border turns cyan. A tile without an image shows a letter
  badge in one of seven accents per site.
- A space can have a colour (edit mode, space menu, "Colour…"; 12 swatches plus the default). It sets one CSS
  variable, `--accent`, and `:root[data-themed]` in `style.css` derives the backgrounds, cards, borders, waves and
  the text colour on filled buttons from it. The start page sets it in the `<head>` before first paint, so a
  themed space never flashes the default colours.

- The space name, top left, is also the space switcher. The other spaces are listed only when it is opened,
  so they stay out of sight while sharing a screen with one customer.
- Tabs sit next to it; the active tab is in the path, `/spaces/personal/tools`, derived from the tab title (`-2`, `-3` for duplicate titles). Renaming a tab changes its address. Switching tabs is in-page (`pushState`), so Back returns to the previous tab.
- A search box at the right of the header finds a bookmark by name across **every tab** of the space. It matches
  the title and the host, accent- and case-insensitive, every word of the query must match, and a title that
  starts with the query ranks first. Results show the logo, title and `Tab › Section`; Up/Down select, Enter opens
  (Ctrl/Cmd+Enter in a new tab), Escape clears, and `/` focuses the box from anywhere. It runs in the browser on the
  space already loaded, so it needs no endpoint. In edit mode a result opens the tile's dialog instead.

## Editing

- **Edit** is an item in the space menu (click the space name), so the start page shows no edit controls.
  In edit mode a **Done** button appears top right, and the menu offers **Done editing**.
- A single faint **+** after the last section adds a bookmark without entering edit mode; when the tab has
  several sections, the dialog asks which one (default: the last).
- Add, edit and delete tabs, sections and tiles inline on the start page. In edit mode, clicking a tile opens
  its dialog (URL, title, image, move/copy, delete); clicking the active tab renames it.
- Drag and drop to reorder tiles, move them between sections, and reorder sections and tabs. Dropping a tile
  on a tab moves it to that tab's first section.
- Move or copy a tile to another space.
- Saves go through `PUT /api/spaces/{slug}`; on `409` the page reloads the space and tells the user.
  Changes apply locally at once and are saved in the background; rapid edits coalesce into one save.
- Space rename, create and delete live in the space switcher, in edit mode only.

## Import from Speed Dial 2

A one-off import of a Speed Dial 2 JSON export (`dials[]`, `groups[]`, `preferences`), from the space menu in
edit mode. It runs in the browser against the normal API, so the server needs no import code:

- Each group is mapped to an existing space, or skipped. Create the spaces first.
- Each Speed Dial group becomes a tab with one untitled section. Splitting into sections is done afterwards
  in the app.
- Keeps group and tile order from the export's `position` fields.
- Keeps `title` and `url`. Drops visit counters, timestamps and preferences.
- **Ignores the exported thumbnails.** They are page screenshots or links to hosts that are gone or will go.
  Every imported tile gets a freshly fetched icon instead.
- Tiles without an http(s) URL are skipped and counted. Tiles sharing a `position` keep creation order.
- Afterwards it runs **Fetch missing icons** per space. A test run on a real export of ~900 tiles took two
  minutes and found an icon for 80%; the misses are mostly sites behind a login redirect or bot protection.
- Importing twice adds the tabs twice.
- The export file is read from outside the repo and is never committed.

## Open questions

- Which "custom new tab URL" extension for Edge to use — chosen on security: minimal permissions, open source,
  maintained.
