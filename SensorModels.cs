namespace SmartX.Api.Models;

public enum SensorCategory
{
    Environmental,
    PowerConsumption,
    Actuator
}

/// <summary>A registered sensor / device on the Smart-X mesh.</summary>
public class SensorProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Device MAC address / unique identifier, e.g. "AA:BB:CC:11:22:33".</summary>
    public string MacAddress { get; set; } = string.Empty;

    /// <summary>Human-readable deployment location, e.g. "Sub-Zone B -> Zone 1 -> Facility A".</summary>
    public string DeploymentLocation { get; set; } = string.Empty;

    public SensorCategory Category { get; set; }

    public DateTime RegisteredUtc { get; init; } = DateTime.UtcNow;

    /// <summary>File names of attached configuration files / photos / logs.</summary>
    public List<string> AttachedMedia { get; init; } = new();

    /// <summary>Rolling engagement score - see HealthScoreService.</summary>
    public int HealthScore { get; set; } = 100;

    public int Streak { get; set; }

    public string Badge { get; set; } = "New Deployment";
}

// =====================================================================
// ASSIGNMENT REQUIREMENT: OPERATOR OVERLOADING
// SensorReading below overloads +, -, >, <, >=, <=, ==, != so sensor
// values can be aggregated/compared directly, e.g. Meter = Meter1 + Meter2.
// =====================================================================

/// <summary>
/// A lightweight, comparable measurement used for aggregation and delta checks
/// (e.g. combined load of two smart meters, or the drift between two readings).
/// Implemented as a struct so aggregating thousands of readings per second on a
/// resource-constrained gateway avoids per-reading heap allocation.
/// </summary>
public readonly struct SensorReading : IEquatable<SensorReading>
{
    public string DeviceId { get; }
    public double Magnitude { get; }
    public DateTime TimestampUtc { get; }

    public SensorReading(string deviceId, double magnitude, DateTime? timestampUtc = null)
    {
        DeviceId = deviceId;
        Magnitude = magnitude;
        TimestampUtc = timestampUtc ?? DateTime.UtcNow;
    }

    /// <summary>Aggregate load of two readings, e.g. Meter = Meter1 + Meter2.</summary>
    public static SensorReading operator +(SensorReading a, SensorReading b) =>
        new($"{a.DeviceId}+{b.DeviceId}", a.Magnitude + b.Magnitude);

    /// <summary>Delta between two readings, useful for drift / spike detection.</summary>
    public static SensorReading operator -(SensorReading a, SensorReading b) =>
        new($"{a.DeviceId}-{b.DeviceId}", a.Magnitude - b.Magnitude);

    public static bool operator >(SensorReading a, SensorReading b) => a.Magnitude > b.Magnitude;
    public static bool operator <(SensorReading a, SensorReading b) => a.Magnitude < b.Magnitude;
    public static bool operator >=(SensorReading a, SensorReading b) => a.Magnitude >= b.Magnitude;
    public static bool operator <=(SensorReading a, SensorReading b) => a.Magnitude <= b.Magnitude;

    public static bool operator ==(SensorReading a, SensorReading b) =>
        Math.Abs(a.Magnitude - b.Magnitude) < 0.0001 && a.DeviceId == b.DeviceId;

    public static bool operator !=(SensorReading a, SensorReading b) => !(a == b);

    public bool Equals(SensorReading other) => this == other;
    public override bool Equals(object? obj) => obj is SensorReading r && Equals(r);
    public override int GetHashCode() => HashCode.Combine(DeviceId, Magnitude);
    public override string ToString() => $"{DeviceId}: {Magnitude:F2}";
}
