using System.Text;
using System.Text.Json.Nodes;

namespace HmlEnvDb;

// Weather, Air Quality, Tide & Marine and Data Status sections — the HTML the
// original built with template strings is built here and set via interop.
public static class Sections
{
    // ── WEATHER ──

    public static async Task UpdateWeather(JsonNode? weather)
    {
        if (weather is null) return;
        var times = Js.Strs(weather["hourly"]?["time"]);
        var hi = times.Count > 0 ? Fmt.GetCurrentHourIndex(times) : 0;
        var h = weather["hourly"];
        var c = weather["current"];
        var d = weather["daily"];

        if (h is not null && c is not null)
        {
            var rp = Js.NumAt(h["precipitation_probability"], hi);
            var pr = Js.NumAt(h["precipitation"], hi);
            var cards = new (string Name, string Icon, string Value, string Unit, string Sub)[]
            {
                ("Temperature", "temperature", Fmt.FormatNumber(Js.Num(c["temperature_2m"])), "°C",
                    $"Feels like {Fmt.FormatNumber(Js.Num(c["temperature_2m"]), 0)}°C"),
                ("Wind", "wind", Math.Round(Js.Num(c["windspeed_10m"]) ?? 0).ToString("0"), "km/h",
                    Fmt.GetWindDirection(Js.Num(c["winddirection_10m"]))),
                ("Rain Probability", "rain", rp is double rpv ? Math.Round(rpv).ToString("0") : "—", "%",
                    pr is double prv ? $"{Fmt.FormatNumber(prv)} mm" : ""),
                ("Humidity", "humidity", Math.Round(Js.Num(c["relativehumidity_2m"]) ?? 0).ToString("0"), "%", ""),
                ("Pressure", "pressure", Math.Round(Js.Num(c["surface_pressure"]) ?? 0).ToString("0"), "hPa", ""),
                ("UV Index", "uv", Fmt.FormatNumber(Js.Num(c["uv_index"])), "", Fmt.UvLabel(Js.Num(c["uv_index"]))),
                ("Conditions", "rain", "", "", Fmt.GetWeatherLabel(Js.Num(c["weathercode"]))),
            };
            var sb = new StringBuilder();
            foreach (var tc in cards)
            {
                sb.Append($"""
                    <div class="today-card">
                      <div class="tc-icon">{Icons.Icon(tc.Icon)}</div>
                      <div class="tc-name">{tc.Name}</div>
                      <div class="tc-value">{tc.Value}</div>
                      <div class="tc-unit">{tc.Unit}</div>
                      {(tc.Sub.Length > 0 ? $"""<div class="tc-sub">{tc.Sub}</div>""" : "")}
                    </div>
                    """);
            }
            await Dom.SetHtml("today-grid", sb.ToString());
        }

        // Weekly outlook strip
        if (d is not null)
        {
            var today = Fmt.GetTodayDateStr();
            var dTimes = Js.Strs(d["time"]);
            var sb = new StringBuilder();
            var shown = 0;
            for (var i = 0; i < dTimes.Count && shown < 7; i++)
            {
                var dt = dTimes[i];
                if (string.CompareOrdinal(dt, today) < 0) continue;
                var label = i == 0 ? "Today" : Fmt.WeekdayShort(DateTime.Parse(dt));
                var tmax = Js.NumAt(d["temperature_2m_max"], i);
                var tmin = Js.NumAt(d["temperature_2m_min"], i);
                var rp = Js.NumAt(d["precipitation_probability_max"], i);
                int? rpv = rp is double r ? (int)Math.Round(r) : null;
                var rainClass = rpv is null ? "" : rpv >= 60 ? "fc-rain-high" : rpv >= 30 ? "fc-rain-med" : "fc-rain-low";
                sb.Append($"""
                    <div class="forecast-card{(i == 0 ? " is-today" : "")}">
                      <div class="fc-day">{label}</div>
                      <div class="fc-icon">{Icons.Icon("temperature")}</div>
                      <div class="fc-temp">{(tmax is double tx ? Math.Round(tx).ToString("0") : "—")}°</div>
                      <div class="fc-range">{(tmin is double tn ? Math.Round(tn).ToString("0") : "—")}° – {(tmax is double tx2 ? Math.Round(tx2).ToString("0") : "—")}°</div>
                      <div class="fc-rain {rainClass}">
                        <div class="fc-rain-bar"><span style="width:{rpv ?? 0}%"></span></div>
                        <div class="fc-rain-val">{Icons.Matsym("rain")}{(rpv is int rv ? rv.ToString() : "—")}%</div>
                      </div>
                      <div class="fc-sub">{Fmt.GetWeatherLabel(Js.NumAt(d["weathercode"], i))}</div>
                    </div>
                    """);
                shown++;
            }
            await Dom.SetHtml("forecast-strip", sb.ToString());
        }
    }

