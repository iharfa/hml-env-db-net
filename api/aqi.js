// GET /api/aqi — proxy the current AQICN reading (keeps the token server-side).
const { fetchAqicnFeed } = require('./_lib');

module.exports = async (req, res) => {
  try {
    const d = await fetchAqicnFeed();
    // Cache at the edge for 10 min; serve stale while revalidating.
    res.setHeader('Cache-Control', 's-maxage=600, stale-while-revalidate=1200');
    res.status(200).json({
      aqi: d.aqi,
      dominentpol: d.dominentpol || null,
      station: (d.city && d.city.name) || null,
      url: (d.city && d.city.url) || null,
      geo: (d.city && d.city.geo) || null,
      time: d.time || null,
      iaqi: d.iaqi || {},
      forecast: d.forecast || null,
      attributions: d.attributions || [],
    });
  } catch (e) {
    res.status(502).json({ error: String((e && e.message) || e) });
  }
};
