---
name: deploy
description: Deploy Starbook (this bookmarks app) to its server, or check a running deployment. Use when asked to deploy, ship, release, update the server, or when a change should go live.
---

# Deploy

The app runs as one Docker container on a private server, built on the server from committed source. The host
name, SSH user, ports and paths are **not** in this public repo: they are in the deployer's private global
`CLAUDE.md` (the home-server section). Read them from there.

## Shape on the server

```
/srv/starbook/
  compose.yml   # build: ./src, ports "127.0.0.1:<port>:8080", volumes ./data:/data, restart: unless-stopped
  src/          # git archive of HEAD, replaced whole on each deploy
  data/         # spaces + images, owned by uid 1654 (the container's user)
```

The app has **no login**. It is published on `127.0.0.1` only and reached through `tailscale serve` (HTTPS,
tailnet only). Never publish it on `0.0.0.0` or through Funnel.

## Deploy

1. Commit first: only committed code ships, so uncommitted changes cannot leak into a deploy.
2. Run `dotnet test`, and `npm run build` in `web` (type-check).
3. Ship and build (Git Bash; substitute the host):
   ```bash
   git -c core.autocrlf=false archive --format=tar.gz -o "$TEMP/starbook-src.tar.gz" HEAD
   scp "$TEMP/starbook-src.tar.gz" <host>:/tmp/
   ssh <host> 'set -e
     sudo mkdir -p /srv/starbook/src.new
     sudo tar -xzf /tmp/starbook-src.tar.gz -C /srv/starbook/src.new && rm /tmp/starbook-src.tar.gz
     sudo rm -rf /srv/starbook/src && sudo mv /srv/starbook/src.new /srv/starbook/src
     cd /srv/starbook && sudo docker compose up -d --build'
   ```
   - `core.autocrlf=false`: otherwise the archive carries CRLF line endings from Windows.
   - Extract into `src.new` and swap, so files deleted from the repo do not linger in the build context.
   - The Native AOT build takes a few minutes on the server.
4. Verify: `sudo docker ps` shows `(healthy)` after ~15 s, and the tailnet URL loads.

Open browser tabs keep the previous version's data until reloaded; a save from such a tab is refused (409) and
the page reloads itself.

## First-time setup (already done)

`sudo chown 1654:1654 /srv/starbook/data`, then
`sudo tailscale serve --bg --https=<tls-port> http://127.0.0.1:<port>`. `/srv` is in the nightly backup; the
data files are written via temp file + rename, so no pre-backup hook is needed.