    public static async Task RenderWeatherCharts(JsonNode? weather)
    {
        var h = weather?["hourly"];
        if (h is null) return;
        var times = Js.Strs(h["time"]);
        var hi = Fmt.GetCurrentHourIndex(times);
        const int next48 = 49;
        var t = Slice(times, hi, next48);

        await Chart("chart-temp", Charts.BuildLineChartOption(t,
            new() { new() { Name = "Temperature", Data = Slice(Js.Nums(h["temperature_2m"]), hi, next48), Color = "#f97316" } }, "°C"));

        await Chart("chart-wind", Charts.BuildLineChartOption(t,
            new() { new() { Name = "Wind Speed", Data = Slice(Js.Nums(h["windspeed_10m"]), hi, next48), Color = "#06b6d4" } }, "km/h"));

        var rain = Charts.BuildLineChartOption(t,
            new() { new() { Name = "Rain Probability", Data = Slice(Js.Nums(h["precipitation_probability"]), hi, next48), Color = "#3b82f6" } }, "%");
        rain["yAxis"] = new
        {
            type = "value", name = "%", min = 0, max = 100,
            axisLabel = new { formatter = "{value}%", fontSize = 10 },
            splitLine = new { lineStyle = new { color = "#f1f5f9" } },
        };
        await Chart("chart-rain", rain);

        var d = weather?["daily"];
        if (d is not null)
        {
            await Chart("chart-rain-5d", new Dictionary<string, object?>
            {
                ["tooltip"] = new { trigger = "axis", formatter = "@fn:rain5dTooltip" },
                ["grid"] = new { left = 40, right = 16, top = 24, bottom = 32 },
                ["xAxis"] = new
                {
                    type = "category",
                    data = Js.Strs(d["time"]).Select(Fmt.FormatDate).ToList(),
                    axisLabel = new { fontSize = 10, rotate = 20 },
                },
                ["yAxis"] = new object[]
                {
                    new { type = "value", name = "mm", axisLabel = new { fontSize = 10 }, splitLine = new { lineStyle = new { color = "#f1f5f9" } } },
                    new { type = "value", name = "%", min = 0, max = 100, axisLabel = new { fontSize = 10 } },
                },
                ["series"] = new object[]
                {
                    new { name = "Rainfall", type = "bar", data = Js.Nums(d["precipitation_sum"]),
                          itemStyle = new { color = "#3b82f6", borderRadius = new[] { 3, 3, 0, 0 } } },
                    new { name = "Rain Prob", type = "line", yAxisIndex = 1, data = Js.Nums(d["precipitation_probability_max"]),
                          lineStyle = new { color = "#94a3b8", type = "dashed" }, symbol = "none" },
                },
            });
        }
    }

    // ── AIR QUALITY ──

