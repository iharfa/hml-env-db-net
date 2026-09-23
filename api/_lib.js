// Shared helpers for the AQICN serverless functions.
// No npm dependencies — uses Node's built-in global fetch (Node 18+ on Vercel).
// Files prefixed with "_" are not exposed as routes.

// Fetch the current AQICN (WAQI) feed for the configured location/station.
// Token is read from the AQICN_TOKEN env var — never hard-coded in the repo.
async function fetchAqicnFeed(queryOverride) {
  const token = process.env.AQICN_TOKEN;
  if (!token) throw new Error('AQICN_TOKEN env var is not set');
  // e.g. "geo:4.2105;73.5446" (nearest station) or "@A503515" (specific station id).
  // Default: @A503515 = Siththimaavaa Hingun (Galolhu, Malé) — the closest
  // reporting station to Hulhumalé. It isn't in WAQI's geo/nearest index, so it
  // must be pinned by id. Override with the AQICN_QUERY env var if needed.
  const query = queryOverride || process.env.AQICN_QUERY || '@A503515';
  const url = `https://api.waqi.info/feed/${query}/?token=${encodeURIComponent(token)}`;
  const r = await fetch(url);
  const j = await r.json();
  if (!j || j.status !== 'ok') {
    throw new Error(`AQICN error: ${(j && (j.data || j.status)) || r.status}`);
  }
  return j.data;
}

// Minimal Upstash/Vercel KV REST client. Sends a single Redis command as a JSON
// array and returns its result. Requires KV_REST_API_URL + KV_REST_API_TOKEN,
// which Vercel injects automatically when a KV store is linked to the project.
async function kv(command) {
  const url = process.env.KV_REST_API_URL;
  const token = process.env.KV_REST_API_TOKEN;
  if (!url || !token) throw new Error('KV is not configured (no KV_REST_API_URL/TOKEN)');
  const r = await fetch(url, {
    method: 'POST',
    headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
    body: JSON.stringify(command),
  });
  if (!r.ok) throw new Error(`KV request failed: ${r.status}`);
  const j = await r.json();
  return j.result;
}

// ── AirGradient helpers ──
const AG_LOCATION_DEFAULT = '76611'; // Siththimaavaa Hingun, Galolhu, Malé (moenv.mv)

// Current measures from AirGradient's OPEN (no-token) world endpoint.
async function fetchAirgradientCurrentOpen() {
  const loc = process.env.AIRGRADIENT_LOCATION || AG_LOCATION_DEFAULT;
  const url = `https://api.airgradient.com/public/api/v1/world/locations/${loc}/measures/current`;
  const r = await fetch(url);
  if (!r.ok) throw new Error(`AirGradient ${r.status}`);
  return r.json();
}

// US EPA PM2.5 → AQI (legacy 24h breakpoints, matches WAQI's US AQI).
function pm25ToAqi(c) {
  if (c == null || isNaN(c)) return null;
  c = Math.round(c * 10) / 10;
  const bp = [
    [0.0, 12.0, 0, 50], [12.1, 35.4, 51, 100], [35.5, 55.4, 101, 150],
    [55.5, 150.4, 151, 200], [150.5, 250.4, 201, 300],
    [250.5, 350.4, 301, 400], [350.5, 500.4, 401, 500],
  ];
  for (const [cl, ch, al, ah] of bp) {
    if (c <= ch) { const x = Math.max(c, cl); return Math.round((ah - al) / (ch - cl) * (x - cl) + al); }
  }
  return 500;
}

function aqiCategoryName(v) {
  if (v == null) return null;
  if (v <= 50) return 'Good';
  if (v <= 100) return 'Moderate';
  if (v <= 150) return 'Unhealthy (SG)';
  if (v <= 200) return 'Unhealthy';
  if (v <= 300) return 'Very Unhealthy';
  return 'Hazardous';
}

const round1 = x => Math.round(x * 10) / 10;

// Fetch AirGradient "past measures" for the last `days` and aggregate to one
// record per calendar day: {date, n, pm25Avg/Min/Max, aqiAvg, aqiMax}.
async function airgradientPastDaily(days) {
  const token = process.env.AIRGRADIENT_TOKEN;
  if (!token) throw new Error('AIRGRADIENT_TOKEN env var is not set');
  const loc = process.env.AIRGRADIENT_LOCATION || AG_LOCATION_DEFAULT;
  const to = new Date();
  const from = new Date(to.getTime() - days * 86400000);
  const url = `https://api.airgradient.com/public/api/v1/locations/${loc}/measures/past`
    + `?token=${encodeURIComponent(token)}&from=${from.toISOString()}&to=${to.toISOString()}`;
  const r = await fetch(url);
  if (!r.ok) throw new Error(`AirGradient ${r.status}`);
  const rows = await r.json();
  const list = Array.isArray(rows) ? rows : (rows && Array.isArray(rows.measures) ? rows.measures : []);

  const byDay = {};
  for (const row of list) {
    const ts = row.timestamp || row.date;
    const pm = (typeof row.pm02 === 'number') ? row.pm02
      : (typeof row.pm25 === 'number') ? row.pm25
      : (typeof row.pm02Corrected === 'number') ? row.pm02Corrected : null;
    if (!ts || pm == null || isNaN(pm)) continue;
    (byDay[String(ts).slice(0, 10)] || (byDay[String(ts).slice(0, 10)] = [])).push(pm);
  }
  return Object.keys(byDay).sort().map(date => {
    const v = byDay[date];
    const avg = v.reduce((a, b) => a + b, 0) / v.length;
    return {
      date, n: v.length,
      pm25Avg: round1(avg), pm25Min: round1(Math.min(...v)), pm25Max: round1(Math.max(...v)),
      aqiAvg: pm25ToAqi(avg), aqiMax: pm25ToAqi(Math.max(...v)),
    };
  });
}

