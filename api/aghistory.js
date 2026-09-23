// GET /api/aghistory?days=N — daily PM2.5 + US AQI history from OUR stored copy
// in Vercel KV (accumulated by /api/agsync from the open AirGradient API).
// Token-free. The dashboard merges this with the committed seed for backfill.
const { kv, pm25ToAqi, round1 } = require('./_lib');

const KEY = 'ag:daily';

module.exports = async (req, res) => {
  let days = parseInt((req.query && req.query.days) || '30', 10);
  if (isNaN(days)) days = 30;
  days = Math.max(1, Math.min(365, days));

  try {
    let raw;
    try { raw = await kv(['HGETALL', KEY]); }
    catch (e) { res.status(200).json({ configured: false, error: 'KV not configured', daily: [], summary: null }); return; }

    // HGETALL → [field, val, field, val, ...] or object
    const recs = [];
    if (Array.isArray(raw)) { for (let i = 1; i < raw.length; i += 2) recs.push(raw[i]); }
    else if (raw && typeof raw === 'object') { recs.push(...Object.values(raw)); }

    const daily = recs
      .map(v => { try { return typeof v === 'string' ? JSON.parse(v) : v; } catch { return null; } })
      .filter(r => r && r.date && r.n > 0)
      .map(r => {
        // Current shape (from fetchAirnetHistoricDaily): already a full aggregate.
        if (typeof r.pm25Avg === 'number') return r;
        // Legacy shape (from the old current-ping accumulator): {date,n,sum,min,max}.
        const avg = r.sum / r.n;
        return {
          date: r.date, n: r.n,
          pm25Avg: round1(avg), pm25Min: round1(r.min), pm25Max: round1(r.max),
          aqiAvg: pm25ToAqi(avg), aqiMax: pm25ToAqi(r.max),
        };
      })
      .sort((a, b) => (a.date < b.date ? -1 : 1));

    const cutoff = new Date(Date.now() - days * 86400000).toISOString().slice(0, 10);
    const windowed = daily.filter(d => d.date >= cutoff);

    res.setHeader('Cache-Control', 's-maxage=900, stale-while-revalidate=1800');
    res.status(200).json({ configured: true, source: 'stored', stored: daily.length, days, daily: windowed });
  } catch (e) {
    res.status(200).json({ configured: false, error: String((e && e.message) || e), daily: [], summary: null });
  }
};
