namespace DriversUpdator.Core.Models;

/// <summary>Version de pilote au format a.b.c.d, comparable.</summary>
public sealed record DriverVersion(int Major, int Minor, int Build, int Revision) : IComparable<DriverVersion>
{
    public static DriverVersion? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Trim().Split('.', StringSplitOptions.TrimEntries);
        var n = new int[4];
        for (int i = 0; i < Math.Min(4, parts.Length); i++)
        {
            if (!int.TryParse(parts[i], out n[i])) return null;
        }
        return new DriverVersion(n[0], n[1], n[2], n[3]);
    }

    public int CompareTo(DriverVersion? other)
    {
        if (other is null) return 1;
        int c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Build.CompareTo(other.Build);
        return c != 0 ? c : Revision.CompareTo(other.Revision);
    }

    public static bool operator >(DriverVersion a, DriverVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(DriverVersion a, DriverVersion b) => a.CompareTo(b) < 0;

    public override string ToString() => $"{Major}.{Minor}.{Build}.{Revision}";
}