    public static double? ComputeAqiFromPollutants(JsonNode? aq, int hi)
    {
        var h = aq?["hourly"];
        if (h is null) return null;
        var keys = new[] { "us_aqi_pm2_5", "us_aqi_pm10", "us_aqi_nitrogen_dioxide", "us_aqi_ozone", "us_aqi_sulphur_dioxide", "us_aqi_carbon_monoxide" };
        var vals = keys.Select(k => Js.NumAt(h[k], hi)).Where(v => v is not null).Select(v => v!.Value).ToList();
        return vals.Count > 0 ? vals.Max() : null;
    }

    public static async Task UpdateAirQuality(JsonNode? aq)
    {
        if (aq?["hourly"] is null)
        {
            await RenderAqiGauge(null);
            return;
        }
        var times = Js.Strs(aq["hourly"]!["time"]);
        var hi = Fmt.GetCurrentHourIndex(times);
        var aqiVal = Js.NumAt(aq["hourly"]!["us_aqi"], hi) ?? ComputeAqiFromPollutants(aq, hi);

        await RenderAqiGauge(aqiVal);
        await RenderPollutantCards(aq, hi);
        await RenderAqiTrends(aq);
    }

    public static async Task RenderAqiGauge(double? aqiVal)
    {
        var has = aqiVal is double d && !double.IsNaN(d);
        var v = has ? (int)Math.Max(0, Math.Min(500, Math.Round(aqiVal!.Value))) : 0;
        var band = Aqi.BandFor(has ? v : 0);
        var markerLeft = has ? v / 500.0 * 100 : 0;

        var segs = string.Join("", Aqi.Bands.Select(b =>
        {
            var active = has && b == band;
            return $"""<div class="aqi-seg" style="flex:{b.Hi - b.Lo + 1};background:{b.Color};opacity:{(active ? "1" : "0.28")}"></div>""";
        }));
        var ticks = string.Join("", Aqi.Bands.Select(b =>
        {
            var active = has && b == band;
            return $"""<div class="aqi-tick" style="flex:{b.Hi - b.Lo + 1}{(active ? $";color:{b.Color}" : "")}">{b.Lo}</div>""";
        }));

        await Dom.SetHtml("chart-aqi-gauge", $"""
            <div class="aqi-scale">
              <div class="aqi-scale-top">
                <div class="aqi-scale-readout">
                  <div class="aqi-scale-value" style="color:{(has ? band.Color : "var(--c-text-3)")}">{(has ? v.ToString() : "—")}</div>
                  <div class="aqi-scale-meta">
                    <div class="aqi-scale-cat">{(has ? band.Name : "No live value")}</div>
                    <div class="aqi-scale-sub">US EPA AQI</div>
                  </div>
                </div>
                <div class="aqi-scale-note">{(has ? band.Note : "Air quality data unavailable.")}</div>
              </div>
              <div class="aqi-scale-bar">
                <div class="aqi-scale-marker" style="left:{markerLeft.ToString("0.###")}%;opacity:{(has ? "1" : "0")}"><span style="background:{band.Color}"></span></div>
                <div class="aqi-scale-bands">{segs}</div>
                <div class="aqi-scale-ticks">{ticks}</div>
              </div>
            </div>
            """);
    }

    private record PollutantSpec(string? Key, string? AqiKey, string InfoKey, string FullName, string Symbol, string Unit, string IconName);

    private static readonly PollutantSpec[] Pollutants =
    {
        new("pm2_5", "us_aqi_pm2_5", "pm25", "Fine Particulate Matter", "PM2.5", "µg/m³", "pm25"),
        new("pm10", "us_aqi_pm10", "pm10", "Coarse Particulate Matter", "PM10", "µg/m³", "pm10"),
        new("dust", null, "dust", "Atmospheric Dust", "Dust", "µg/m³", "dust"),
        new(null, "us_aqi_nitrogen_dioxide", "no2", "Nitrogen Dioxide", "NO₂", "AQI", "aqi"),
        new(null, "us_aqi_ozone", "ozone", "Ground-Level Ozone", "O₃", "AQI", "uv"),
        new(null, "us_aqi_sulphur_dioxide", "so2", "Sulphur Dioxide", "SO₂", "AQI", "warning"),
        new(null, "us_aqi_carbon_monoxide", "co", "Carbon Monoxide", "CO", "AQI", "aqi"),
        new("uv_index", null, "uv", "Ultraviolet Index", "UV", "", "uv"),
    };

