namespace HmlEnvDb;

// Location constants + all label formatting. Formatting is hand-rolled against
// fixed English month/day names so output matches the original app.js exactly
// (and InvariantGlobalization can stay on).
public static class Fmt
{
    public const double LAT = 4.2105;
    public const double LON = 73.5446;

    private static readonly string[] MonthsShort = { "Jan","Feb","Mar","Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec" };
    private static readonly string[] MonthsLong  = { "January","February","March","April","May","June","July","August","September","October","November","December" };
    private static readonly string[] DaysShort   = { "Sun","Mon","Tue","Wed","Thu","Fri","Sat" };
    private static readonly string[] DaysLong    = { "Sunday","Monday","Tuesday","Wednesday","Thursday","Friday","Saturday" };

    // Maldives is a fixed UTC+5 with no DST, so "local time" is plain offset math.
    public static DateTime NowMaldives() => DateTime.UtcNow.AddHours(5);

    public static string FormatNumber(double? v, int digits = 1)
        => v is double d && !double.IsNaN(d) ? d.ToString("F" + digits) : "—";

    // "02:15 PM" — from a local wall-clock ISO string ("YYYY-MM-DDTHH:MM...").
    public static string FormatTime(string? iso)
    {
        if (string.IsNullOrEmpty(iso) || iso.Length < 16) return "—";
        if (!int.TryParse(iso.AsSpan(11, 2), out var h) || !int.TryParse(iso.AsSpan(14, 2), out var m)) return iso;
        return FormatTime(h, m);
    }

    public static string FormatTime(DateTime local) => FormatTime(local.Hour, local.Minute);

    private static string FormatTime(int h, int m)
    {
        var ap = h < 12 ? "AM" : "PM";
        var hr = h % 12 == 0 ? 12 : h % 12;
        return $"{hr:00}:{m:00} {ap}";
    }

    // "Thu, Jul 3" — from a date or datetime ISO string.
    public static string FormatDate(string? iso)
    {
        if (string.IsNullOrEmpty(iso) || iso.Length < 10) return "—";
        if (!DateTime.TryParse(iso[..10], out var d)) return iso;
        return $"{DaysShort[(int)d.DayOfWeek]}, {MonthsShort[d.Month - 1]} {d.Day}";
    }

    // "2 PM" — hour label parsed straight off the wall-clock string.
    public static string FormatHourLabel(string? iso)
    {
        if (string.IsNullOrEmpty(iso) || iso.Length < 13) return "";
        if (!int.TryParse(iso.AsSpan(11, 2), out var h)) return "";
        var ampm = h < 12 ? "AM" : "PM";
        var hr = h % 12 == 0 ? 12 : h % 12;
        return $"{hr} {ampm}";
    }

    public static string FormatDayLabel(string? iso) => string.IsNullOrEmpty(iso) ? "—" : FormatDate(iso[..Math.Min(10, iso.Length)]);

    public static string FormatDateTimeLabel(string? iso) => $"{FormatDayLabel(iso)} · {FormatTime(iso)}";

    // "3 Jul 2026" (en-GB style, as the coral cards always showed).
    public static string FormatCoralDate(string iso, bool noYear = false)
    {
        var p = iso.Split('-');
        if (p.Length < 3 || !int.TryParse(p[0], out var y) || !int.TryParse(p[1], out var m) || !int.TryParse(p[2], out var d))
            return iso;
        return noYear ? $"{d} {MonthsShort[m - 1]}" : $"{d} {MonthsShort[m - 1]} {y}";
    }

    // Day-of-year → "3 Jul" (short: month only), on a non-leap reference year.
    public static string DoyToLabel(int doy, bool shortLabel = false)
    {
        var dt = new DateTime(2001, 1, 1).AddDays(doy - 1);
        return shortLabel ? MonthsShort[dt.Month - 1] : $"{dt.Day} {MonthsShort[dt.Month - 1]}";
    }

