using SmartX.Api.Models;
using SmartX.Api.Services;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<TelemetryStore>();
builder.Services.AddSingleton<HealthScoreService>();
builder.Services.AddSingleton<FileEncryptionService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Without this, enums are only accepted as numbers over JSON (0, 1, 2),
// so sending the category as "Environmental" from the client would fail
// to deserialize and the request would be rejected with a 400.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddCors(options =>
{
    // Wide-open CORS for the local dashboard client during development.
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

var uploadsRoot = Path.Combine(AppContext.BaseDirectory, "uploads");
Directory.CreateDirectory(uploadsRoot);

// ---------------------------------------------------------------------------
// Sensor registration
// ---------------------------------------------------------------------------
app.MapPost("/api/sensors", (RegisterSensorRequest request, TelemetryStore store) =>
{
    if (string.IsNullOrWhiteSpace(request.MacAddress) || string.IsNullOrWhiteSpace(request.DeploymentLocation))
        return Results.BadRequest("MAC address and deployment location are required.");

    var profile = new SensorProfile
    {
        MacAddress = request.MacAddress,
        DeploymentLocation = request.DeploymentLocation,
        Category = request.Category
    };
    store.RegisterSensor(profile);
    return Results.Created($"/api/sensors/{profile.Id}", profile);
})
.WithName("RegisterSensor");

app.MapGet("/api/sensors", (TelemetryStore store) =>
    Results.Ok(store.Sensors.Values.OrderByDescending(s => s.RegisteredUtc)))
.WithName("ListSensors");

app.MapGet("/api/sensors/{id}", (string id, TelemetryStore store) =>
    store.Sensors.TryGetValue(id, out var profile) ? Results.Ok(profile) : Results.NotFound())
.WithName("GetSensor");

// ---------------------------------------------------------------------------
// Media / log attachment (multipart file upload)
// ---------------------------------------------------------------------------
app.MapPost("/api/sensors/{id}/media", async (string id, HttpRequest request, TelemetryStore store, FileEncryptionService encryption) =>
{
    if (!store.Sensors.TryGetValue(id, out var profile))
        return Results.NotFound();

    if (!request.HasFormContentType)
        return Results.BadRequest("Expected multipart/form-data.");

    var form = await request.ReadFormAsync();
    if (form.Files.Count == 0)
        return Results.BadRequest("No file supplied.");

    var savedNames = new List<string>();
    foreach (var file in form.Files)
    {
        // Stored with a .enc extension and AES-256 encrypted - see
        // FileEncryptionService. Nothing readable ever touches disk.
        var safeName = $"{id}_{DateTime.UtcNow:yyyyMMddHHmmss}_{Path.GetFileName(file.FileName)}.enc";
        var fullPath = Path.Combine(uploadsRoot, safeName);
        await using var uploadStream = file.OpenReadStream();
        await encryption.EncryptToFileAsync(uploadStream, fullPath);
        savedNames.Add(safeName);
        profile.AttachedMedia.Add(safeName);
    }

    return Results.Ok(new { profile.Id, Saved = savedNames });
})
.WithName("UploadSensorMedia")
.DisableAntiforgery();

app.MapGet("/api/sensors/{id}/media/{fileName}/download", async (string id, string fileName, TelemetryStore store, FileEncryptionService encryption) =>
{
    if (!store.Sensors.TryGetValue(id, out var profile) || !profile.AttachedMedia.Contains(fileName))
        return Results.NotFound();

    var fullPath = Path.Combine(uploadsRoot, fileName);
    if (!File.Exists(fullPath))
        return Results.NotFound();

    var decryptedBytes = await encryption.DecryptFileAsync(fullPath);
    var originalName = fileName.EndsWith(".enc") ? fileName[..^4] : fileName;
    return Results.File(decryptedBytes, "application/octet-stream", originalName);
})
.WithName("DownloadSensorMedia");

// ---------------------------------------------------------------------------
// Telemetry ingestion - uses TelemetryPacket<T> per declared value type so no
// boxing/unboxing or lossy type coercion happens on the hot ingestion path.
// ---------------------------------------------------------------------------
app.MapPost("/api/telemetry", (IngestTelemetryRequest request, TelemetryStore store, HealthScoreService health) =>
{
    if (!store.Sensors.TryGetValue(request.DeviceId, out var profile))
        return Results.NotFound($"Unknown sensor '{request.DeviceId}'. Register it first.");

    TelemetryFeedItem feedItem;

    switch (request.ValueType.ToLowerInvariant())
    {
        case "float":
        case "double":
        {
            var value = double.Parse(request.Value);
            var anomalous = HealthScoreService.IsAnomalous(value, request.ExpectedMin, request.ExpectedMax);
            var packet = new TelemetryPacket<double>
            {
                DeviceId = request.DeviceId,
                Value = value,
                Unit = request.Unit,
                IsAnomalous = anomalous
            };
            store.PushLive(new TelemetryEnvelope<double>(packet));
            ApplyHealthOutcome(profile, anomalous, health);
            feedItem = new TelemetryFeedItem(packet.DeviceId, packet.TimestampUtc, packet.Value, packet.Unit, anomalous);
            break;
        }
        case "int":
        {
            var value = int.Parse(request.Value);
            var anomalous = HealthScoreService.IsAnomalous(value, request.ExpectedMin, request.ExpectedMax);
            var packet = new TelemetryPacket<int>
            {
                DeviceId = request.DeviceId,
                Value = value,
                Unit = request.Unit,
                IsAnomalous = anomalous
            };
            store.PushLive(new TelemetryEnvelope<int>(packet));
            ApplyHealthOutcome(profile, anomalous, health);
            feedItem = new TelemetryFeedItem(packet.DeviceId, packet.TimestampUtc, packet.Value, packet.Unit, anomalous);
            break;
        }
        case "bool":
        {
            var value = bool.Parse(request.Value);
            var packet = new TelemetryPacket<bool>
            {
                DeviceId = request.DeviceId,
                Value = value,
                Unit = request.Unit,
                IsAnomalous = false
            };
            store.PushLive(new TelemetryEnvelope<bool>(packet));
            ApplyHealthOutcome(profile, anomalous: false, health);
            feedItem = new TelemetryFeedItem(packet.DeviceId, packet.TimestampUtc, packet.Value, packet.Unit, false);
            break;
        }
        default:
            return Results.BadRequest($"Unsupported ValueType '{request.ValueType}'. Use float, int, or bool.");
    }

    return Results.Ok(feedItem);
})
.WithName("IngestTelemetry");

static void ApplyHealthOutcome(SensorProfile profile, bool anomalous, HealthScoreService health)
{
    if (anomalous) health.RecordAnomaly(profile);
    else health.RecordCleanReading(profile);
}

app.MapPost("/api/telemetry/{deviceId}/disconnect", (string deviceId, TelemetryStore store, HealthScoreService health) =>
{
    if (!store.Sensors.TryGetValue(deviceId, out var profile))
        return Results.NotFound();
    health.RecordDisconnect(profile);
    return Results.Ok(profile);
})
.WithName("SimulateDisconnect");

app.MapGet("/api/telemetry/feed", (string? deviceId, int take, TelemetryStore store) =>
    Results.Ok(store.GetLiveFeed(deviceId, take <= 0 ? 50 : take)
        .Select(e => new TelemetryFeedItem(e.DeviceId, e.TimestampUtc, e.BoxedValue, e.Unit, e.IsAnomalous))))
.WithName("GetTelemetryFeed");

// ---------------------------------------------------------------------------
// Historical batches: jagged array ingestion -> optimised List<T> archive
// ---------------------------------------------------------------------------
app.MapPost("/api/telemetry/batch", (IngestBatchRequest request, TelemetryStore store) =>
{
    store.IngestBatch(request.DeviceId, request.Samples);
    return Results.Ok(new { request.DeviceId, ArchivedCount = store.Archive.CountForDevice(request.DeviceId) });
})
.WithName("IngestBatch");

// ---------------------------------------------------------------------------
// Recursive nested deployment-tree validation
// ---------------------------------------------------------------------------
app.MapPost("/api/deployment/validate", (DeploymentValidationRequest request) =>
{
    var result = DeploymentValidator.Validate(request.Root);
    return Results.Ok(result);
})
.WithName("ValidateDeploymentTree");

// ---------------------------------------------------------------------------
// Operator-overloading demo: aggregate/delta-compare two SensorReadings,
// e.g. combined smart-meter load: Meter = Meter1 + Meter2.
// ---------------------------------------------------------------------------
app.MapPost("/api/sensors/aggregate", (AggregateReadingsRequest request) =>
{
    var a = new SensorReading(request.DeviceIdA, request.MagnitudeA);
    var b = new SensorReading(request.DeviceIdB, request.MagnitudeB);

    var sum = a + b;   // operator +
    var delta = a - b; // operator -

    return Results.Ok(new
    {
        Sum = new { sum.DeviceId, sum.Magnitude },
        Delta = new { delta.DeviceId, delta.Magnitude },
        AIsGreaterThanB = a > b,   // operator >
        AIsLessThanB = a < b,      // operator <
        AEqualsB = a == b          // operator ==
    });
})
.WithName("AggregateSensorReadings");

app.MapGet("/", () => Results.Redirect("/swagger"));

app.Run();
