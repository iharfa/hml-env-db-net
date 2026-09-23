namespace HmlEnvDb;

// The shared colour/category scales: US AQI and NOAA's Bleaching Alert Area.

public record AqiCat(string Label, string Color, string Health);

public record AqiBand(int Lo, int Hi, string Name, string Color, string Note);

public static class Aqi
{
    public static AqiCat GetUsAqiCategory(double? aqi)
    {
        if (aqi is not double v || double.IsNaN(v))
            return new("No live value", "#94a3b8", "Air quality data unavailable.");
        if (v <= 50) return new("Good", "#16a34a", "Air quality is satisfactory and poses little or no risk.");
        if (v <= 100) return new("Moderate", "#ca8a04", "Acceptable. Unusually sensitive individuals may experience symptoms.");
        if (v <= 150) return new("Unhealthy for Sensitive Groups", "#ea580c", "Members of sensitive groups may experience health effects.");
        if (v <= 200) return new("Unhealthy", "#dc2626", "Everyone may experience health effects. Sensitive groups: serious effects.");
        if (v <= 300) return new("Very Unhealthy", "#7c3aed", "Health alert: everyone may experience serious health effects.");
        return new("Hazardous", "#7e0023", "Health emergency: everyone is affected.");
    }

    // Shorter labels used by the station-AQ history block.
    public static (string Label, string Color) Category(double? v)
    {
        if (v is not double d || double.IsNaN(d)) return ("—", "#94a3b8");
        if (d <= 50) return ("Good", "#16a34a");
        if (d <= 100) return ("Moderate", "#ca8a04");
        if (d <= 150) return ("Unhealthy for Sensitive Groups", "#ea580c");
        if (d <= 200) return ("Unhealthy", "#dc2626");
        if (d <= 300) return ("Very Unhealthy", "#7c3aed");
        return ("Hazardous", "#7e0023");
    }

    public static string? CatShort(double? v) => v switch
    {
        null => null,
        <= 50 => "Good",
        <= 100 => "Moderate",
        <= 150 => "Unhealthy (SG)",
        <= 200 => "Unhealthy",
        <= 300 => "Very Unhealthy",
        _ => "Hazardous",
    };

    // US EPA PM2.5 → AQI (legacy 24h breakpoints, matches WAQI's US AQI).
    public static int? Pm25ToAqi(double? conc)
    {
        if (conc is not double c || double.IsNaN(c)) return null;
        c = Math.Round(c * 10) / 10;
        double[][] bp = {
            new[]{0.0,12.0,0,50.0}, new[]{12.1,35.4,51,100.0}, new[]{35.5,55.4,101,150.0},
            new[]{55.5,150.4,151,200.0}, new[]{150.5,250.4,201,300.0},
            new[]{250.5,350.4,301,400.0}, new[]{350.5,500.4,401,500.0},
        };
        foreach (var b in bp)
        {
            if (c <= b[1])
                return (int)Math.Round((b[3] - b[2]) / (b[1] - b[0]) * (Math.Max(c, b[0]) - b[0]) + b[2]);
        }
        return 500;
    }

    // Linear band scale for the Overall AQI readout.
    public static readonly AqiBand[] Bands =
    {
        new(0, 50, "Good", "#3FB97F", "Air quality is satisfactory. No precautions needed."),
        new(51, 100, "Moderate", "#E3C24A", "Acceptable, though unusually sensitive people may notice symptoms."),
        new(101, 150, "Sensitive Groups", "#EE8B3C", "Sensitive groups should limit prolonged outdoor exertion."),
        new(151, 200, "Unhealthy", "#E0524D", "Everyone may begin to feel health effects. Reduce outdoor time."),
        new(201, 300, "Very Unhealthy", "#9A5CCB", "Health alert. Avoid outdoor activity where possible."),
        new(301, 500, "Hazardous", "#8C3A50", "Emergency conditions. Stay indoors with filtered air."),
    };

    public static AqiBand BandFor(double v)
    {
        v = Math.Max(0, Math.Min(500, v));
        foreach (var b in Bands) if (v <= b.Hi) return b;
        return Bands[^1];
    }
}

