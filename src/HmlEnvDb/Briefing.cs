using System.Text.Json.Nodes;

namespace HmlEnvDb;

// The Daily Briefing — turns the raw readings into "what to expect / do".
// Rules are deliberately a flat list of thresholds, not a rule engine; adjust
// the numbers here to re-tune advice.

public record Advisory(string Icon, string Tone, string Tag, string Title, string Msg);

public static class Briefing
{
    private static readonly Dictionary<string, int> ToneRank = new() { ["good"] = 0, ["info"] = 0, ["watch"] = 1, ["alert"] = 2 };

    // Highest value in an hourly array over the next `hours` from index `hi`.
    private static double? PeakAhead(List<double?>? arr, int hi, int hours)
    {
        if (arr is null) return null;
        double? max = null;
        for (var i = hi; i < Math.Min(arr.Count, hi + hours); i++)
        {
            var v = arr[i];
            if (v is null) continue;
            if (max is null || v > max) max = v;
        }
        return max;
    }

    // Sea state on a 0-3 scale (calm / slight / choppy / rough); the worse of
    // wave height and wind gusts wins so the cards can't contradict each other.
    private static int? SeaStateFromWave(double? w) => w is null ? null : w >= 2.5 ? 3 : w >= 1.5 ? 2 : w >= 0.8 ? 1 : 0;
    private static int? SeaStateFromGust(double? g) => g is null ? null : g >= 60 ? 3 : g >= 45 ? 2 : g >= 30 ? 1 : 0;

    private class Conditions
    {
        public double WindPeak, GustPeak;
        public bool RainingNow;
        public double? WaveMax, WindWaveMax, SwellMax, CurrentMax;
        public int? WaveLevel, GustLevel;
        public int SeaLevel;
    }

    private static Conditions ReadConditions(JsonNode? weather, JsonNode? marine)
    {
        var c = new Conditions();
        var wh = weather?["hourly"];
        if (wh?["windspeed_10m"] is not null)
        {
            var times = Js.Strs(wh["time"]);
            var hi = Fmt.GetCurrentHourIndex(times);
            c.WindPeak = PeakAhead(Js.Nums(wh["windspeed_10m"]), hi, 12)
                ?? Js.Num(weather?["current"]?["windspeed_10m"]) ?? 0;
            // Estimate gusts when the model doesn't return them.
            c.GustPeak = PeakAhead(Js.Nums(wh["windgusts_10m"]), hi, 12) ?? c.WindPeak * 1.4;
        }
        if (wh?["precipitation_probability"] is not null)
        {
            var times = Js.Strs(wh["time"]);
            var hi = Fmt.GetCurrentHourIndex(times);
            c.RainingNow = (Js.NumAt(wh["precipitation_probability"], hi) ?? 0) >= 50
                || (Js.NumAt(wh["precipitation"], hi) ?? 0) > 0.2;
        }
        var mh = marine?["hourly"];
        if (mh is not null)
        {
            var mhi = Fmt.GetCurrentHourIndex(Js.Strs(mh["time"]));
            c.WaveMax = PeakAhead(Js.Nums(mh["wave_height"]), mhi, 12);
            c.WindWaveMax = PeakAhead(Js.Nums(mh["wind_wave_height"]), mhi, 12);
            c.SwellMax = PeakAhead(Js.Nums(mh["swell_wave_height"]), mhi, 12);
            c.CurrentMax = PeakAhead(Js.Nums(mh["ocean_current_velocity"]), mhi, 12);
        }
        c.WaveLevel = SeaStateFromWave(c.WaveMax);
        c.GustLevel = SeaStateFromGust(c.GustPeak);
        c.SeaLevel = Math.Max(c.WaveLevel ?? 0, c.GustLevel ?? 0);
        return c;
    }

