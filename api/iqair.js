// GET /api/iqair — current air quality for Malé from IQAir's AirVisual API.
// The API key lives in the IQAIR_KEY env var (set it in Vercel → Project →
// Settings → Environment Variables), never in the repo. Runs server-side so the
// key isn't exposed and the browser CORS restriction on IQAir doesn't apply.
//
// Free "Community" key: https://www.iqair.com/dashboard/api

// IQAir "mainus" pollutant codes → friendly names.
const POLLUTANT = { p2: 'PM2.5', p1: 'PM10', o3: 'Ozone', n2: 'NO₂', s2: 'SO₂', co: 'CO' };

module.exports = async (req, res) => {
  const key = process.env.IQAIR_KEY;
  if (!key) { res.status(200).json({ configured: false, reason: 'IQAIR_KEY not set' }); return; }

  const city    = process.env.IQAIR_CITY    || 'Male';
  const state   = process.env.IQAIR_STATE   || 'Kaafu Atoll';
  const country = process.env.IQAIR_COUNTRY || 'Maldives';

  const url = 'https://api.airvisual.com/v2/city'
    + `?city=${encodeURIComponent(city)}`
    + `&state=${encodeURIComponent(state)}`
    + `&country=${encodeURIComponent(country)}`
    + `&key=${encodeURIComponent(key)}`;

  try {
    const r = await fetch(url);
    const j = await r.json();
    if (!j || j.status !== 'success' || !j.data?.current) {
      res.status(502).json({ configured: true, error: (j && (j.data?.message || j.status)) || `HTTP ${r.status}` });
      return;
    }
    const cur = j.data.current;
    const p = cur.pollution || {};
    const w = cur.weather || {};
    res.setHeader('Cache-Control', 's-maxage=600, stale-while-revalidate=1200');
    res.status(200).json({
      configured: true,
      source: 'iqair',
      city: j.data.city || city,
      aqi: (typeof p.aqius === 'number') ? p.aqius : null,   // US AQI
      mainPollutant: POLLUTANT[p.mainus] || p.mainus || null,
      temp: (typeof w.tp === 'number') ? w.tp : null,        // °C
      humidity: (typeof w.hu === 'number') ? w.hu : null,    // %
      wind: (typeof w.ws === 'number') ? Math.round(w.ws * 3.6) : null, // m/s → km/h
      pressure: (typeof w.pr === 'number') ? w.pr : null,    // hPa
      timestamp: p.ts || null,
      url: 'https://www.iqair.com/air-quality/maldives/kaafu-atoll/male',
    });
  } catch (e) {
    res.status(502).json({ configured: true, error: String((e && e.message) || e) });
  }
};
