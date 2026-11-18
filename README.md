# Bookmarks

A fast, self-hosted bookmark start page, split into spaces so each browser profile gets its own subset.

See [docs/spec.md](docs/spec.md) for the design.

## Layout

| Path | What |
|---|---|
| `src/Bookmarks.Api` | ASP.NET Core minimal API, .NET 10, Native AOT. Serves the page, the API and the images. |
| `web` | Plain TypeScript frontend, built by Vite into `src/Bookmarks.Api/wwwroot`. |
| `tests/Bookmarks.Api.Tests` | xUnit v3 tests for storage and validation. |
| `data` | Runtime data in development. Never committed. |

## Develop

Requires the .NET 10 SDK and Node.js 22+.

```sh
dotnet run --project src/Bookmarks.Api   # API on http://localhost:5080, data in ./data
cd web && npm install && npm run dev      # page on http://localhost:5173, proxies the API
```

Open <http://localhost:5173/s/personal>. An empty data folder is seeded with a `personal` space.

To run the production setup locally, build the frontend once (`npm run build` in `web`) and open
<http://localhost:5080/> instead: the API then serves the page with the space embedded.

```sh
dotnet test
```

## Run

```sh
docker build -t bookmarks .
docker run -d --name bookmarks -p 127.0.0.1:8080:8080 -v bookmarks-data:/data bookmarks
```

- The app has **no login**. Only expose it on a private network, for example behind `tailscale serve`.
- Everything the app stores is in `/data`; back that up.
- The container runs as uid `1654`. A bind mount instead of a named volume must be writable by that uid
  (`chown -R 1654:1654 <dir>`).
