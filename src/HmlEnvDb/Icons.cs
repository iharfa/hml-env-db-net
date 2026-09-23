namespace HmlEnvDb;

// The inline SVG icon set, verbatim from the original dashboard.
public static class Icons
{
    private static readonly Dictionary<string, string> Svg = new()
    {
        ["temperature"] = """<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M14 14.76V3.5a2.5 2.5 0 0 0-5 0v11.26a4.5 4.5 0 1 0 5 0z"/></svg>""",
        ["wind"] = """<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M17.7 7.7a2.5 2.5 0 1 1 1.8 4.3H2"/><path d="M9.6 4.6A2 2 0 1 1 11 8H2"/><path d="M12.6 19.4A2 2 0 1 0 14 16H2"/></svg>""",
        ["rain"] = """<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M20 17.58A5 5 0 0 0 18 8h-1.26A8 8 0 1 0 4 16.25"/><line x1="8" y1="19" x2="8" y2="21"/><line x1="12" y1="18" x2="12" y2="20"/><line x1="16" y1="19" x2="16" y2="21"/></svg>""",
        ["humidity"] = """<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 2.69l5.66 5.66a8 8 0 1 1-11.31 0z"/></svg>""",
        ["pressure"] = """<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M12 8v4l3 3"/><circle cx="12" cy="12" r="1" fill="currentColor"/></svg>""",
        ["aqi"] = """<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M8 5H6a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-1"/><rect x="8" y="2" width="8" height="6" rx="1"/><path d="M10.5 11.5l1.5 1.5 3-3"/></svg>""",
        ["pm25"] = """<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="9" cy="12" r="3"/><circle cx="15" cy="9" r="2"/><circle cx="16" cy="15" r="1.5"/><circle cx="7" cy="8" r="1"/></svg>""",
        ["pm10"] = """<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="8" cy="12" r="4"/><circle cx="17" cy="10" r="3"/><circle cx="16" cy="17" r="2"/></svg>""",
        ["dust"] = """<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M4 12h16M4 17h16M4 7h5"/><circle cx="12" cy="7" r="1.5"/><circle cx="17" cy="7" r="1"/></svg>""",
        ["uv"] = """<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="5"/><line x1="12" y1="1" x2="12" y2="3"/><line x1="12" y1="21" x2="12" y2="23"/><line x1="4.22" y1="4.22" x2="5.64" y2="5.64"/><line x1="18.36" y1="18.36" x2="19.78" y2="19.78"/><line x1="1" y1="12" x2="3" y2="12"/><line x1="21" y1="12" x2="23" y2="12"/><line x1="4.22" y1="19.78" x2="5.64" y2="18.36"/><line x1="18.36" y1="5.64" x2="19.78" y2="4.22"/></svg>""",
        ["highTide"] = """<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><polyline points="5 3 12 10 19 3"/><path d="M2 14c1.5 0 3 1 4.5 1S9.5 14 11 14s3 1 4.5 1S18.5 14 20 14"/><path d="M2 19c1.5 0 3 1 4.5 1S9.5 19 11 19s3 1 4.5 1S18.5 19 20 19"/></svg>""",
        ["lowTide"] = """<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><polyline points="5 21 12 14 19 21"/><path d="M2 9c1.5 0 3 1 4.5 1S9.5 9 11 9s3 1 4.5 1S18.5 9 20 9"/><path d="M2 4c1.5 0 3 1 4.5 1S9.5 4 11 4s3 1 4.5 1S18.5 4 20 4"/></svg>""",
        ["wave"] = """<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M2 6c.6.5 1.2 1 2.5 1C7 7 7 5 9.5 5s2.5 2 5 2 2.5-2 5-2"/><path d="M2 12c.6.5 1.2 1 2.5 1 2.5 0 2.5-2 5-2s2.5 2 5 2 2.5-2 5-2"/><path d="M2 18c.6.5 1.2 1 2.5 1 2.5 0 2.5-2 5-2s2.5 2 5 2 2.5-2 5-2"/></svg>""",
        ["current"] = """<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14"/><polyline points="13 5 20 12 13 19"/><path d="M5 5v14"/></svg>""",
        ["coral"] = """<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 22v-6"/><path d="M12 16c-2-2-4-3-4-6a4 4 0 0 1 8 0c0 3-2 4-4 6z"/><path d="M9 22h6"/><path d="M8 12c-2 0-4-1-4-3s2-3 4-3"/><path d="M16 12c2 0 4-1 4-3s-2-3-4-3"/></svg>""",
        ["sun"] = """<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/></svg>""",
        ["moon"] = """<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"/></svg>""",
        ["warning"] = """<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>""",
        ["ocean"] = """<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M2 8c2.5 0 2.5 2 5 2s2.5-2 5-2 2.5 2 5 2 2.5-2 5-2"/><circle cx="12" cy="14" r="4"/><path d="M12 10v4M9.5 12.5l2.5 2.5 2.5-2.5"/></svg>""",
        ["compass"] = """<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><polygon points="16.24 7.76 14.12 14.12 7.76 16.24 9.88 9.88 16.24 7.76" fill="currentColor" opacity=".3"/></svg>""",
        ["sst"] = """<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M2 12c.6.5 1.2 1 2.5 1C7 13 7 11 9.5 11s2.5 2 5 2 2.5-2 5-2"/><path d="M14 14.76V7.5a2 2 0 0 0-4 0v7.26a3.5 3.5 0 1 0 4 0z"/></svg>""",
    };

    public static string Icon(string name) => Svg.TryGetValue(name, out var s) ? s : Svg["warning"];

    // Material Symbol + colour shown on each briefing tile, keyed by icon name.
    private static readonly Dictionary<string, (string Sym, string Color)> AdvisorySymbol = new()
    {
        ["aqi"] = ("air", "#10b981"),
        ["warning"] = ("warning", "#ef4444"),
        ["uv"] = ("wb_sunny", "#f97316"),
        ["temperature"] = ("device_thermostat", "#10b981"),
        ["rain"] = ("rainy", "#3b82f6"),
        ["wind"] = ("cyclone", "#f59e0b"),
        ["wave"] = ("waves", "#06b6d4"),
        ["ocean"] = ("pool", "#06b6d4"),
        ["sun"] = ("wb_sunny", "#f97316"),
        ["moon"] = ("dark_mode", "#6366f1"),
    };

    public static string Matsym(string iconKey)
    {
        var m = AdvisorySymbol.TryGetValue(iconKey, out var v) ? v : AdvisorySymbol["warning"];
        return $"""<span class="material-symbols-outlined advisory-sym" style="color:{m.Color}">{m.Sym}</span>""";
    }

    public static string GetStatusBadge(string status) => status switch
    {
        "live" => """<span class="pill pill-live">Live</span>""",
        "forecast" => """<span class="pill pill-forecast">Forecast</span>""",
        "live-derived" => """<span class="pill pill-live-derived">Live-derived</span>""",
        "placeholder" => """<span class="pill pill-placeholder">Placeholder</span>""",
        "not-live" => """<span class="pill pill-not-live">Not live</span>""",
        _ => $"""<span class="pill">{status}</span>""",
    };
}
