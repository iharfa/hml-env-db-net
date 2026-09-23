namespace HmlEnvDb;

// C# port of the parts of SunCalc the dashboard uses (sun times/position, moon
// rise/set/position/illumination). Original: (c) Vladimir Agafonkin, MIT,
// https://github.com/mourner/suncalc — formulas from Astronomical Algorithms
// (Meeus) via aa.quae.nl. Dates are Unix epoch milliseconds throughout.
public static class SunCalc
{
    private const double DayMs = 86400000;
    private const double J1970 = 2440588, J2000 = 2451545;
    private const double Rad = Math.PI / 180;
    private const double E = Rad * 23.4397; // obliquity of the Earth

    private static double ToJulian(double ms) => ms / DayMs - 0.5 + J1970;
    private static double FromJulian(double j) => (j + 0.5 - J1970) * DayMs;
    private static double ToDays(double ms) => ToJulian(ms) - J2000;

    private static double RightAscension(double l, double b)
        => Math.Atan2(Math.Sin(l) * Math.Cos(E) - Math.Tan(b) * Math.Sin(E), Math.Cos(l));
    private static double Declination(double l, double b)
        => Math.Asin(Math.Sin(b) * Math.Cos(E) + Math.Cos(b) * Math.Sin(E) * Math.Sin(l));
    private static double Azimuth(double h, double phi, double dec)
        => Math.Atan2(Math.Sin(h), Math.Cos(h) * Math.Sin(phi) - Math.Tan(dec) * Math.Cos(phi));
    private static double Altitude(double h, double phi, double dec)
        => Math.Asin(Math.Sin(phi) * Math.Sin(dec) + Math.Cos(phi) * Math.Cos(dec) * Math.Cos(h));
    private static double SiderealTime(double d, double lw) => Rad * (280.16 + 360.9856235 * d) - lw;

    private static double AstroRefraction(double h)
    {
        if (h < 0) h = 0;
        return 0.0002967 / Math.Tan(h + 0.00312536 / (h + 0.08901179));
    }

    private static double SolarMeanAnomaly(double d) => Rad * (357.5291 + 0.98560028 * d);

    private static double EclipticLongitude(double m)
    {
        var c = Rad * (1.9148 * Math.Sin(m) + 0.02 * Math.Sin(2 * m) + 0.0003 * Math.Sin(3 * m));
        var p = Rad * 102.9372;
        return m + c + p + Math.PI;
    }

    private static (double Dec, double Ra) SunCoords(double d)
    {
        var l = EclipticLongitude(SolarMeanAnomaly(d));
        return (Declination(l, 0), RightAscension(l, 0));
    }

    public static double GetSunAzimuth(double ms, double lat, double lng)
    {
        var lw = Rad * -lng; var phi = Rad * lat; var d = ToDays(ms);
        var c = SunCoords(d);
        var h = SiderealTime(d, lw) - c.Ra;
        return Azimuth(h, phi, c.Dec);
    }

    // Sunrise/sunset (the only pair of the SunCalc "times" table this app reads).
    private const double J0 = 0.0009;
    private static double JulianCycle(double d, double lw) => Math.Round(d - J0 - lw / (2 * Math.PI));
    private static double ApproxTransit(double ht, double lw, double n) => J0 + (ht + lw) / (2 * Math.PI) + n;
    private static double SolarTransitJ(double ds, double m, double l) => J2000 + ds + 0.0053 * Math.Sin(m) - 0.0069 * Math.Sin(2 * l);
    private static double HourAngle(double h, double phi, double d) => Math.Acos((Math.Sin(h) - Math.Sin(phi) * Math.Sin(d)) / (Math.Cos(phi) * Math.Cos(d)));

    public static (double SunriseMs, double SunsetMs) GetTimes(double ms, double lat, double lng)
    {
        var lw = Rad * -lng; var phi = Rad * lat;
        var d = ToDays(ms);
        var n = JulianCycle(d, lw);
        var ds = ApproxTransit(0, lw, n);
        var m = SolarMeanAnomaly(ds);
        var l = EclipticLongitude(m);
        var dec = Declination(l, 0);
        var jnoon = SolarTransitJ(ds, m, l);

        var h0 = -0.833 * Rad;
        var w = HourAngle(h0, phi, dec);
        var a = ApproxTransit(w, lw, n);
        var jset = SolarTransitJ(a, m, l);
        var jrise = jnoon - (jset - jnoon);
        return (FromJulian(jrise), FromJulian(jset));
    }