    public static async Task RenderPollutantCards(JsonNode aq, int hi)
    {
        var h = aq["hourly"]!;
        var sb = new StringBuilder();
        foreach (var p in Pollutants)
        {
            var raw = p.Key is not null ? Js.NumAt(h[p.Key], hi) : null;
            var aqiV = p.AqiKey is not null ? Js.NumAt(h[p.AqiKey], hi) : null;
            var display = raw is double r ? Fmt.FormatNumber(r)
                        : aqiV is double a ? Math.Round(a).ToString("0") : "—";
            var unitLabel = raw is not null ? p.Unit : (aqiV is not null ? "AQI" : p.Unit);
            var aqiCat = aqiV is not null ? Aqi.GetUsAqiCategory(aqiV) : null;
            sb.Append($"""
                <div class="pollutant-card">
                  <div class="pollutant-card-top">
                    <div class="pollutant-icon">{Icons.Icon(p.IconName)}</div>
                    <button class="pollutant-info-btn" onclick="showPollutantInfo('{p.InfoKey}')" title="More info about {p.FullName}" aria-label="More info about {p.FullName}">ⓘ</button>
                  </div>
                  <div class="pollutant-symbol">{p.Symbol}</div>
                  <div class="pollutant-fullname">{p.FullName}</div>
                  <div class="pollutant-value" style="{(aqiCat is not null ? $"color:{aqiCat.Color}" : "")}">{display}</div>
                  <div class="pollutant-unit">{unitLabel}</div>
                  {(aqiCat is not null ? $"""<div class="pollutant-aqi" style="color:{aqiCat.Color}">{aqiCat.Label}</div>""" : "")}
                </div>
                """);
        }
        await Dom.SetHtml("pollutant-grid", sb.ToString());
    }

    public static async Task RenderAqiTrends(JsonNode aq)
    {
        var h = aq["hourly"];
        if (h is null) return;
        var times = Js.Strs(h["time"]);
        var values = Js.Nums(h["us_aqi"]);
        var hi = Fmt.GetCurrentHourIndex(times);

        // 24h trend (past 12h + next 12h)
        var start24 = Math.Max(0, hi - 12);
        var end24 = Math.Min(times.Count, hi + 13);
        await Chart("chart-aqi-24h", Charts.BuildLineChartOption(
            times.GetRange(start24, end24 - start24),
            new() { new() { Name = "US AQI", Data = values.GetRange(start24, end24 - start24), Color = "#0ea5e9" } },
            "AQI", markIndex: hi - start24, thresholdBands: true));

        // 72h forecast
        var end72 = Math.Min(times.Count, hi + 73);
        await Chart("chart-aqi-72h", Charts.BuildLineChartOption(
            times.GetRange(hi, end72 - hi),
            new() { new() { Name = "AQI Forecast", Data = values.GetRange(hi, end72 - hi), Color = "#6366f1", Dashed = true } },
            "AQI"));
    }

    // ── TIDE & MARINE ──

    public static TideSeries? GetTideSeries(JsonNode? marine)
    {
        if (Data.Tides is { } t && t.Time.Count > 0) return t;
        var h = marine?["hourly"];
        if (h is null) return null;
        return new TideSeries { Time = Js.Strs(h["time"]), Heights = Js.Nums(h["sea_level_height_msl"]), IsReal = false };
    }

