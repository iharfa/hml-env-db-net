// POST/GET /api/agsync — pull the station's public historic feed (no login, no
// token — see fetchAirnetHistoricDaily in _lib.js) and store daily aggregates
// in Vercel KV. Each run captures a ~2-week rolling window at fine resolution,
// so running this on a schedule both refreshes recent days and extends the
// archive backward in time as the window slides forward.
// Auth: Vercel cron sends "Authorization: Bearer <CRON_SECRET>"; manual calls
// pass ?secret=<CRON_SECRET>.
const { kv, fetchAirnetHistoricDaily } = require('./_lib');

const KEY = 'ag:daily';

module.exports = async (req, res) => {
  const secret = process.env.CRON_SECRET;
  if (secret) {
    const auth = (req.headers.authorization || '').replace(/^Bearer\s+/i, '');
    const provided = auth || (req.query && req.query.secret) || '';
    if (provided !== secret) { res.status(401).json({ error: 'unauthorized' }); return; }
  }

  try {
    const daily = await fetchAirnetHistoricDaily();
    if (!daily.length) { res.status(200).json({ ok: true, stored: 0, note: 'no data parsed from the historic feed' }); return; }

    const args = ['HSET', KEY];
    for (const d of daily) args.push(d.date, JSON.stringify(d));
    await kv(args);

    res.status(200).json({ ok: true, stored: daily.length, from: daily[0].date, to: daily[daily.length - 1].date });
  } catch (e) {
    res.status(502).json({ error: String((e && e.message) || e) });
  }
};