    public static List<Advisory> BuildAdvisories(JsonNode? weather, JsonNode? aq, JsonNode? marine)
    {
        var outList = new List<Advisory>();
        var c = ReadConditions(weather, marine);

        // ── Air quality ──
        var aqh = aq?["hourly"];
        if (aqh is not null)
        {
            var hi = Fmt.GetCurrentHourIndex(Js.Strs(aqh["time"]));
            var aqiVal = Js.NumAt(aqh["us_aqi"], hi) ?? Sections.ComputeAqiFromPollutants(aq, hi);
            if (aqiVal is double av)
            {
                var v = (int)Math.Round(av);
                if (v <= 50) outList.Add(new("aqi", "good", "Good", $"Air quality is good (AQI {v})",
                    "Clean air right now. A great time to be outside and active."));
                else if (v <= 100) outList.Add(new("aqi", "watch", "Moderate", $"Air quality is moderate (AQI {v})",
                    "Fine for most people. If you’re unusually sensitive to air pollution, watch for irritation and ease up on hard exertion outdoors."));
                else if (v <= 150) outList.Add(new("aqi", "alert", "Sensitive", $"Unhealthy for sensitive groups (AQI {v})",
                    "People with asthma or heart/lung conditions, children and the elderly should limit long or intense activity outdoors. Keep reliever medication handy."));
                else outList.Add(new("warning", "alert", "Unhealthy", $"Air quality is unhealthy (AQI {v})",
                    "Everyone should cut back on strenuous outdoor activity. Sensitive groups stay indoors where possible and consider a well-fitted mask outside."));
            }
        }

        // ── Heat & sun (temperature + UV) ──
        var wh = weather?["hourly"];
        var wc = weather?["current"];
        if (wc is not null || wh is not null)
        {
            var times = wh is not null ? Js.Strs(wh["time"]) : new List<string>();
            var hi = times.Count > 0 ? Fmt.GetCurrentHourIndex(times) : 0;
            var tNow = Js.Num(wc?["temperature_2m"]);
            var tMax = Js.NumAt(weather?["daily"]?["temperature_2m_max"], 0)
                ?? PeakAhead(wh is not null ? Js.Nums(wh["temperature_2m"]) : null, hi, 12);
            var uvMax = PeakAhead(wh is not null ? Js.Nums(wh["uv_index"]) : null, hi, 14) ?? Js.Num(wc?["uv_index"]);

            // Window of strong sun today (hours with UV >= 6).
            var sunWindow = "";
            if (wh?["uv_index"] is not null && times.Count > 0)
            {
                var today = Fmt.GetTodayDateStr();
                var uv = Js.Nums(wh["uv_index"]);
                string? start = null, end = null;
                for (var i = 0; i < times.Count && i < uv.Count; i++)
                {
                    if (!times[i].StartsWith(today)) continue;
                    if ((uv[i] ?? 0) >= 6) { start ??= times[i]; end = times[i]; }
                }
                if (start is not null && end is not null)
                    sunWindow = $" Strongest sun is roughly {Fmt.FormatHourLabel(start)} to {Fmt.FormatHourLabel(end)}.";
            }

            var hot = (tMax ?? 0) >= 33 || (tNow ?? 0) >= 33;
            var warm = (tMax ?? 0) >= 31 || (tNow ?? 0) >= 31;
            var bigUV = (uvMax ?? 0) >= 8;
            var modUV = (uvMax ?? 0) >= 6;
            var wet = c.RainingNow;   // don't call it sunny while it's raining

            if (hot || bigUV) outList.Add(new("uv", "watch", bigUV ? "Very High UV" : "High UV",
                wet ? "Hot and muggy, UV still high" : "Hot with strong sun",
                wet
                    ? $"It’s hot and close, and UV stays {(bigUV ? "very high" : "high")} through the breaks between showers. Keep water on you, and don’t skip sunscreen just because it’s grey.{sunWindow}"
                    : $"It’s hot and UV is {(bigUV ? "very high" : "high")} today. Wear sunscreen, a hat and UV-protective clothing, drink plenty of water, and seek shade at midday.{sunWindow}"));
            else if (warm || modUV) outList.Add(new("uv", "good", "Warm",
                wet ? "Warm and humid" : "Warm and sunny",
                wet
                    ? $"Warm and humid with showers around. Sunscreen still earns its keep in the bright spells, and keep some water on you.{sunWindow}"
                    : $"Pleasant but sunny. Sunscreen and a hat are worth it around midday, and keep some water on you.{sunWindow}"));
            else outList.Add(new("temperature", "good", "Comfortable", "Comfortable conditions",
                "No extreme heat or UV expected. An easy day to be outdoors."));
        }

        // ── Rain ──
        if (wh?["precipitation_probability"] is not null)
        {
            var times = Js.Strs(wh["time"]);
            var hi = Fmt.GetCurrentHourIndex(times);
            const int horizon = 18;
            var prob = Js.Nums(wh["precipitation_probability"]);
            var firstWet = -1; double peak = 0;
            for (var i = hi; i < Math.Min(prob.Count, hi + horizon); i++)
            {
                var p = prob[i] ?? 0;
                if (p > peak) peak = p;
                if (firstWet == -1 && p >= 50) firstWet = i;
            }
            if (c.RainingNow) outList.Add(new("rain", "watch", "Raining", "Rain around now",
                "Showers are likely at the moment, so keep an umbrella or rain gear with you until it clears."));
            else if (firstWet != -1) outList.Add(new("rain", "watch", $"{Math.Round(peak)}% Chance",
                $"Rain likely around {Fmt.FormatHourLabel(times[firstWet])}",
                $"Showers become likely later (peak chance ~{Math.Round(peak)}%). Carry an umbrella or rain gear if you’ll be out then."));
            else if (peak >= 30) outList.Add(new("rain", "good", $"{Math.Round(peak)}% Chance", "Passing showers possible",
                $"Mostly dry, but a brief shower isn’t ruled out (up to ~{Math.Round(peak)}% chance). An umbrella is optional."));
            else outList.Add(new("rain", "good", "Dry", "Rain unlikely",
                "Little to no rain expected, so you can leave the umbrella at home."));
        }

        // ── Wind & crossing the bridge ──
        // Gusts, not the sustained average, are what shove a motorcycle out of
        // lane on the Sinamalé Bridge, so they set the rating.
        if (wh?["windspeed_10m"] is not null)
        {
            var g = Math.Round(c.GustPeak);
            var spray = (c.WaveMax ?? 0) >= 1.8;
            var sprayBit = spray ? $" Seas to ~{Fmt.FormatNumber(c.WaveMax)} m may throw spray across the deck." : "";

            if (c.GustPeak >= 60 || (c.GustPeak >= 45 && spray))
                outList.Add(new("wind", "alert", "Strong", "Strong crosswind on the bridge",
                    $"Gusts to around {g} km/h. Winds like this can push a motorcycle out of its lane on the Sinamalé Bridge, and crossings are sometimes restricted. Take a car or taxi if you can, and check official advisories before riding.{sprayBit}"));
            else if (c.GustPeak >= 45)
                outList.Add(new("wind", "watch", "Gusty", "Take care crossing the bridge",
                    $"Gusts to around {g} km/h. Expect a firm sideways push on the bridge, especially on a motorcycle. Slow down, keep both hands on the bars and leave extra room when overtaking.{sprayBit}"));
            else if (c.GustPeak >= 30)
                outList.Add(new("wind", "good", "Breezy", "Breezy on the bridge",
                    $"Gusts to around {g} km/h. Noticeable crossing the bridge but manageable. Hold your line on a motorcycle, and mind hats and loose items elsewhere."));
            else
                outList.Add(new("wind", "good", "Light", "Light winds",
                    $"Gusts to around {g} km/h. An easy crossing on the bridge, and calm going for anything outdoors."));
        }

        // ── Sea & tide ──
        var mh = marine?["hourly"];
        if (mh is not null)
        {
            var hi = Fmt.GetCurrentHourIndex(Js.Strs(mh["time"]));
            var tide = Sections.GetTideSeries(marine)!;
            var ti = tide.IsReal ? Fmt.NearestTimeIndex(tide.Time) : hi;
            var tides = HmlEnvDb.Tides.Detect(tide.Time, tide.Heights);
            var nextHigh = tides.FirstOrDefault(t => t.Type == "high" && t.Index > ti);
            var nextLow = tides.FirstOrDefault(t => t.Type == "low" && t.Index > ti);
            var tideBits = new List<string>();
            if (nextHigh is not null) tideBits.Add($"next high tide {Fmt.FormatTime(nextHigh.Time)}");
            if (nextLow is not null) tideBits.Add($"next low tide {Fmt.FormatTime(nextLow.Time)}");
            var tideStr = tideBits.Count > 0 ? $" ({string.Join(", ", tideBits)})" : "";

            var waveBit = c.WaveMax is not null ? $"Waves to ~{Fmt.FormatNumber(c.WaveMax)} m" : "Seas";
            var windDriven = (c.GustLevel ?? 0) > (c.WaveLevel ?? 0);
            var swellDriven = (c.WaveLevel ?? 0) > (c.GustLevel ?? 0);
            var ww = c.WindWaveMax; var sw = c.SwellMax;
            var swellRules = ww is not null && sw is not null && sw > ww * 1.3;
            var lowSwell = sw is not null && sw < 1.0;
            var whySwell = swellDriven && swellRules
                ? $" That is mostly swell rolling in (~{Fmt.FormatNumber(sw)} m) rather than local wind." : "";
            var why = windDriven
                ? $"{(lowSwell ? " The swell itself is low, but" : " On top of that,")} gusts to ~{Math.Round(c.GustPeak)} km/h will chop up open water."
                : whySwell;

            if (c.SeaLevel >= 3) outList.Add(new("wave", "alert", "Rough", "Rough seas",
                $"{waveBit} with gusts to ~{Math.Round(c.GustPeak)} km/h. Small boats should stay in, and swimming or reef walking is not advisable{tideStr}.{whySwell}"));
            else if (c.SeaLevel == 2) outList.Add(new("wave", "watch", "Choppy", "Choppy seas",
                $"{waveBit} expected. Caution for small boats, swimmers and reef walkers{tideStr}.{why}"));
            else if (c.SeaLevel == 1) outList.Add(new("wave", "good", "Slight", "Slight sea",
                $"{waveBit}, so a little movement on the water but nothing difficult{tideStr}.{why}"));
            else outList.Add(new("wave", "good", "Calm", "Calm seas",
                $"Light wind and {(c.WaveMax is not null ? $"waves under ~{Fmt.FormatNumber(c.WaveMax)} m" : "low seas")}{tideStr}. Good for time on or by the water."));
        }

        // ── Swimming in the channel (best around high tide) ──
        if (mh is not null)
        {
            var tide = Sections.GetTideSeries(marine)!;
            var ti = tide.IsReal ? Fmt.NearestTimeIndex(tide.Time) : Fmt.GetCurrentHourIndex(Js.Strs(mh["time"]));
            var tides = HmlEnvDb.Tides.Detect(tide.Time, tide.Heights);
            var nextHigh = tides.FirstOrDefault(t => t.Type == "high" && t.Index > ti);
            var highBit = nextHigh is not null
                ? $"The next high tide is around {Fmt.FormatTime(nextHigh.Time)}, when the water is deepest and most comfortable."
                : "High tide gives the deepest, most comfortable water, so check the tide chart below.";
            var strongCurrent = (c.CurrentMax ?? 0) >= 0.6;
            var currentBit = strongCurrent ? $" The current is running to ~{Fmt.FormatNumber(c.CurrentMax)} m/s." : "";

            if (c.SeaLevel >= 3)
                outList.Add(new("ocean", "alert", "Not Today", "Give the channel a miss",
                    $"Rough water and gusts to ~{Math.Round(c.GustPeak)} km/h make the channel a bad idea today, whatever the tide is doing.{currentBit} Wait for the sea to settle."));
            else if (c.SeaLevel == 2 || strongCurrent)
            {
                var choppy = c.SeaLevel == 2;
                outList.Add(new("ocean", "watch", "Take Care",
                    choppy ? "Choppy in the channel" : "Strong current in the channel",
                    $"{(choppy ? "The channel will be choppy today." : $"The sea itself is reasonably settled, but the current is running to ~{Fmt.FormatNumber(c.CurrentMax)} m/s.")} If you do go in, stay close to shore, keep someone with you, and don't swim against the current. {highBit}"));
            }
            else
                outList.Add(new("ocean", "info", "Tide Alert", "Swimming in the channel",
                    $"Fancy a dip in the channel? {highBit} Conditions look settled, but always mind the current."));
        }

        // ── Sun & moon (SunCalc port — computed locally, no API needed) ──
        {
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // Sunrise / sunset — prefer Open-Meteo's values (already fetched).
            var sunrise = Js.StrAt(weather?["daily"]?["sunrise"], 0);
            var sunset = Js.StrAt(weather?["daily"]?["sunset"], 0);
            string sunriseStr, sunsetStr;
            if (sunrise is not null && sunset is not null)
            {
                sunriseStr = Fmt.FormatTime(sunrise);
                sunsetStr = Fmt.FormatTime(sunset);
            }
            else
            {
                var t = SunCalc.GetTimes(nowMs, Fmt.LAT, Fmt.LON);
                sunriseStr = Fmt.FormatTime(ToMvt(t.SunriseMs));
                sunsetStr = Fmt.FormatTime(ToMvt(t.SunsetMs));
            }
            outList.Add(new("sun", "info", "Sun Cycle", "Sunrise & sunset",
                $"For the sunrise and sunset lovers, sunrise is at {sunriseStr} and sunset is at {sunsetStr}."));

            // Moon rise/set + the compass direction to look toward at each.
            var mt = SunCalc.GetMoonTimes(nowMs, Fmt.LAT, Fmt.LON);
            string DirAt(double ms) => Fmt.GetWindDirection(AzToBearing(SunCalc.GetMoonPosition(ms, Fmt.LAT, Fmt.LON).Azimuth));
            var riseStr = mt.RiseMs is double rms ? $"rises at {Fmt.FormatTime(ToMvt(rms))} in the {DirAt(rms)}" : null;
            var setStr = mt.SetMs is double sms ? $"sets at {Fmt.FormatTime(ToMvt(sms))} in the {DirAt(sms)}" : null;
            var phase = MoonPhaseName(SunCalc.GetMoonIllumination(nowMs).Phase);
            var moonMsg =
                riseStr is not null && setStr is not null ? $"The moon {riseStr}, and {setStr}. Tonight it's a {phase}."
                : riseStr is not null ? $"The moon {riseStr} and stays up through the night. Tonight it's a {phase}."
                : setStr is not null ? $"The moon is already up and {setStr}. Tonight it's a {phase}."
                : $"The moon stays above the horizon all day today. Tonight it's a {phase}.";
            outList.Add(new("moon", "info", "Lunar Phase", "Moonrise & moonset", moonMsg));
        }

        return outList;
    }

