# AmiDiskLab ScreenScraper proxy

This Cloudflare Worker keeps the AmiDiskLab ScreenScraper developer credentials
outside the desktop application. It accepts only a fixed set of ScreenScraper
operations and replaces media URLs with short-lived encrypted proxy URLs.
The allow-list includes `ssuserInfos.php` so the desktop client can validate an
optional user account and obey its thread and request quotas.

## Dashboard deployment

1. Create a Worker named `amidisklab-api` using the Hello World option.
2. Open **Edit code**, replace the example with `worker.js`, and deploy it.
3. Under **Settings → Variables and Secrets**, add these encrypted secrets:
   - `SCREENSCRAPER_DEVELOPER_ID`
   - `SCREENSCRAPER_DEVELOPER_PASSWORD`
   - `MEDIA_TOKEN_SECRET` (a new random value of at least 32 bytes)
4. Open `https://<worker-name>.<account>.workers.dev/health`. It must return a
   JSON response with `"status":"ok"`.

Never add real secret values to Git, `wrangler.toml`, screenshots, logs, issues,
or support messages.

The desktop application uses the deployed proxy at
`https://amidisklab-api.c-schaef.workers.dev`. Change `ScreenScraperClient.ProxyRoot`
when deploying a separate instance.
