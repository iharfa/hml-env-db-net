using System.Text.Json.Nodes;
using Microsoft.JSInterop;

namespace HmlEnvDb;

// Data store (populated after fetch), shared across sections.
public static class Data
{
    public static JsonNode? Weather, AirQuality, Marine;
    public static TideSeries? Tides;
    public static CoralResult? Coral;
}

// Main load & refresh orchestration — the port of app.js's loadAll() and its
// init block. Event callbacks arrive from the interop layer via [JSInvokable].
public static class Dashboard
{
    private static HttpClient _http = default!;
    private static bool _loading;

    public static void Init(IJSRuntime js, HttpClient http)
    {
        _http = http;
        Dom.Init(js);
        Fetch.Init(http);
    }

    public static async Task BootAsync()
    {
        await Dom.WireEvents();
        await LoadAllAsync();

        // Auto-refresh every 30 minutes.
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
        while (await timer.WaitForNextTickAsync())
            await LoadAllAsync();
    }

    [JSInvokable("LoadAll")]
    public static Task LoadAllJs() => LoadAllAsync();

    public static async Task LoadAllAsync()
    {
        if (_loading) return;          // ignore overlapping refreshes
        _loading = true;
        await Dom.SetLoading(true);

        // Coral (NOAA) is CORS-blocked in-browser and slow (~4s to fail), so it
        // runs OFF the critical path and fills in when it settles.
        _ = LoadCoralAsync();

        try
        {
            var weatherTask = Fetch.Weather();
            var aqTask = Fetch.AirQuality();
            var marineTask = Fetch.Marine();
            var tidesTask = Fetch.TidesGauge();

            Data.Weather = await Safe(weatherTask, "Weather");
            Data.AirQuality = await Safe(aqTask, "AQ");
            Data.Marine = await Safe(marineTask, "Marine");
            Data.Tides = await Safe(tidesTask, "Tides");

            await Briefing.UpdateBriefing(Data.Weather, Data.AirQuality, Data.Marine);
            await Sections.UpdateAirQuality(Data.AirQuality);
            await Sections.UpdateWeather(Data.Weather);
            await Sections.UpdateMarine(Data.Marine);
            await CoralSection.UpdateCoral(Data.Coral);   // fallback / last-known until the background fetch resolves
            await Sections.UpdateStatusTable(Data.Weather, Data.AirQuality, Data.Marine, Data.Coral);

            if (Data.Weather is not null) await Sections.RenderWeatherCharts(Data.Weather);
            if (Data.Marine is not null) await Sections.RenderMarineCharts(Data.Marine);

            await UpdateTimestamp();

            // Station air quality — skipped while the #station-aq block is hidden
            // (awaiting an IQAir API key), matching the original behaviour.
            if (!await Dom.IsHidden("station-aq"))
            {
                await Task.WhenAll(StationAq.LoadAqicn(), StationAq.LoadIqair(), StationAq.LoadAgHistory());
                await Dom.ReconcileStationSection();
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Dashboard load error: {e}");
        }
        finally
        {
            _loading = false;
            await Dom.SetLoading(false);
        }
    }

    private static async Task<T?> Safe<T>(Task<T?> task, string label) where T : class
    {
        try { return await task; }
        catch (Exception e) { Console.WriteLine($"{label} fetch failed: {e.Message}"); return null; }
    }

    private static async Task LoadCoralAsync()
    {
        try
        {
            var coral = await Fetch.CoralWatch();
            Data.Coral = coral;
            await CoralSection.UpdateCoral(coral);
            await Sections.UpdateStatusTable(Data.Weather, Data.AirQuality, Data.Marine, coral);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Coral fetch failed: {e.Message}");
            await CoralSection.UpdateCoral(null);
        }
    }

    private static async Task UpdateTimestamp()
    {
        var now = Fmt.NowMaldives();
        await Dom.SetText("last-updated", Fmt.FormatTime(now) + " MVT");
        await Dom.SetAttr("last-updated", "datetime", DateTime.UtcNow.ToString("o"));
    }

    // ── Callbacks from the interop layer ──

    [JSInvokable("AgRange")]
    public static async Task AgRange(int days)
    {
        StationAq.Range = days;
        await StationAq.LoadAgHistory();
    }

    [JSInvokable("AgCsv")]
    public static Task AgCsv() => StationAq.ExportCsv();
}