    public static DateTime ToMvt(double epochMs)
        => DateTimeOffset.FromUnixTimeMilliseconds((long)epochMs).UtcDateTime.AddHours(5);

    // SunCalc azimuth is measured from south, clockwise toward west (radians).
    public static double AzToBearing(double azRad) => (azRad * 180 / Math.PI + 180 + 360) % 360;

    public static string MoonPhaseName(double p)
    {
        if (p < 0.03 || p > 0.97) return "new moon";
        if (p < 0.22) return "waxing crescent";
        if (p < 0.28) return "first-quarter moon";
        if (p < 0.47) return "waxing gibbous";
        if (p < 0.53) return "full moon";
        if (p < 0.72) return "waning gibbous";
        if (p < 0.78) return "last-quarter moon";
        return "waning crescent";
    }

    // Map an advisory's icon to its day-outlook card slot.
    private static readonly Dictionary<string, string> CardKey = new()
    {
        ["aqi"] = "aqi", ["warning"] = "aqi", ["uv"] = "uv", ["temperature"] = "uv", ["rain"] = "rain",
        ["wind"] = "wind", ["wave"] = "sea", ["ocean"] = "swim", ["sun"] = "sun", ["moon"] = "moon",
    };

    public static async Task UpdateBriefing(JsonNode? weather, JsonNode? aq, JsonNode? marine)
    {
        var items = BuildAdvisories(weather, aq, marine);
        var now = Fmt.NowMaldives();

        if (items.Count == 0)
        {
            await Dom.SetText("briefing-headline", "Conditions unavailable");
            await Dom.SetText("briefing-sub", "Live data could not be loaded. Try refreshing.");
            return;
        }

        var worst = items.Max(a => ToneRank.GetValueOrDefault(a.Tone, 0));
        var headline = worst switch
        {
            2 => "Take some precautions today",
            1 => "A good day out, with a few things to plan for",
            _ => "A good day to be outside",
        };
        await Dom.SetText("briefing-headline", headline);
        await Dom.SetText("briefing-sub", $"Hulhumalé · {Fmt.LongDate(now)}. Here's what to expect and how to plan.");

        // First advisory per card slot supplies that card's title / message / badge.
        var byKey = new Dictionary<string, Advisory>();
        foreach (var a in items)
            if (CardKey.TryGetValue(a.Icon, out var k) && !byKey.ContainsKey(k)) byKey[k] = a;

        static string BadgeClass(Advisory a, string key) =>
            key == "moon" ? "badge--lunar" :
            key == "sun" ? "badge--info" :
            a.Tone == "good" ? "badge--good" :
            a.Tone == "info" ? "badge--info" : "badge--warn";

        async Task Fill(string id, string key)
        {
            if (byKey.TryGetValue(key, out var a))
                await Dom.FillOutlookCard(id, a.Title, a.Msg, a.Tag, "badge " + BadgeClass(a, key), true);
            else
                await Dom.FillOutlookCard(id, "", "", "", "", false);
        }
        await Fill("oc-aqi", "aqi"); await Fill("oc-uv", "uv"); await Fill("oc-rain", "rain");
        await Fill("oc-wind", "wind"); await Fill("oc-sea", "sea");
        await Fill("oc-swim", "swim"); await Fill("oc-sun", "sun"); await Fill("oc-moon", "moon");

        await Dom.RenderHeroViz(Js.Ser(BuildHeroData(weather, aq, marine)));
    }

