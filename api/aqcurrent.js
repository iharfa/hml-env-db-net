// GET /api/aqcurrent — current station reading from AirGradient's OPEN API
// (no token). Computes US AQI from PM2.5 (pm02).
const { fetchAirgradientCurrentOpen, pm25ToAqi, aqiCategoryName } = require('./_lib');

module.exports = async (req, res) => {
  try {
    const d = await fetchAirgradientCurrentOpen();
    const pm25 = (typeof d.pm02 === 'number') ? d.pm02 : null;
    const aqi = pm25 != null ? pm25ToAqi(pm25) : null;
    res.setHeader('Cache-Control', 's-maxage=300, stale-while-revalidate=600');
    res.status(200).json({
      configured: true,
      source: 'airgradient-open',
      station: d.locationName || d.publicLocationName || null,
      offline: !!d.offline,
      aqi,
      category: aqi != null ? aqiCategoryName(aqi) : null,
      pm25, pm10: (typeof d.pm10 === 'number') ? d.pm10 : null,
      pm01: (typeof d.pm01 === 'number') ? d.pm01 : null,
      co2: (typeof d.rco2 === 'number') ? d.rco2 : null,
      temp: (typeof d.atmp === 'number') ? d.atmp : null,
      humidity: (typeof d.rhum === 'number') ? d.rhum : null,
      tvoc: (typeof d.tvoc === 'number') ? d.tvoc : null,
      timestamp: d.timestamp || null,
      url: 'https://aqicn.org/station/maldives-galolhu-siththimaavaa-hingun/',
    });
  } catch (e) {
    res.status(502).json({ configured: false, error: String((e && e.message) || e) });
  }
};