    public static async Task UpdateMarine(JsonNode? marine)
    {
        var h = marine?["hourly"];
        if (h is null)
        {
            await Dom.SetHtml("marine-card-grid", """<div class="error-card">Marine data temporarily unavailable.</div>""");
            return;
        }
        var times = Js.Strs(h["time"]);
        var hi = Fmt.GetCurrentHourIndex(times);

        var tide = GetTideSeries(marine)!;
        var ti = tide.IsReal ? Fmt.NearestTimeIndex(tide.Time) : hi;
        var tides = HmlEnvDb.Tides.Detect(tide.Time, tide.Heights);
        var nextHigh = tides.FirstOrDefault(t => t.Type == "high" && t.Index >= ti);
        var nextLow = tides.FirstOrDefault(t => t.Type == "low" && t.Index >= ti);
        var tideFlat = !tide.IsReal && tides.Count < 2;

        await Dom.SetText("marine-source", tide.IsReal ? "MSRO + Open-Meteo" : "Open-Meteo");

        // Break the total down into swell vs locally wind-driven wave.
        var waveBits = new List<string>();
        if (Js.NumAt(h["swell_wave_height"], hi) is double sw) waveBits.Add($"Swell {Fmt.FormatNumber(sw)} m");
        if (Js.NumAt(h["wind_wave_height"], hi) is double wwv) waveBits.Add($"wind wave {Fmt.FormatNumber(wwv)} m");
        if (Js.NumAt(h["wave_period"], hi) is double wp) waveBits.Add($"{Fmt.FormatNumber(wp, 0)}s");

        var cards = new (string Name, string Icon, string Value, string Unit, string Sub)[]
        {
            ("Tide Level", "wave", Fmt.FormatNumber(tide.Heights.ElementAtOrDefault(ti), 2), "m",
                tideFlat ? "Coarse model resolution" : (nextHigh is not null ? $"Next high: {Fmt.FormatTime(nextHigh.Time)}" : "")),
            ("Next High Tide", "highTide", nextHigh is not null ? Fmt.FormatTime(nextHigh.Time) : "—", "",
                nextHigh is not null ? $"{Fmt.FormatNumber(nextHigh.Value, 2)} m" : "Not detected"),
            ("Next Low Tide", "lowTide", nextLow is not null ? Fmt.FormatTime(nextLow.Time) : "—", "",
                nextLow is not null ? $"{Fmt.FormatNumber(nextLow.Value, 2)} m" : "Not detected"),
            ("Wave Height", "wave", Fmt.FormatNumber(Js.NumAt(h["wave_height"], hi), 2), "m", string.Join(" · ", waveBits)),
            ("Sea Surface Temp", "sst", Fmt.FormatNumber(Js.NumAt(h["sea_surface_temperature"], hi)), "°C", ""),
            ("Ocean Current", "current", Fmt.FormatNumber(Js.NumAt(h["ocean_current_velocity"], hi), 2), "m/s",
                Fmt.GetWindDirection(Js.NumAt(h["ocean_current_direction"], hi))),
        };

        if (tideFlat)
        {
            var warn = Icons.Icon("warning").Replace("<svg", """<svg style="display:inline;width:14px;height:14px;vertical-align:middle;margin-right:4px" """);
            await Dom.InsertTideNote($"{warn} Tide variation available. High and low tide estimates may be limited due to model resolution.");
        }

        var sb = new StringBuilder();
        foreach (var c in cards)
        {
            sb.Append($"""
                <div class="marine-card">
                  <div class="mc-icon">{Icons.Icon(c.Icon)}</div>
                  <div class="mc-name">{c.Name}</div>
                  <div class="mc-value">{c.Value}</div>
                  <div class="mc-unit">{c.Unit}</div>
                  {(c.Sub.Length > 0 ? $"""<div class="mc-sub">{c.Sub}</div>""" : "")}
                </div>
                """);
        }
        await Dom.SetHtml("marine-card-grid", sb.ToString());
    }