    private static double Clamp01(double v) => Math.Max(0, Math.Min(1, v));

    // Turn live weather / AQ / marine + SunCalc into the plain data the hero
    // visualisations need; the drawing itself stays in the interop layer.
    public static object BuildHeroData(JsonNode? weather, JsonNode? aq, JsonNode? marine)
    {
        var nowD = Fmt.NowMaldives();
        var d = new Dictionary<string, object?> { ["nowMinutes"] = nowD.Hour * 60 + nowD.Minute };

        var aqh = aq?["hourly"];
        if (aqh is not null)
        {
            var hi = Fmt.GetCurrentHourIndex(Js.Strs(aqh["time"]));
            var v = Js.NumAt(aqh["us_aqi"], hi) ?? Sections.ComputeAqiFromPollutants(aq, hi);
            d["aqi"] = v is double dv ? (int)Math.Round(dv) : null;
        }

        var wh = weather?["hourly"];
        if (wh?["uv_index"] is not null)
        {
            var times = Js.Strs(wh["time"]);
            var uvArr = Js.Nums(wh["uv_index"]);
            var today = Fmt.GetTodayDateStr();
            var uv = new List<double>();
            for (var h = 6; h <= 17; h++)
            {
                var idx = times.IndexOf($"{today}T{h:00}:00");
                uv.Add(idx >= 0 ? uvArr[idx] ?? 0 : 0);
            }
            d["uvByHour"] = uv;
        }