    public static string LongDate(DateTime d) => $"{DaysLong[(int)d.DayOfWeek]}, {MonthsLong[d.Month - 1]} {d.Day}";
    public static string WeekdayShort(DateTime d) => DaysShort[(int)d.DayOfWeek];

    public static string GetTodayDateStr()
    {
        var now = NowMaldives();
        return $"{now.Year:0000}-{now.Month:00}-{now.Day:00}";
    }

    public static int GetCurrentHourIndex(List<string> times)
    {
        var now = NowMaldives();
        var target = $"{now.Year:0000}-{now.Month:00}-{now.Day:00}T{now.Hour:00}:00";
        var idx = times.IndexOf(target);
        return idx >= 0 ? idx : 0;
    }

    // Index of the sample at/just before now, by wall-clock string comparison.
    public static int NearestTimeIndex(List<string> times)
    {
        if (times.Count == 0) return 0;
        var now = NowMaldives();
        var target = $"{now.Year:0000}-{now.Month:00}-{now.Day:00}T{now.Hour:00}:{now.Minute:00}";
        var idx = 0;
        for (var i = 0; i < times.Count; i++)
        {
            var t = times[i];
            if (string.CompareOrdinal(t.Length > 16 ? t[..16] : t, target) <= 0) idx = i; else break;
        }
        return idx;
    }

    private static readonly string[] WindDirs = { "N","NNE","NE","ENE","E","ESE","SE","SSE","S","SSW","SW","WSW","W","WNW","NW","NNW" };

    public static string GetWindDirection(double? deg)
        => deg is double d ? WindDirs[(int)Math.Round(d / 22.5) % 16] : "";

    private static readonly Dictionary<int, string> WeatherCodes = new()
    {
        [0]="Clear",[1]="Mainly clear",[2]="Partly cloudy",[3]="Overcast",
        [45]="Foggy",[48]="Icy fog",
        [51]="Light drizzle",[53]="Drizzle",[55]="Heavy drizzle",
        [61]="Light rain",[63]="Rain",[65]="Heavy rain",
        [71]="Light snow",[73]="Snow",[75]="Heavy snow",
        [80]="Showers",[81]="Rain showers",[82]="Heavy showers",
        [85]="Snow showers",[86]="Heavy snow showers",
        [95]="Thunderstorm",[96]="Thunderstorm + hail",[99]="Heavy thunderstorm",
    };

    public static string GetWeatherLabel(double? code)
    {
        if (code is not double c) return "Unknown";
        return WeatherCodes.TryGetValue((int)c, out var s) ? s : $"Code {(int)c}";
    }

    public static string UvLabel(double? uv) => uv switch
    {
        null => "",
        < 3  => "Low",
        < 6  => "Moderate",
        < 8  => "High",
        < 11 => "Very High",
        _    => "Extreme",
    };

    public static string EscapeHtml(string? s) => string.IsNullOrEmpty(s) ? "" : s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("\"", "&quot;").Replace("'", "&#39;");

    // "2:05 PM" from minutes-of-day (no leading zero, as the hero cards showed).
    public static string Min2Str(double minutes)
    {
        var m = (((int)Math.Round(minutes)) % 1440 + 1440) % 1440;
        var h = m / 60; var mm = m % 60; var ap = h < 12 ? "AM" : "PM";
        h %= 12; if (h == 0) h = 12;
        return $"{h}:{mm:00} {ap}";
    }
}

public record TidePoint(string Type, string Time, double Value, int Index);

public static class Tides
{
    // Local extrema over the series — same simple 3-point test as always.
    public static List<TidePoint> Detect(List<string> times, List<double?> heights)
    {
        var tides = new List<TidePoint>();
        if (heights.Count < 3) return tides;
        for (var i = 1; i < heights.Count - 1; i++)
        {
            var p = heights[i - 1]; var c = heights[i]; var n = heights[i + 1];
            if (p is null || c is null || n is null) continue;
            if (c > p && c > n) tides.Add(new("high", times[i], c.Value, i));
            else if (c < p && c < n) tides.Add(new("low", times[i], c.Value, i));
        }
        return tides;
    }
}