    public static async Task RenderMarineCharts(JsonNode? marine)
    {
        var h = marine?["hourly"];
        if (h is null) return;
        var times = Js.Strs(h["time"]);
        var hi = Fmt.GetCurrentHourIndex(times);

        var tide = GetTideSeries(marine)!;
        var ti = tide.IsReal ? Fmt.NearestTimeIndex(tide.Time) : hi;

        // Tide today (same day)
        var todayStr = Fmt.GetTodayDateStr();
        var tideTimes = new List<string>();
        var tideValues = new List<double?>();
        for (var i = 0; i < tide.Time.Count; i++)
        {
            if (!tide.Time[i].StartsWith(todayStr)) continue;
            tideTimes.Add(tide.Time[i]);
            tideValues.Add(tide.Heights[i]);
        }
        var todayTides = HmlEnvDb.Tides.Detect(tideTimes, tideValues);
        await Chart("chart-tide-today", TideChartCfg(tideTimes, tideValues,
            todayTides.Select(t => TideMark(t.Type, t.Time, t.Value)).ToList()));

        // Tide 48h — sample count depends on resolution (hourly model vs 1-min gauge).
        var stepMin = 60;
        if (tide.Time.Count > 1 && DateTime.TryParse(tide.Time[0], out var t0) && DateTime.TryParse(tide.Time[1], out var t1))
            stepMin = Math.Max(1, (int)Math.Round((t1 - t0).TotalMinutes));
        var span48 = 48 * 60 / stepMin + 1;
        var end48 = Math.Min(tide.Time.Count, ti + span48);
        var times48 = tide.Time.GetRange(ti, end48 - ti);
        var values48 = tide.Heights.GetRange(ti, end48 - ti);
        var tides48 = HmlEnvDb.Tides.Detect(times48, values48);
        await Chart("chart-tide-48h", TideChartCfg(times48, values48,
            tides48.Select(t => TideMark(t.Type, tide.Time[ti + t.Index], t.Value)).ToList()));

        // Wave height + SST
        await Chart("chart-wave", Charts.BuildLineChartOption(Slice(times, hi, 49),
            new() { new() { Name = "Wave Height", Data = Slice(Js.Nums(h["wave_height"]), hi, 49), Color = "#06b6d4" } }, "m"));

        await Chart("chart-sst", Charts.BuildLineChartOption(Slice(times, hi, 49),
            new() { new() { Name = "SST", Data = Slice(Js.Nums(h["sea_surface_temperature"]), hi, 49), Color = "#f97316" } }, "°C"));
    }

    // High/low tide markers, labelled above (high) / below (low) the line.
    private static object TideMark(string type, string coordX, double value) => new
    {
        name = type == "high" ? "▲ High" : "▼ Low",
        coord = new object[] { coordX, value },
        symbolSize = 0,
        label = new
        {
            show = true,
            position = type == "high" ? "top" : "bottom",
            formatter = "@fn:tideMarkLabel",
            color = type == "high" ? "#0284c7" : "#475569",
            fontSize = 10, fontWeight = 600, lineHeight = 12,
            backgroundColor = "rgba(255,255,255,0.9)", padding = new[] { 2, 5 }, borderRadius = 3,
        },
    };