        if (wh?["precipitation_probability"] is not null)
        {
            var hi = Fmt.GetCurrentHourIndex(Js.Strs(wh["time"]));
            var prob = Js.Nums(wh["precipitation_probability"]);
            var r = new List<double>();
            for (var i = 0; i < 6; i++) r.Add((prob.ElementAtOrDefault(hi + i) ?? 0) / 100);
            d["rainByHour"] = r;
        }

        if (wh?["windspeed_10m"] is not null)
        {
            var hi = Fmt.GetCurrentHourIndex(Js.Strs(wh["time"]));
            var hasGusts = wh["windgusts_10m"] is not null;
            var gusts = hasGusts ? Js.Nums(wh["windgusts_10m"]) : Js.Nums(wh["windspeed_10m"]);
            var g = new List<double>();
            for (var i = 0; i < 6; i++) g.Add(gusts.ElementAtOrDefault(hi + i) ?? 0);
            d["gustByHour"] = g;
            var dir = Js.Num(weather?["current"]?["winddirection_10m"]) ?? Js.NumAt(wh["winddirection_10m"], hi);
            var nowGust = gusts.ElementAtOrDefault(hi) ?? Js.Num(weather?["current"]?["windgusts_10m"]);
            d["windMeta"] = $"{(hasGusts ? "GUSTS" : "WIND")} {(nowGust is double ng ? Math.Round(ng).ToString("0") : "—")} KM/H"
                + (dir is not null ? $" · {Fmt.GetWindDirection(dir)}" : "");
        }

