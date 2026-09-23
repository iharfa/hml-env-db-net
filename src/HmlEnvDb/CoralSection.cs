using System.Text.Json.Nodes;

namespace HmlEnvDb;

// Coral Bleaching Watch section. The NOAA gauge image loads directly in the
// page (an <img> isn't subject to CORS); the structured text feed usually is
// CORS-blocked, so the info boxes fall back to the marine-model SST and a link
// to the official monitoring page.
public static class CoralSection
{
    public static async Task UpdateCoral(CoralResult? coralResult)
    {
        if (coralResult is null || coralResult.Status == "cors-blocked" || coralResult.Raw is null)
        {
            await ShowFallback();
            return;
        }

        var records = CoralData.ParseCoralText(coralResult.Raw);
        if (records.Count == 0)
        {
            await Dom.SetText("coral-data-pill", "Not live");
            await Dom.SetClass("coral-data-pill", "pill pill-not-live");
            await Dom.SetDisplay("coral-fallback-card", true);
            return;
        }

        var last = records[^1];
        var alertLabel = CoralData.AlertLabel(last.Alert);
        var alertColor = CoralData.AlertColor(last.Alert);

        await Dom.SetText("coral-data-pill", "Live");
        await Dom.SetClass("coral-data-pill", "pill pill-live");

        await Dom.SetHtml("coral-layout", $"""
            <div class="coral-card {CoralData.AlertClass(last.Alert)}">
              <div class="cc-icon" style="color:{alertColor}">{Icons.Icon("coral")}</div>
              <div class="cc-name">Bleaching Status</div>
              <div class="cc-value" style="color:{alertColor}">{alertLabel}</div>
              <div class="cc-sub">Maldives region</div>
              <div class="cc-pill">{Icons.GetStatusBadge("live")}</div>
            </div>
            <div class="coral-card">
              <div class="cc-icon" style="color:#0284c7">{Icons.Icon("sst")}</div>
              <div class="cc-name">Sea Surface Temp</div>
              <div class="cc-value">{Fmt.FormatNumber(last.Sst)}</div>
              <div class="cc-unit">°C</div>
              <div class="cc-pill">{Icons.GetStatusBadge("live")}</div>
            </div>
            <div class="coral-card">
              <div class="cc-icon" style="color:{(last.Ssta > 0 ? "#dc2626" : "#16a34a")}">{Icons.Icon("temperature")}</div>
              <div class="cc-name">SST Anomaly</div>
              <div class="cc-value" style="color:{(last.Ssta > 0 ? "#dc2626" : "#16a34a")}">{(last.Ssta > 0 ? "+" : "")}{Fmt.FormatNumber(last.Ssta, 2)}</div>
              <div class="cc-unit">°C above baseline</div>
              <div class="cc-pill">{Icons.GetStatusBadge("live")}</div>
            </div>
            <div class="coral-card">
              <div class="cc-icon" style="color:#ea580c">{Icons.Icon("warning")}</div>
              <div class="cc-name">Degree Heating Weeks</div>
              <div class="cc-value">{Fmt.FormatNumber(last.Dhw)}</div>
              <div class="cc-unit">°C-weeks</div>
              <div class="cc-sub">≥4 = bleaching risk</div>
              <div class="cc-pill">{Icons.GetStatusBadge("live")}</div>
            </div>
            """);

        // DHW trend, shown only when a live series was actually parsed.
        if (records.Count > 7)
        {
            await Dom.SetDisplay("coral-chart-row", true);
            await RenderDhwChart(records);
        }
    }

    private static async Task ShowFallback()
    {
        await Dom.SetText("coral-data-pill", "Not live");
        await Dom.SetClass("coral-data-pill", "pill pill-not-live");
        await Dom.SetDisplay("coral-fallback-card", true);

        // Static informational cards using SST from the marine model (if available).
        var mh = Data.Marine?["hourly"];
        double? sst = null;
        if (mh?["sea_surface_temperature"] is not null)
        {
            var hi = Fmt.GetCurrentHourIndex(Js.Strs(mh["time"]));
            sst = Js.NumAt(mh["sea_surface_temperature"], hi);
        }

        await Dom.SetHtml("coral-layout", $"""
            <div class="coral-card">
              <div class="cc-icon" style="color:#0284c7">{Icons.Icon("sst")}</div>
              <div class="cc-name">Sea Surface Temp</div>
              <div class="cc-value">{Fmt.FormatNumber(sst)}</div>
              <div class="cc-unit">°C (marine model)</div>
              <div class="cc-pill">{Icons.GetStatusBadge("live-derived")}</div>
            </div>
            <div class="coral-card">
              <div class="cc-icon" style="color:#ca8a04">{Icons.Icon("coral")}</div>
              <div class="cc-name">Bleaching Status</div>
              <div class="cc-value" style="font-size:0.9rem;">See NOAA page</div>
              <div class="cc-sub">Structured data not accessible from browser</div>
              <div class="cc-pill">{Icons.GetStatusBadge("not-live")}</div>
            </div>
            <div class="coral-card">
              <div class="cc-icon" style="color:#ea580c">{Icons.Icon("warning")}</div>
              <div class="cc-name">Degree Heating Weeks</div>
              <div class="cc-value">—</div>
              <div class="cc-sub">Not available in-browser</div>
              <div class="cc-pill">{Icons.GetStatusBadge("not-live")}</div>
            </div>
            <div class="coral-card">
              <div class="cc-icon" style="color:#dc2626">{Icons.Icon("heatmap")}</div>
              <div class="cc-name">HotSpot</div>
              <div class="cc-value">—</div>
              <div class="cc-sub">Not available in-browser</div>
              <div class="cc-pill">{Icons.GetStatusBadge("not-live")}</div>
            </div>
            """);
    }

    private static async Task RenderDhwChart(List<CoralRecord> records)
    {
        var slice = records.Skip(Math.Max(0, records.Count - 90)).ToList();
        await Sections.Chart("chart-dhw", new Dictionary<string, object?>
        {
            ["tooltip"] = new { trigger = "axis", formatter = "@fn:dhwTooltip" },
            ["grid"] = new { left = 48, right = 48, top = 24, bottom = 36 },
            ["xAxis"] = new { type = "category", data = slice.Select(r => r.Doy).ToList(), axisLabel = new { fontSize = 10 } },
            ["yAxis"] = new object[]
            {
                new { type = "value", name = "DHW", axisLabel = new { fontSize = 10 }, splitLine = new { lineStyle = new { color = "#f1f5f9" } } },
                new { type = "value", name = "°C", axisLabel = new { fontSize = 10 } },
            },
            ["series"] = new object[]
            {
                new
                {
                    name = "DHW", type = "bar",
                    data = slice.Select(r => (double?)r.Dhw).ToList(),
                    itemStyle = new { color = "@fn:dhwBarColor", borderRadius = new[] { 3, 3, 0, 0 } },
                },
                new
                {
                    name = "SST", type = "line", yAxisIndex = 1,
                    data = slice.Select(r => (double?)r.Sst).ToList(),
                    lineStyle = new { color = "#0ea5e9" }, symbol = "none",
                },
            },
        });
    }
}
