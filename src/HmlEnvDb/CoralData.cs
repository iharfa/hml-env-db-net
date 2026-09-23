namespace HmlEnvDb;

// NOAA CRW virtual-station text parsing. The feed is CORS-blocked in-browser,
// so in practice the parse only runs if NOAA ever starts sending CORS headers —
// until then the UI shows the live gauge image plus a link fallback.

public class CoralResult
{
    public string Status = "cors-blocked";   // "ok" | "cors-blocked"
    public string? Raw;
}

public class CoralRecord
{
    public int Year, Doy;
    public double Sst, Ssta, Dhw;
    public int Alert;
}

public static class CoralData
{
    public static List<CoralRecord> ParseCoralText(string text)
    {
        var records = new List<CoralRecord>();
        foreach (var line in text.Split('\n'))
        {
            var t = line.Trim();
            if (t.Length == 0 || t.StartsWith("#")) continue;
            var parts = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5) continue;
            // Typical columns: Year DOY SST SSTA DHW Alert_Level (or similar)
            if (parts[0].Length != 4 || !int.TryParse(parts[0], out var year)) continue;
            if (!int.TryParse(parts[1], out var doy)) continue;
            records.Add(new CoralRecord
            {
                Year = year,
                Doy = doy,
                Sst = Parse(parts[2]),
                Ssta = Parse(parts[3]),
                Dhw = Parse(parts[4]),
                Alert = parts.Length > 5 && int.TryParse(parts[5], out var a) ? a : 0,
            });
        }
        return records;
    }

    private static double Parse(string s) => double.TryParse(s, out var v) ? v : double.NaN;

    public static string AlertLabel(int level) => level switch
    {
        0 => "No Stress", 1 => "Bleaching Watch", 2 => "Bleaching Warning",
        3 => "Alert Level 1", 4 => "Alert Level 2", 5 => "Alert Level 3",
        6 => "Alert Level 4", 7 => "Alert Level 5",
        _ => $"Alert Level {level}",
    };

    public static string AlertClass(int level) => level switch
    {
        0 => "alert-no-stress",
        1 => "alert-watch",
        2 => "alert-warning",
        >= 3 => "alert-level-1",
        _ => "",
    };

    private static readonly string[] AlertColors =
        { "#16a34a", "#ca8a04", "#ea580c", "#dc2626", "#7c3aed", "#7e0023", "#4a0010", "#1a0005" };

    public static string AlertColor(int level) => AlertColors[Math.Min(Math.Max(level, 0), AlertColors.Length - 1)];
}
