using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace HmlEnvDb;

public class TideSeries
{
    public List<string> Time = new();
    public List<double?> Heights = new();
    public bool IsReal;                  // true = MSRO gauge via /api/tides
}

// All the live data sources. Open-Meteo sends CORS headers and is fetched
// straight from the browser; the MSRO tide gauge does not (and guards its API
// key), so it goes through the /api/tides Node shim.
public static class Fetch
{
    public const string TZ = "Indian%2FMaldives";

    private static HttpClient _http = default!;
    public static void Init(HttpClient http) => _http = http;

    private static HttpRequestMessage NoStore(string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.SetBrowserRequestCache(BrowserRequestCache.NoStore);
        return req;
    }

    private static async Task<JsonNode?> GetJson(string url, int timeoutSec = 60)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
        using var res = await _http.SendAsync(NoStore(url), cts.Token);
        res.EnsureSuccessStatusCode();
        return JsonNode.Parse(await res.Content.ReadAsStringAsync(cts.Token));
    }

    public static Task<JsonNode?> Weather() => GetJson(
        "https://api.open-meteo.com/v1/forecast?latitude=4.2105&longitude=73.5446" +
        "&current=temperature_2m,windspeed_10m,windgusts_10m,winddirection_10m,precipitation,weathercode,relativehumidity_2m,surface_pressure,uv_index" +
        "&hourly=temperature_2m,precipitation_probability,precipitation,windspeed_10m,windgusts_10m,winddirection_10m,relativehumidity_2m,surface_pressure,uv_index,weathercode" +
        "&daily=temperature_2m_max,temperature_2m_min,precipitation_probability_max,precipitation_sum,windspeed_10m_max,weathercode,sunrise,sunset" +
        $"&timezone={TZ}&forecast_days=7");

    public static Task<JsonNode?> AirQuality() => GetJson(
        "https://air-quality-api.open-meteo.com/v1/air-quality?latitude=4.2105&longitude=73.5446" +
        "&hourly=us_aqi,us_aqi_pm2_5,us_aqi_pm10,us_aqi_nitrogen_dioxide,us_aqi_ozone,us_aqi_sulphur_dioxide,us_aqi_carbon_monoxide,pm2_5,pm10,dust,uv_index" +
        $"&timezone={TZ}&forecast_days=3");

    public static Task<JsonNode?> Marine() => GetJson(
        "https://marine-api.open-meteo.com/v1/marine?latitude=4.2105&longitude=73.5446" +
        "&hourly=sea_level_height_msl,wave_height,wave_period,wind_wave_height,swell_wave_height,sea_surface_temperature,ocean_current_velocity,ocean_current_direction" +
        $"&timezone={TZ}&forecast_days=3");

    public static async Task<TideSeries?> TidesGauge()
    {
        var j = await FetchJsonApi("/api/tides");
        if (j is null || !Js.Bool(j["configured"]) || Js.Count(j["time"]) == 0) return null;
        return new TideSeries
        {
            Time = Js.Strs(j["time"]),
            Heights = Js.Nums(j["sea_level_height_msl"]),
            IsReal = true,
        };
    }

    // Try the NOAA CRW Maldives time-series text files concurrently, with a
    // timeout so a slow/hung request fails fast. NOAA is CORS-blocked
    // in-browser, so this normally fails quickly and the UI falls back to a
    // link (see CoralSection.UpdateCoral).
    public static async Task<CoralResult> CoralWatch()
    {
        var urls = new[]
        {
            "https://coralreefwatch.noaa.gov/product/vs/data/maldives.txt",
            "https://coralreefwatch.noaa.gov/product/vs/data/maldivesw.txt",
        };
        var attempts = urls.Select(async u =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            using var res = await _http.SendAsync(NoStore(u), cts.Token);
            if (!res.IsSuccessStatusCode) throw new Exception($"Coral {(int)res.StatusCode}");
            return await res.Content.ReadAsStringAsync(cts.Token);
        }).ToList();

        while (attempts.Count > 0)
        {
            var done = await Task.WhenAny(attempts);
            attempts.Remove(done);
            try { return new CoralResult { Status = "ok", Raw = await done }; }
            catch { /* try the next URL */ }
        }
        return new CoralResult { Status = "cors-blocked" };
    }

    // Fetch a JSON API route. Returns null (not an error) when the serverless
    // function isn't deployed / configured, so a static deploy degrades gracefully.
    public static async Task<JsonNode?> FetchJsonApi(string path)
    {
        try
        {
            using var res = await _http.SendAsync(NoStore(path));
            var ct = res.Content.Headers.ContentType?.MediaType ?? "";
            if (!ct.Contains("application/json")) return null;
            return JsonNode.Parse(await res.Content.ReadAsStringAsync());
        }
        catch { return null; }
    }
}
