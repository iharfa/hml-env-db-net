# Hulhumalé Environmental Dashboard (.NET)

A browser-based environmental monitoring dashboard for Hulhumalé, Maldives —
air quality, weather, tide and reef conditions with a plain-language daily
briefing. This is the .NET port of the original JavaScript dashboard
([hml-env-db-v2](https://hml-env-db-v2.vercel.app)): same UI, same data sources,
but the application code is C# running as **Blazor WebAssembly**.

Live: **https://hml-env-db-net.vercel.app**

## Architecture

```
src/HmlEnvDb/            C# — the application (compiled to WebAssembly)
  Program.cs             Boots the app; no Razor tree — C# drives the original
                         static markup through JS interop, mirroring app.js 1:1
  Dashboard.cs           loadAll() orchestration, refresh, event callbacks
  Fetch.cs               All HTTP clients (Open-Meteo, NOAA, /api/*)
  CoralData.cs           NOAA virtual-station text parsing + alert scale
  Briefing.cs            Daily-briefing advisory rules + hero-card data
  Sections.cs            Weather / AQ / marine cards, charts, status table
  CoralSection.cs        Coral info boxes, DHW chart, CORS-blocked fallback
  StationAq.cs           AirGradient/IQAir/WAQI loaders (section currently hidden)
  SunCalc.cs             C# port of SunCalc's sun/moon math (MIT, V. Agafonkin)
  Charts.cs, Scales.cs, Fmt.cs, Icons.cs, Js.cs, Dom.cs   Support code
  wwwroot/
    index.html           The original page markup, unchanged
    styles.css           The original stylesheet, unchanged
    app-interop.js       Presentation glue: the ECharts bridge and DOM helpers
api/                     Node serverless shims (see note below)
```

### Why Blazor WebAssembly

Vercel has no .NET runtime for serverless functions, so a server-side ASP.NET
app cannot run there. Blazor WebAssembly compiles the C# to WASM and publishes
to plain static files, which Vercel serves like any other static site — the
.NET code executes in the visitor's browser.

### Why some JavaScript remains

- **`api/*.js`** — thin serverless shims that must run server-side: the MSRO
  tide gauge sends no CORS headers and its API key must stay off the client,
  and the AirGradient/WAQI/IQAir routes guard API keys too. They would be C#
  as well if Vercel had a .NET function runtime.
- **`wwwroot/app-interop.js`** — ECharts is a JavaScript library; its option
  callbacks (tooltip formatters etc.) must be JS functions. C# builds every
  chart option as data and marks callbacks with `"@fn:name"` tokens that the
  interop layer swaps for functions from a small registry. The daily-briefing
  hero drawings live here as well; every number they show is computed in C#.

SunCalc is no longer loaded from a CDN — the sun/moon astronomy the dashboard
uses is ported to C# in `SunCalc.cs`.

## Data sources

| Section | Data | Path |
|---|---|---|
| Weather / Air Quality / Marine | Open-Meteo (CORS-open) | fetched directly from WASM |
| Tides | MSRO GeoHub tide gauge | `/api/tides` (needs `TIDE_API_KEY`) |
| Coral Bleaching Watch | NOAA CRW gauge image + link (the text feed is CORS-blocked in-browser) | direct `<img>` / attempted fetch with fallback |
| Sun & moon | Computed locally | `SunCalc.cs` |
| Station AQ (currently hidden) | AirGradient / WAQI / IQAir | `/api/aqcurrent`, `/api/aghistory`, `/api/iqair` |

## Local development

Requires the .NET 8 SDK and Node (for the API shims).

```sh
dotnet publish src/HmlEnvDb/HmlEnvDb.csproj -c Release -o out
node devserver.js
# open http://localhost:4173
```

`devserver.js` serves the published output and routes `/api/<name>` to
`api/<name>.js` with Vercel's handler contract, so the tide route works
locally too.

## Deploy to Vercel

`vercel.json` installs the .NET SDK during the build (`dotnet-install.sh`),
publishes the WASM app, and serves `out/wwwroot`. The `api/` functions deploy
as normal Node serverless functions alongside it.

```sh
npx vercel --prod
```

Optional environment variables (all degrade gracefully when unset):
`TIDE_API_KEY` (MSRO tides), `AQICN_TOKEN`, `AIRGRADIENT_TOKEN`, `IQAIR_KEY`
and a linked KV store for the station-AQ history sync. When re-enabling the
hidden station-AQ section with a KV store, also restore the daily sync cron
in `vercel.json`:

```json
"crons": [{ "path": "/api/agsync", "schedule": "0 6 * * *" }]
```

## Known limitations

Carried over from the original: the NOAA coral time series is CORS-blocked in
the browser (the section shows NOAA's live gauge image and links to the
official page instead), hourly-model tide extrema can miss tides where the
model resolution is coarse, and the 5×5 heatmap grid stays disabled pending
better spatial resolution.