        var mh = marine?["hourly"];
        if (mh?["wave_height"] is not null)
        {
            var hi = Fmt.GetCurrentHourIndex(Js.Strs(mh["time"]));
            var wave = Js.Nums(mh["wave_height"]);
            int[] offs = { 0, 3, 6, 9 };
            string[] labels = { "NOW", "+3H", "+6H", "+9H" };
            double[] ops = { .9, .7, .55, .4 };
            d["swell"] = offs.Select((o, i) =>
            {
                var w = wave.ElementAtOrDefault(hi + o) ?? 0;
                var px = Math.Max(4, Math.Min(26, w / 1.5 * 26));
                return new object[] { labels[i], Math.Round(px), ops[i] };
            }).ToList();
            var sw = wave.ElementAtOrDefault(hi);
            var wk = Js.Num(weather?["current"]?["windspeed_10m"]);
            d["seaMeta"] = $"SWELL {(sw is double s ? s.ToString("F1") : "—")} M · WIND {(wk is double k ? Math.Round(k * 0.539957).ToString("0") : "—")} KT";
        }

        var tide = Sections.GetTideSeries(marine);
        if (tide is not null && tide.Heights.Count > 0 && mh is not null)
        {
            var ti = tide.IsReal ? Fmt.NearestTimeIndex(tide.Time) : Fmt.GetCurrentHourIndex(Js.Strs(mh["time"]));
            var tides = HmlEnvDb.Tides.Detect(tide.Time, tide.Heights);
            var prev = tides.LastOrDefault(t => t.Index <= ti);
            var next = tides.FirstOrDefault(t => t.Index > ti);
            if (prev is not null && next is not null && next.Index > prev.Index)
            {
                d["tideProgress"] = Clamp01((double)(ti - prev.Index) / (next.Index - prev.Index));
                d["tideLeft"] = $"{prev.Type.ToUpper()} {Fmt.FormatHourLabel(prev.Time)}";
                d["tideRight"] = $"{next.Type.ToUpper()} {Fmt.FormatHourLabel(next.Time)}";
            }
            else if (next is not null)
            {
                d["tideProgress"] = 0.15;
                d["tideLeft"] = "";
                d["tideRight"] = $"{next.Type.ToUpper()} {Fmt.FormatHourLabel(next.Time)}";
            }
        }