    private static Dictionary<string, object?> TideChartCfg(List<string> xData, List<double?> data, List<object> tideMarks)
    {
        var vals = data.Where(v => v is not null).Select(v => v!.Value).ToList();
        double? yMin = vals.Count > 0 ? Math.Floor((vals.Min() - 0.05) * 10) / 10 : null;
        double? yMax = vals.Count > 0 ? Math.Ceiling((vals.Max() + 0.05) * 10) / 10 : null;
        var boundaries = Charts.DayBoundaryMarkLine(xData);

        return new Dictionary<string, object?>
        {
            ["tooltip"] = new { trigger = "axis", formatter = "@fn:tideTooltip" },
            ["grid"] = new { left = 50, right = 22, top = 34, bottom = 48 },
            ["xAxis"] = Charts.HourlyCategoryAxis(xData),
            ["yAxis"] = new Dictionary<string, object?>
            {
                ["type"] = "value", ["name"] = "m", ["nameGap"] = 12,
                ["nameTextStyle"] = new { color = "#475569", fontSize = 10, fontWeight = 600, align = "left" },
                ["min"] = yMin, ["max"] = yMax,
                ["interval"] = 0.1,
                ["axisLabel"] = new { formatter = "@fn:fixed1", fontSize = 10, color = "#475569" },
                ["splitLine"] = new { lineStyle = new { color = "#eef2f7" } },
            },
            ["series"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["name"] = "Sea Level",
                    ["type"] = "line",
                    ["data"] = data,
                    ["smooth"] = 0.4,
                    ["lineStyle"] = new { color = "#0ea5e9", width = 2.5 },
                    ["areaStyle"] = new
                    {
                        color = new
                        {
                            type = "linear", x = 0, y = 0, x2 = 0, y2 = 1,
                            colorStops = new object[]
                            {
                                new { offset = 0, color = "rgba(14,165,233,0.25)" },
                                new { offset = 1, color = "rgba(14,165,233,0.02)" },
                            },
                        },
                    },
                    ["symbol"] = "none",
                    ["markPoint"] = tideMarks.Count > 0 ? new { data = tideMarks } : null,
                    ["markLine"] = boundaries.Count > 0 ? new { silent = true, symbol = "none", data = boundaries } : null,
                },
            },
        };
    }

    // ── DATA STATUS ──

    public static async Task UpdateStatusTable(JsonNode? weather, JsonNode? aq, JsonNode? marine, CoralResult? coral)
    {
        var rows = new (string Name, string Detail, string Status)[]
        {
            ("Weather", "Hourly + 7-day forecast (Open-Meteo)", weather is not null ? "forecast" : "not-live"),
            ("Air Quality (model)", "Hourly US AQI + 72-h forecast (CAMS)", aq is not null ? "live" : "not-live"),
            ("Station Air Quality", "Malé (Galolhu) AirGradient sensor, live PM2.5", "live"),
            ("PM2.5 History", "Daily archive (AirGradient + WAQI feed)", "live-derived"),
            ("IQAir (AirVisual)", "Malé reading + national station map", "live"),
            ("Tides", Data.Tides is not null ? "MSRO tide gauge, 1-minute live readings" : "Not configured — falls back to modelled sea level",
                Data.Tides is not null ? "live" : "not-live"),
            ("Marine (Waves, SST, Currents)", "Hourly forecast model (Open-Meteo)", marine is not null ? "forecast" : "not-live"),
            ("Coral Bleaching Watch", coral?.Status == "ok" ? "Live time series parsed" : "CORS-limited, link provided",
                coral?.Status == "ok" ? "live" : "not-live"),
            ("Sun & Moon", "Sunrise, sunset and moon phase, computed (SunCalc)", "live-derived"),
        };
        var sb = new StringBuilder();
        foreach (var r in rows)
        {
            sb.Append($"""
                <div class="status-row">
                  <div>
                    <div class="sr-name">{r.Name}</div>
                    <div class="sr-detail">{r.Detail}</div>
                  </div>
                  {Icons.GetStatusBadge(r.Status)}
                </div>
                """);
        }
        await Dom.SetHtml("status-table", sb.ToString());
    }

    // ── shared helpers ──

    public static async Task Chart(string id, Dictionary<string, object?> option, object? ctx = null)
        => await Dom.RenderChart(id, Js.Ser(option), ctx is null ? null : Js.Ser(ctx));

    public static List<T> Slice<T>(List<T> list, int start, int count)
    {
        if (start >= list.Count) return new List<T>();
        return list.GetRange(start, Math.Min(count, list.Count - start));
    }
}
