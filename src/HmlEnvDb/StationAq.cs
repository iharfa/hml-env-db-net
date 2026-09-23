using System.Text;
using System.Text.Json.Nodes;

namespace HmlEnvDb;

// Station Air Quality (AirGradient / IQAir / WAQI history). The whole block is
// currently `hidden` in index.html pending an IQAir API key — the loaders run
// only when it is re-enabled, exactly as before.
public static class StationAq
{
    public static int Range = 30;   // days of history shown

    private static readonly Dictionary<string, string> CatColors = new()
    {
        ["Good"] = "#16a34a", ["Moderate"] = "#ca8a04", ["Unhealthy (SG)"] = "#ea580c",
        ["Unhealthy"] = "#dc2626", ["Very Unhealthy"] = "#7c3aed", ["Hazardous"] = "#7e0023",
    };

    public class DailyRec
    {
        public string Date = "";
        public int N;
        public double Pm25Avg, Pm25Min, Pm25Max;
        public int? AqiAvg, AqiMax;
    }

    private static List<DailyRec> AgDaily = new();   // displayed records (for CSV export)

    private static string F(double? v, string unit = "") => v is null ? "—" : $"{Fmt.FormatNumber(v)}{unit}";

    // Current station reading from AirGradient's open API (no token).
    public static async Task LoadAqicn()
    {
        var data = await Fetch.FetchJsonApi("/api/aqcurrent");
        var aqi = Js.Num(data?["aqi"]);
        if (data is null || (data["configured"] is not null && !Js.Bool(data["configured"])) || aqi is null)
        {
            await Dom.SetDisplay("aqicn-current", false);
            await Dom.SetDisplay("aqicn-pill", false);
            return;
        }
        await Dom.SetDisplay("aqicn-current", true);
        await Dom.SetDisplay("aqicn-pill", true);
        var offline = Js.Bool(data["offline"]);
        await Dom.SetText("aqicn-pill", offline ? "Stale" : "Live");
        await Dom.SetClass("aqicn-pill", offline ? "pill pill-not-live" : "pill pill-live");

        var cat = Aqi.Category(aqi);
        var when = Js.Str(data["timestamp"]) is string ts ? Fmt.FormatDateTimeLabel(ts.Replace(" ", "T")) : "—";
        var url = Js.Str(data["url"]);

        await Dom.SetHtml("aqicn-current-body", $"""
            <div class="aqicn-value" style="color:{cat.Color}">{aqi:0}<span class="aqicn-unit">US AQI</span></div>
            <div class="aqicn-cat" style="color:{cat.Color}">{cat.Label}</div>
            <div class="aqicn-station">{(Js.Str(data["station"]) is string st ? Fmt.EscapeHtml(st) : "Malé station")}</div>
            <div class="aqicn-pollutants">
              <span>PM2.5 <b>{F(Js.Num(data["pm25"]))}</b></span><span>PM10 <b>{F(Js.Num(data["pm10"]))}</b></span><span>CO₂ <b>{F(Js.Num(data["co2"]))}</b></span>
              <span>Temp <b>{F(Js.Num(data["temp"]), "°C")}</b></span><span>Humidity <b>{F(Js.Num(data["humidity"]), "%")}</b></span><span>TVOC <b>{F(Js.Num(data["tvoc"]))}</b></span>
            </div>
            <div class="aqicn-meta">PM2.5 {F(Js.Num(data["pm25"]))} µg/m³ · Updated {when} · Source: AirGradient (open API)</div>
            {(url is not null ? $"""<a class="btn-external" href="{Fmt.EscapeHtml(url)}" target="_blank" rel="noopener noreferrer">View station ↗</a>""" : "")}
            """);
    }

