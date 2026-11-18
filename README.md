# Starbook

A fast, self-hosted bookmark start page, split into spaces so each browser profile gets its own subset.

See [docs/spec.md](docs/spec.md) for the design.

## Layout

| Path | What |
|---|---|
| `src/Starbook.Api` | ASP.NET Core minimal API, .NET 10, Native AOT. Serves the page, the API and the images. |
| `web` | Plain TypeScript frontend, built by Vite into `src/Starbook.Api/wwwroot`. |
| `tests/Starbook.Api.Tests` | xUnit v3 tests for storage and validation. |
| `data` | Runtime data in development. Never committed. |

## Develop

Requires the .NET 10 SDK and Node.js 22+.

```sh
dotnet run --project src/Starbook.Api   # API on http://localhost:5080, data in ./data
cd web && npm install && npm run dev      # page on http://localhost:5173, proxies the API
```

Open <http://localhost:5173/spaces/personal>. An empty data folder is seeded with a `personal` space.

To run the production setup locally, build the frontend once (`npm run build` in `web`) and open
<http://localhost:5080/> instead: the API then serves the page with the space embedded.

```sh
dotnet test
```

## Run

```sh
mkdir -p data && sudo chown 1654:1654 data   # the container runs as uid 1654
STARBOOK_PORT=8080 docker compose up -d --build
```

- The app has **no login**. [compose.yml](compose.yml) publishes it on `127.0.0.1` only; put a private network
  in front of it, for example `tailscale serve --bg --https=<port> http://127.0.0.1:<port>`.
- Everything the app stores is in `data/`: JSON files and images, always written via temp file + rename, so a
  plain file backup is consistent while the app runs.
- The image has a healthcheck (`/healthz`: data folder loaded and writable).
