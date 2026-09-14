using System.Net.Http.Json;
using System.Text.Json;

namespace SmartX.Client.Services;

public record SensorProfileDto(string Id, string MacAddress, string DeploymentLocation, string Category,
    DateTime RegisteredUtc, List<string> AttachedMedia, int HealthScore, int Streak, string Badge);

public record TelemetryFeedItemDto(string DeviceId, DateTime TimestampUtc, JsonElement Value, string Unit, bool IsAnomalous);

public record DeploymentValidationResultDto(bool IsValid, List<string> Errors);

public record ReadingDto(string DeviceId, double Magnitude);
public record AggregateResultDto(ReadingDto Sum, ReadingDto Delta, bool AIsGreaterThanB, bool AIsLessThanB, bool AEqualsB);
public record BatchResultDto(string DeviceId, int ArchivedCount);

/// <summary>
/// Thin, typed wrapper around the SmartX.Api HTTP surface so Razor components
/// never touch HttpClient directly. This is the "API Integration Layer" the
/// dashboard uses to push and retrieve data.
/// </summary>
public class SmartXApiClient
{
    private readonly HttpClient _http;

    public SmartXApiClient(HttpClient http) => _http = http;

    public string BaseAddress => _http.BaseAddress?.ToString().TrimEnd('/') ?? string.Empty;

    public async Task<List<SensorProfileDto>> GetSensorsAsync() =>
        await _http.GetFromJsonAsync<List<SensorProfileDto>>("/api/sensors") ?? new();

    public async Task<SensorProfileDto?> RegisterSensorAsync(string mac, string location, string category)
    {
        var response = await _http.PostAsJsonAsync("/api/sensors", new { MacAddress = mac, DeploymentLocation = location, Category = category });
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }
        return await response.Content.ReadFromJsonAsync<SensorProfileDto>();
    }

    public async Task<bool> UploadMediaAsync(string sensorId, Stream fileStream, string fileName, string contentType)
    {
        using var content = new MultipartFormDataContent();
        using var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        content.Add(streamContent, "file", fileName);

        var response = await _http.PostAsync($"/api/sensors/{sensorId}/media", content);
        return response.IsSuccessStatusCode;
    }

    public async Task<TelemetryFeedItemDto?> SendTelemetryAsync(string deviceId, string valueType, string value,
        string unit, double expectedMin, double expectedMax)
    {
        var response = await _http.PostAsJsonAsync("/api/telemetry", new
        {
            DeviceId = deviceId,
            ValueType = valueType,
            Value = value,
            Unit = unit,
            ExpectedMin = expectedMin,
            ExpectedMax = expectedMax
        });

        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<TelemetryFeedItemDto>();
    }

    public async Task SimulateDisconnectAsync(string deviceId) =>
        await _http.PostAsync($"/api/telemetry/{deviceId}/disconnect", null);

    public async Task<List<TelemetryFeedItemDto>> GetFeedAsync(string? deviceId = null, int take = 30)
    {
        var url = $"/api/telemetry/feed?take={take}" + (deviceId is null ? "" : $"&deviceId={deviceId}");
        return await _http.GetFromJsonAsync<List<TelemetryFeedItemDto>>(url) ?? new();
    }

    /// <summary>Operator-overloading demo: combines two readings via SensorReading + / - / comparisons.</summary>
    public async Task<AggregateResultDto?> AggregateReadingsAsync(string deviceIdA, double magnitudeA, string deviceIdB, double magnitudeB)
    {
        var response = await _http.PostAsJsonAsync("/api/sensors/aggregate", new
        {
            DeviceIdA = deviceIdA,
            MagnitudeA = magnitudeA,
            DeviceIdB = deviceIdB,
            MagnitudeB = magnitudeB
        });
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<AggregateResultDto>();
    }

    /// <summary>Jagged-array demo: loads a raw sample batch, promoted server-side into the List&lt;T&gt; archive.</summary>
    public async Task<BatchResultDto?> LoadBatchAsync(string deviceId, double[] samples)
    {
        var response = await _http.PostAsJsonAsync("/api/telemetry/batch", new { DeviceId = deviceId, Samples = samples });
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<BatchResultDto>();
    }

    /// <summary>Recursion demo: validates a nested deployment tree supplied as raw JSON.</summary>
    public async Task<(bool ok, DeploymentValidationResultDto? result, string? rawError)> ValidateDeploymentTreeAsync(string rootJson)
    {
        var payload = $"{{\"root\":{rootJson}}}";
        using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("/api/deployment/validate", content);
        if (!response.IsSuccessStatusCode)
            return (false, null, await response.Content.ReadAsStringAsync());

        var result = await response.Content.ReadFromJsonAsync<DeploymentValidationResultDto>();
        return (true, result, null);
    }
}