        // Sun arc + moon arc/phase.
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        static int ToMin(string iso) => int.Parse(iso.Substring(11, 2)) * 60 + int.Parse(iso.Substring(14, 2));
        static int MvtMin(double ms) { var t = ToMvt(ms); return t.Hour * 60 + t.Minute; }

        var sunriseIso = Js.StrAt(weather?["daily"]?["sunrise"], 0);
        var sunsetIso = Js.StrAt(weather?["daily"]?["sunset"], 0);
        int? sunRise = null, sunSet = null;
        if (sunriseIso is not null && sunsetIso is not null) { sunRise = ToMin(sunriseIso); sunSet = ToMin(sunsetIso); }

        var st = SunCalc.GetTimes(nowMs, Fmt.LAT, Fmt.LON);
        sunRise ??= MvtMin(st.SunriseMs);
        sunSet ??= MvtMin(st.SunsetMs);
        d["sun"] = new { rise = sunRise, set = sunSet };
        d["sunriseVal"] = Fmt.Min2Str(sunRise.Value);
        d["sunsetVal"] = Fmt.Min2Str(sunSet.Value);
        string SunDirAt(double ms) => Fmt.GetWindDirection(AzToBearing(SunCalc.GetSunAzimuth(ms, Fmt.LAT, Fmt.LON)));
        string MoonDirAt(double ms) => Fmt.GetWindDirection(AzToBearing(SunCalc.GetMoonPosition(ms, Fmt.LAT, Fmt.LON).Azimuth));
        d["sunriseDir"] = "SUNRISE · " + SunDirAt(st.SunriseMs);
        d["sunsetDir"] = "SUNSET · " + SunDirAt(st.SunsetMs);

        var mt = SunCalc.GetMoonTimes(nowMs, Fmt.LAT, Fmt.LON);
        var il = SunCalc.GetMoonIllumination(nowMs);
        d["moon"] = new
        {
            rise = mt.RiseMs is double r2 ? (int?)MvtMin(r2) : null,
            set = mt.SetMs is double s2 ? (int?)MvtMin(s2) : null,
            phase = MoonPhaseName(il.Phase),
            illumination = il.Fraction,
            waxing = il.Phase < 0.5,
        };
        d["moonriseVal"] = mt.RiseMs is double r3 ? Fmt.Min2Str(MvtMin(r3)) : "—";
        d["moonsetVal"] = mt.SetMs is double s3 ? Fmt.Min2Str(MvtMin(s3)) : "—";
        d["moonriseDir"] = "MOONRISE" + (mt.RiseMs is double r4 ? " · " + MoonDirAt(r4) : "");
        d["moonsetDir"] = "MOONSET" + (mt.SetMs is double s4 ? " · " + MoonDirAt(s4) : "");

        return d;
    }
}