    // Current Malé reading from IQAir (via /api/iqair serverless proxy).
    public static async Task LoadIqair()
    {
        var data = await Fetch.FetchJsonApi("/api/iqair");
        var aqi = Js.Num(data?["aqi"]);
        if (data is null || (data["configured"] is not null && !Js.Bool(data["configured"])) || aqi is null)
        {
            await Dom.SetDisplay("iqair-current", false);
            return;
        }
        await Dom.SetDisplay("iqair-current", true);
        await Dom.SetDisplay("iqair-pill", true);
        await Dom.SetText("iqair-pill", "Live");
        await Dom.SetClass("iqair-pill", "pill pill-live");

        var cat = Aqi.Category(aqi);
        var when = Js.Str(data["timestamp"]) is string ts ? Fmt.FormatDateTimeLabel(ts.Replace(" ", "T")) : "—";
        var url = Js.Str(data["url"]);
        var main = Js.Str(data["mainPollutant"]);

        await Dom.SetHtml("iqair-current-body", $"""
            <div class="aqicn-value" style="color:{cat.Color}">{aqi:0}<span class="aqicn-unit">US AQI</span></div>
            <div class="aqicn-cat" style="color:{cat.Color}">{cat.Label}</div>
            <div class="aqicn-station">{(Js.Str(data["city"]) is string c ? Fmt.EscapeHtml(c) : "Malé")}{(main is not null ? $" · main pollutant {Fmt.EscapeHtml(main)}" : "")}</div>
            <div class="aqicn-pollutants">
              <span>Temp <b>{F(Js.Num(data["temp"]), "°C")}</b></span><span>Humidity <b>{F(Js.Num(data["humidity"]), "%")}</b></span>
              <span>Wind <b>{F(Js.Num(data["wind"]), " km/h")}</b></span><span>Pressure <b>{F(Js.Num(data["pressure"]), " hPa")}</b></span>
            </div>
            <div class="aqicn-meta">Updated {when} · Source: IQAir AirVisual API</div>
            {(url is not null ? $"""<a class="btn-external" href="{Fmt.EscapeHtml(url)}" target="_blank" rel="noopener noreferrer">View on IQAir ↗</a>""" : "")}
            """);
    }

    private static DailyRec? ParseDaily(JsonNode? n)
    {
        if (n is null) return null;
        var aqiAvg = Js.Int(n["aqiAvg"]);
        if (aqiAvg is null) return null;
        return new DailyRec
        {
            Date = Js.Str(n["date"]) ?? "",
            N = Js.Int(n["n"]) ?? 0,
            Pm25Avg = Js.Num(n["pm25Avg"]) ?? 0,
            Pm25Min = Js.Num(n["pm25Min"]) ?? 0,
            Pm25Max = Js.Num(n["pm25Max"]) ?? 0,
            AqiAvg = aqiAvg,
            AqiMax = Js.Int(n["aqiMax"]),
        };
    }