// Build the period-overview summary from an array of daily records.
function summarizeDaily(daily) {
  const withData = daily.filter(d => d && d.aqiAvg != null);
  if (!withData.length) return null;
  let n = 0, sum = 0, min = Infinity, max = -Infinity;
  const categories = {};
  for (const d of withData) {
    n += d.n || 0;
    sum += d.pm25Avg * (d.n || 1);
    min = Math.min(min, d.pm25Min);
    max = Math.max(max, d.pm25Max);
    const c = aqiCategoryName(d.aqiAvg);
    if (c) categories[c] = (categories[c] || 0) + 1;
  }
  const totalW = withData.reduce((a, d) => a + (d.n || 1), 0);
  const pm25Avg = round1(sum / totalW);
  return {
    from: withData[0].date, to: withData[withData.length - 1].date,
    daysWithData: withData.length, samples: n,
    pm25Avg, pm25Min: round1(min), pm25Max: round1(max),
    aqiAvg: pm25ToAqi(pm25Avg), aqiMax: pm25ToAqi(max),
    categories,
  };
}

// ── Public historical feed (no login, no token) ──
// The aqicn.org station page (https://aqicn.org/station/maldives-galolhu-
// siththimaavaa-hingun/) renders its "Historic" section from this public SSE
// endpoint — no account or token required, just the WAQI-internal airnet id
// for this station (503515, distinct from the AirGradient locationId 76611).
// It streams a rolling ~2-week window of fine-resolution PM2.5 readings.
const AIRNET_ID = '503515';

// Read the SSE response body for up to `timeoutMs`, then stop (the stream
// otherwise stays open indefinitely for live push updates).
async function readSseFor(url, timeoutMs) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  let text = '';
  try {
    const r = await fetch(url, { signal: controller.signal, headers: { Accept: 'text/event-stream' } });
    const reader = r.body.getReader();
    const decoder = new TextDecoder();
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      text += decoder.decode(value, { stream: true });
    }
  } catch { /* expected: aborted once the time budget is used up */ }
  finally { clearTimeout(timer); }
  return text;
}

// Parse the SSE "data: [startTs, val, durationSec, val, durationSec, ...]"
// arrays into one aggregate per calendar day (UTC).
function parseAirnetHistoricSse(sseText) {
  const arrays = [];
  for (const line of sseText.split('\n')) {
    if (line.startsWith('data: [')) {
      try { arrays.push(JSON.parse(line.slice(6))); } catch { /* skip malformed line */ }
    }
  }
  const byDay = {};
  for (const a of arrays) {
    let t = a[0] * 1000;
    for (let i = 1; i + 1 < a.length; i += 2) {
      const v = a[i], durMs = a[i + 1] * 1000;
      if (typeof v === 'number') {
        const day = new Date(t).toISOString().slice(0, 10);
        (byDay[day] || (byDay[day] = [])).push(v);
      }
      t += durMs;
    }
  }
  return Object.keys(byDay).sort().map(date => {
    const v = byDay[date];
    const avg = v.reduce((a, b) => a + b, 0) / v.length;
    return {
      date, n: v.length,
      pm25Avg: round1(avg), pm25Min: round1(Math.min(...v)), pm25Max: round1(Math.max(...v)),
      aqiAvg: pm25ToAqi(avg), aqiMax: pm25ToAqi(Math.max(...v)),
    };
  });
}

// Fetch + parse the public historic feed in one call.
async function fetchAirnetHistoricDaily(timeoutMs = 9000) {
  const sse = await readSseFor(`https://airnet.waqi.info/airnet/sse/historic/${AIRNET_ID}`, timeoutMs);
  return parseAirnetHistoricSse(sse);
}

module.exports = {
  fetchAqicnFeed, kv, fetchAirgradientCurrentOpen,
  airgradientPastDaily, summarizeDaily, pm25ToAqi, aqiCategoryName, round1,
  fetchAirnetHistoricDaily,
};
