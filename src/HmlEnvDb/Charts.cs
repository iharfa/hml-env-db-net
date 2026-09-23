namespace HmlEnvDb;

// Generic ECharts option builders. Options are plain object graphs serialized
// to JSON; anywhere ECharts needs a callback the option carries an "@fn:NAME"
// token that the interop layer swaps for the matching function from its small
// formatter registry (JS callbacks can't cross the WASM boundary).

public class LineSeriesSpec
{
    public string Name = "";
    public List<double?> Data = new();
    public string Color = "#0ea5e9";
    public bool Dashed;
}

public static class Charts
{
    // Shared x-axis for hourly (possibly multi-day) charts.
    public static object HourlyCategoryAxis(List<string> isoTimes)
    {
        // Label spacing follows the time span, not the sample count — the MSRO
        // gauge is 1-minute resolution while the model data is hourly.
        var spanHours = 0;
        if (isoTimes.Count > 1
            && DateTime.TryParse(isoTimes[^1], out var a) && DateTime.TryParse(isoTimes[0], out var b))
            spanHours = (int)Math.Round((a - b).TotalHours);
        var step = spanHours > 50 ? 6 : 3;

        return new
        {
            type = "category",
            data = isoTimes,
            boundaryGap = false,
            axisLine = new { lineStyle = new { color = "#cbd5e1" } },
            axisTick = new { show = false },
            axisLabel = new
            {
                fontSize = 10,
                color = "#64748b",
                rotate = 0,
                margin = 10,
                hideOverlap = true,
                interval = $"@fn:hourlyInterval:{step}",
                formatter = "@fn:hourlyAxisLabel",
                rich = new
                {
                    t = new { fontSize = 10, color = "#64748b", lineHeight = 13 },
                    d = new { fontSize = 10, fontWeight = 700, color = "#0f172a", lineHeight = 15, padding = new[] { 2, 0, 0, 0 } },
                },
            },
        };
    }

    // Vertical divider lines at each midnight, labelled with the weekday/date.
    public static List<object> DayBoundaryMarkLine(List<string> isoTimes)
    {
        var data = new List<object>();
        for (var i = 1; i < isoTimes.Count; i++)
        {
            if (isoTimes[i].Length >= 16 && isoTimes[i].Substring(11, 5) == "00:00")
            {
                data.Add(new
                {
                    xAxis = i,
                    lineStyle = new { color = "#cbd5e1", width = 1, type = "solid" },
                    label = new
                    {
                        show = true, position = "insideEndTop", rotate = 0, align = "center",
                        formatter = Fmt.FormatDayLabel(isoTimes[i]),
                        color = "#0f172a", fontSize = 10, fontWeight = 700,
                        backgroundColor = "rgba(255,255,255,0.85)", padding = new[] { 2, 4 }, borderRadius = 2,
                    },
                });
            }
        }
        return data;
    }

    public static object DefaultYAxis(string yName) => new
    {
        type = "value",
        name = yName,
        nameGap = 12,
        nameTextStyle = new { color = "#475569", fontSize = 10, fontWeight = 600, align = "left" },
        axisLabel = new { fontSize = 10, color = "#475569" },
        splitLine = new { lineStyle = new { color = "#eef2f7" } },
    };

    public static Dictionary<string, object?> BuildLineChartOption(
        List<string> xData, List<LineSeriesSpec> series, string yName,
        int? markIndex = null, bool thresholdBands = false, bool smooth = true)
    {
        var seriesObjs = new List<object>();
        for (var si = 0; si < series.Count; si++)
        {
            var s = series[si];
            // Day-divider lines + the optional "Now" marker ride on the first
            // series only, so they render once rather than once per line.
            var markLineData = new List<object>();
            if (si == 0)
            {
                markLineData.AddRange(DayBoundaryMarkLine(xData));
                if (markIndex is int mi)
                    markLineData.Add(new
                    {
                        xAxis = mi,
                        lineStyle = new { color = "#94a3b8", type = "dashed", width = 1 },
                        label = new { formatter = "Now", fontSize = 9, color = "#94a3b8", position = "insideEndTop" },
                    });
            }
            seriesObjs.Add(new Dictionary<string, object?>
            {
                ["name"] = s.Name,
                ["type"] = "line",
                ["data"] = s.Data,
                ["smooth"] = smooth ? 0.3 : 0,
                ["lineStyle"] = new { color = s.Color, width = 2, type = s.Dashed ? "dashed" : "solid" },
                ["areaStyle"] = s.Dashed ? null : new
                {
                    color = new
                    {
                        type = "linear", x = 0, y = 0, x2 = 0, y2 = 1,
                        colorStops = new object[]
                        {
                            new { offset = 0, color = s.Color + "44" },
                            new { offset = 1, color = s.Color + "08" },
                        },
                    },
                },
                ["symbol"] = "none",
                ["markLine"] = markLineData.Count > 0 ? new { silent = true, symbol = "none", data = markLineData } : null,
            });
        }

        var opt = new Dictionary<string, object?>
        {
            ["tooltip"] = new { trigger = "axis", formatter = "@fn:lineTooltip" },
            ["grid"] = new { left = 48, right = 18, top = 30, bottom = 48 },
            ["xAxis"] = HourlyCategoryAxis(xData),
            ["yAxis"] = DefaultYAxis(yName),
            ["series"] = seriesObjs,
        };
        if (thresholdBands)
            opt["visualMap"] = new
            {
                show = false,
                pieces = new object[]
                {
                    new { lte = 50, color = "#16a34a" }, new { gt = 50, lte = 100, color = "#ca8a04" },
                    new { gt = 100, lte = 150, color = "#ea580c" }, new { gt = 150, lte = 200, color = "#dc2626" },
                    new { gt = 200, lte = 300, color = "#7c3aed" }, new { gt = 300, color = "#7e0023" },
                },
            };
        return opt;
    }
}