    private static (double Ra, double Dec, double Dist) MoonCoords(double d)
    {
        var l = Rad * (218.316 + 13.176396 * d);
        var m = Rad * (134.963 + 13.064993 * d);
        var f = Rad * (93.272 + 13.229350 * d);
        var lng = l + Rad * 6.289 * Math.Sin(m);
        var lat = Rad * 5.128 * Math.Sin(f);
        var dt = 385001 - 20905 * Math.Cos(m);
        return (RightAscension(lng, lat), Declination(lng, lat), dt);
    }

    public static (double Azimuth, double Altitude) GetMoonPosition(double ms, double lat, double lng)
    {
        var lw = Rad * -lng; var phi = Rad * lat; var d = ToDays(ms);
        var c = MoonCoords(d);
        var h = SiderealTime(d, lw) - c.Ra;
        var alt = Altitude(h, phi, c.Dec);
        alt += AstroRefraction(alt);
        return (Azimuth(h, phi, c.Dec), alt);
    }

    public static (double Fraction, double Phase) GetMoonIllumination(double ms)
    {
        var d = ToDays(ms);
        var s = SunCoords(d);
        var m = MoonCoords(d);
        const double sdist = 149598000;
        var phi = Math.Acos(Math.Sin(s.Dec) * Math.Sin(m.Dec) + Math.Cos(s.Dec) * Math.Cos(m.Dec) * Math.Cos(s.Ra - m.Ra));
        var inc = Math.Atan2(sdist * Math.Sin(phi), m.Dist - sdist * Math.Cos(phi));
        var angle = Math.Atan2(Math.Cos(s.Dec) * Math.Sin(s.Ra - m.Ra),
            Math.Sin(s.Dec) * Math.Cos(m.Dec) - Math.Cos(s.Dec) * Math.Sin(m.Dec) * Math.Cos(s.Ra - m.Ra));
        return ((1 + Math.Cos(inc)) / 2, 0.5 + 0.5 * inc * (angle < 0 ? -1 : 1) / Math.PI);
    }

    // Moon rise/set for the local (MVT) day containing `ms` — quadratic
    // interpolation over 2-hour chunks, per stargazing.net/kepler/moonrise.html.
    public static (double? RiseMs, double? SetMs, bool AlwaysUp) GetMoonTimes(double ms, double lat, double lng)
    {
        // Midnight of the MVT calendar day.
        var mvt = DateTimeOffset.FromUnixTimeMilliseconds((long)ms).ToOffset(TimeSpan.FromHours(5));
        var t = new DateTimeOffset(mvt.Year, mvt.Month, mvt.Day, 0, 0, 0, TimeSpan.FromHours(5)).ToUnixTimeMilliseconds();

        var hc = 0.133 * Rad;
        var h0 = GetMoonPosition(t, lat, lng).Altitude - hc;
        double? rise = null, set = null;
        double ye = 0;

        for (var i = 1; i <= 24; i += 2)
        {
            var h1 = GetMoonPosition(t + i * DayMs / 24, lat, lng).Altitude - hc;
            var h2 = GetMoonPosition(t + (i + 1) * DayMs / 24, lat, lng).Altitude - hc;

            var a = (h0 + h2) / 2 - h1;
            var b = (h2 - h0) / 2;
            var xe = -b / (2 * a);
            ye = (a * xe + b) * xe + h1;
            var disc = b * b - 4 * a * h1;
            var roots = 0;
            double x1 = 0, x2 = 0;

            if (disc >= 0)
            {
                var dx = Math.Sqrt(disc) / (Math.Abs(a) * 2);
                x1 = xe - dx;
                x2 = xe + dx;
                if (Math.Abs(x1) <= 1) roots++;
                if (Math.Abs(x2) <= 1) roots++;
                if (x1 < -1) x1 = x2;
            }

            if (roots == 1)
            {
                if (h0 < 0) rise = i + x1;
                else set = i + x1;
            }
            else if (roots == 2)
            {
                rise = i + (ye < 0 ? x2 : x1);
                set = i + (ye < 0 ? x1 : x2);
            }

            if (rise.HasValue && set.HasValue) break;
            h0 = h2;
        }

        return (
            rise.HasValue ? t + rise.Value * DayMs / 24 : null,
            set.HasValue ? t + set.Value * DayMs / 24 : null,
            !rise.HasValue && !set.HasValue && ye > 0);
    }
}