    // Historical air quality: committed backfill seed merged with the KV stored
    // copy that /api/agsync grows daily. KV wins for shared days.
    public static async Task LoadAgHistory()
    {
        var cutoff = DateTime.UtcNow.AddDays(-Range).ToString("yyyy-MM-dd");
        var resTask = Fetch.FetchJsonApi($"/api/aghistory?days={Range}");
        var seedTask = Fetch.FetchJsonApi("/data/ag-seed.json");
        var res = await resTask;
        var seed = await seedTask;

        var byDate = new Dictionary<string, DailyRec>();
        if (seed?["daily"] is JsonArray sd)
            foreach (var n in sd) if (ParseDaily(n) is { } r) byDate[r.Date] = r;
        var liveN = 0;
        if (res?["daily"] is JsonArray ld)
            foreach (var n in ld) if (ParseDaily(n) is { } r) { byDate[r.Date] = r; liveN++; }

        var daily = byDate.Values
            .Where(r => string.CompareOrdinal(r.Date, cutoff) >= 0)
            .OrderBy(r => r.Date, StringComparer.Ordinal)
            .ToList();
        var storedN = Js.Int(res?["stored"]) ?? liveN;
        var sourceLabel = storedN > 0 ? $"stored copy ({storedN}d) + backfill" : "recent sample";

        if (daily.Count == 0)
        {
            // No history — hide the whole history card rather than show an empty chart.
            await Dom.SetDisplaySel(".aqicn-history-card", false);
            AgDaily = new();
            return;
        }
        await Dom.SetDisplaySel(".aqicn-history-card", true);
        AgDaily = daily;

        await Sections.Chart("chart-aqicn-history", new Dictionary<string, object?>
        {
            ["tooltip"] = new { trigger = "axis", formatter = "@fn:agHistTooltip" },
            ["grid"] = new { left = 40, right = 16, top = 24, bottom = 46 },
            ["xAxis"] = new
            {
                type = "category",
                data = daily.Select(d => d.Date).ToList(),
                axisLine = new { lineStyle = new { color = "#cbd5e1" } },
                axisTick = new { show = false },
                axisLabel = new
                {
                    fontSize = 10, color = "#475569", hideOverlap = true,
                    rotate = daily.Count > 31 ? 40 : 0,
                    formatter = "@fn:agAxisLabel",
                },
            },
            ["yAxis"] = new
            {
                type = "value", name = "AQI", nameGap = 12,
                nameTextStyle = new { color = "#475569", fontSize = 10, fontWeight = 600, align = "left" },
                axisLabel = new { fontSize = 10, color = "#475569" },
                splitLine = new { lineStyle = new { color = "#eef2f7" } },
            },
            ["series"] = new object[]
            {
                new
                {
                    type = "bar", name = "Daily AQI",
                    data = daily.Select(d => (double?)d.AqiAvg).ToList(),
                    itemStyle = new { color = "@fn:aqiBarColor", borderRadius = new[] { 2, 2, 0, 0 } },
                    barMaxWidth = 22,
                },
            },
        }, ctx: new
        {
            daily = daily.Select(d => new
            {
                date = d.Date, n = d.N, pm25Avg = d.Pm25Avg, pm25Min = d.Pm25Min,
                pm25Max = d.Pm25Max, aqiAvg = d.AqiAvg, aqiMax = d.AqiMax,
            }).ToList(),
        });

        // Period overview — recomputed from the displayed records.
        var withData = daily.Where(d => d.AqiAvg is not null).ToList();
        if (withData.Count > 0)
        {
            var n = withData.Sum(d => d.N);
            var tw = withData.Sum(d => d.N > 0 ? d.N : 1);
            var pm = Math.Round(withData.Sum(d => d.Pm25Avg * (d.N > 0 ? d.N : 1)) / tw * 10) / 10;
            var mn = withData.Min(d => d.Pm25Min);
            var mx = withData.Max(d => d.Pm25Max);
            var cats = new Dictionary<string, int>();
            foreach (var d in withData)
                if (Aqi.CatShort(d.AqiAvg) is string cs) cats[cs] = cats.GetValueOrDefault(cs) + 1;
            var aqiAvg = Aqi.Pm25ToAqi(pm);
            var aqiMax = Aqi.Pm25ToAqi(mx);
            var avg = Aqi.Category(aqiAvg);
            var peak = Aqi.Category(aqiMax);
            var catHtml = string.Join("", cats.Select(kv =>
                $"""<span class="ov-cat"><i style="background:{CatColors.GetValueOrDefault(kv.Key, "#94a3b8")}"></i>{kv.Key}: <b>{kv.Value}d</b></span>"""));

            await Dom.SetHtml("aqicn-overview", $"""
                <table class="ov-table">
                  <tbody>
                    <tr><th>Period</th><td>{Fmt.FormatDate(withData[0].Date)} – {Fmt.FormatDate(withData[^1].Date)}</td><th>Days w/ data</th><td>{withData.Count}</td></tr>
                    <tr><th>Samples</th><td>{n:N0}</td><th>Avg PM2.5</th><td>{pm} µg/m³</td></tr>
                    <tr><th>PM2.5 range</th><td>{Math.Round(mn * 10) / 10} – {Math.Round(mx * 10) / 10}</td><th>Peak AQI</th><td style="color:{peak.Color};font-weight:700">{aqiMax}</td></tr>
                    <tr><th>Avg AQI</th><td colspan="3" style="color:{avg.Color};font-weight:700">{aqiAvg} · {avg.Label}</td></tr>
                  </tbody>
                </table>
                <div class="ov-cats">{catHtml}</div>
                """);
        }
        await Dom.SetText("aqicn-history-note",
            $"AirGradient · {sourceLabel} · showing {daily.Count} day{(daily.Count == 1 ? "" : "s")}.");
    }

    // Download the currently displayed daily history as CSV.
    public static async Task ExportCsv()
    {
        if (AgDaily.Count == 0) return;
        var sb = new StringBuilder("date,samples,pm25_avg_ugm3,pm25_min,pm25_max,aqi_avg,aqi_max");
        foreach (var d in AgDaily)
            sb.Append($"\r\n{d.Date},{d.N},{d.Pm25Avg},{d.Pm25Min},{d.Pm25Max},{d.AqiAvg},{d.AqiMax}");
        await Dom.DownloadCsv($"hulhumale-station-aqi_{AgDaily[0].Date}_to_{AgDaily[^1].Date}.csv", sb.ToString());
    }
}
