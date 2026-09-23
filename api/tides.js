// GET /api/tides — real tide-gauge data for Hulhumalé from the MSRO GeoHub
// tide API (https://geohub.msro.mv/tides). The key lives in the TIDE_API_KEY
// env var (set it in Vercel → Project → Settings → Environment Variables),
// never in the repo. Runs server-side so the key isn't exposed to the browser.
const TIDE_ENDPOINT = 'https://dlcgwsrtoihwascuencq.supabase.co/functions/v1/tide-api';

module.exports = async (req, res) => {
  const key = process.env.TIDE_API_KEY;
  if (!key) { res.status(200).json({ configured: false, reason: 'TIDE_API_KEY not set' }); return; }

  const island = process.env.TIDE_ISLAND || '.Hulhumaale';
  const pad = n => String(n).padStart(2, '0');
  // The API's dates and timestamps are all Malé local (UTC+5), so derive "today"
  // in MVT rather than in the server's UTC clock — otherwise between 19:00 and
  // 24:00 UTC we'd ask for the wrong day.
  const mvtDateStr = d => `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`;
  const mvtNow = new Date(Date.now() + 5 * 3600000);
  const startDate = mvtDateStr(mvtNow);
  const endDate = mvtDateStr(new Date(mvtNow.getTime() + 2 * 86400000));

  const url = `${TIDE_ENDPOINT}?island=${encodeURIComponent(island)}`
    + `&start_date=${startDate}&end_date=${endDate}&format=json`;

  try {
    const r = await fetch(url, { headers: { 'x-api-key': key } });
    const j = await r.json();
    if (!r.ok || !Array.isArray(j.data)) {
      res.status(502).json({ configured: true, error: (j && (j.error || j.message)) || `HTTP ${r.status}` });
      return;
    }
    // time_utc_plus5 digits are already Malé local time, but the API serialises
    // them with a "Z" (e.g. "2026-07-28T13:08:00.000Z" is 13:08 MVT, verified
    // against the high/low tides shown on geohub.msro.mv/tides). Left as-is, the
    // Z would make new Date() read them as UTC and shift every tide +5 hours.
    // Truncating to "YYYY-MM-DDTHH:MM" gives the same offset-free local format
    // the app already uses for Open-Meteo data.
    const toLocalWallClock = ts => String(ts).slice(0, 16);
    res.setHeader('Cache-Control', 's-maxage=300, stale-while-revalidate=600');
    res.status(200).json({
      configured: true,
      source: 'msro',
      island: j.island || island,
      time: j.data.map(d => toLocalWallClock(d.time_utc_plus5)),
      sea_level_height_msl: j.data.map(d => d.tide_m),
      url: 'https://geohub.msro.mv/tides',
    });
  } catch (e) {
    res.status(502).json({ configured: true, error: String((e && e.message) || e) });
  }
};
