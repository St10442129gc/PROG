namespace SmartX.Api.Models;

// =====================================================================
// ASSIGNMENT REQUIREMENT: GENERICS
// TelemetryPacket<T> below is the reusable generic wrapper class.
// =====================================================================

/// <summary>
/// Generic wrapper that lets the ingestion pipeline handle disparate incoming
/// data structures (float temperatures, int power draws, bool switch states, etc.)
/// uniformly, without boxing every value into <see cref="object"/> and without
/// writing a bespoke DTO per sensor type. Because T is constrained to
/// <see cref="IConvertible"/>, the API can still format/compare/serialize the
/// value generically while each concrete instantiation (TelemetryPacket&lt;float&gt;,
/// TelemetryPacket&lt;int&gt;, TelemetryPacket&lt;bool&gt;...) remains a distinct,
/// boxing-free value carrier at the CLR level.
/// </summary>
/// <typeparam name="T">The primitive payload type carried by this packet.</typeparam>
public class TelemetryPacket<T> where T : IConvertible
{
    public Guid PacketId { get; init; } = Guid.NewGuid();

    /// <summary>MAC address / unique identifier of the originating sensor.</summary>
    public string DeviceId { get; init; } = string.Empty;

    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;

    /// <summary>The strongly typed telemetry value (float, int, bool, ...).</summary>
    public T Value { get; init; } = default!;

    /// <summary>Unit of measure, e.g. "°C", "W", "state".</summary>
    public string Unit { get; init; } = string.Empty;

    /// <summary>True if this packet fell outside the sensor's configured safe band.</summary>
    public bool IsAnomalous { get; set; }

    public override string ToString() =>
        $"[{TimestampUtc:O}] {DeviceId} => {Value}{Unit}{(IsAnomalous ? " (ANOMALY)" : string.Empty)}";
}

/// <summary>Non-generic marker so heterogeneous packets can be stored side by side.</summary>
public interface ITelemetryEnvelope
{
    string DeviceId { get; }
    DateTime TimestampUtc { get; }
    bool IsAnomalous { get; }
    object BoxedValue { get; }
    string Unit { get; }
}

/// <summary>
/// Thin adapter that lets a strongly typed TelemetryPacket&lt;T&gt; be stored in a
/// single heterogeneous collection (e.g. List&lt;ITelemetryEnvelope&gt;) while the
/// original generic packet keeps doing the real work without ever being boxed
/// itself - only this read-only view boxes the value, and only when something
/// actually needs to inspect it generically (e.g. the dashboard feed).
/// </summary>
public sealed class TelemetryEnvelope<T> : ITelemetryEnvelope where T : IConvertible
{
    private readonly TelemetryPacket<T> _packet;

    public TelemetryEnvelope(TelemetryPacket<T> packet) => _packet = packet;

    public string DeviceId => _packet.DeviceId;
    public DateTime TimestampUtc => _packet.TimestampUtc;
    public bool IsAnomalous => _packet.IsAnomalous;
    public object BoxedValue => _packet.Value!;
    public string Unit => _packet.Unit;
}
