namespace SmartX.Api.Models;

public record RegisterSensorRequest(string MacAddress, string DeploymentLocation, SensorCategory Category);

/// <summary>
/// ValueType selects which TelemetryPacket&lt;T&gt; instantiation handles this
/// reading server-side, so a float temperature, an int power draw, and a bool
/// switch trigger are each routed to the correctly typed generic path instead of
/// being coerced to a common type.
/// </summary>
public record IngestTelemetryRequest(string DeviceId, string ValueType, string Value, string Unit,
    double ExpectedMin = double.NegativeInfinity, double ExpectedMax = double.PositiveInfinity);

public record IngestBatchRequest(string DeviceId, double[] Samples);

public record TelemetryFeedItem(string DeviceId, DateTime TimestampUtc, object Value, string Unit, bool IsAnomalous);

public record DeploymentValidationRequest(DeploymentNode Root);

/// <summary>Drives the operator-overloading demo: Meter = MeterA + MeterB.</summary>
public record AggregateReadingsRequest(string DeviceIdA, double MagnitudeA, string DeviceIdB, double MagnitudeB);
