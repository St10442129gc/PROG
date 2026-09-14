using System.Collections.Concurrent;
using SmartX.Api.Models;

namespace SmartX.Api.Services;

/// <summary>
/// Thread-safe in-memory store for the gateway prototype. A real deployment would
/// swap this for a time-series database, but the shape of the API surface stays
/// the same.
/// </summary>
public class TelemetryStore
{
    public ConcurrentDictionary<string, SensorProfile> Sensors { get; } = new();

    // Most-recent-first feed, kept small for the live dashboard.
    private readonly ConcurrentQueue<ITelemetryEnvelope> _liveFeed = new();
    private const int LiveFeedCapacity = 200;

    // =====================================================================
    // ASSIGNMENT REQUIREMENT: ADVANCED ARRAYS (JAGGED ARRAY -> List<T>)
    // _rawBatches below is the jagged array; IngestBatch() promotes each
    // sensor's raw samples into the optimised List<TelemetryPacket<double>>
    // archive further down.
    // =====================================================================

    /// <summary>
    /// Jagged array of raw historical batches: one row per sensor, each row an
    /// independently-sized array of raw samples collected before being promoted
    /// into the optimised List&lt;T&gt; archive below. Jagged (rather than
    /// rectangular multi-dimensional) because sample counts differ wildly between
    /// a slow environmental sensor and a high-frequency power meter.
    /// </summary>
    private double[][] _rawBatches = Array.Empty<double[]>();
    private readonly List<string> _batchOwners = new(); // parallel index -> deviceId
    private readonly object _batchLock = new();

    private readonly TelemetryArchive<double> _archive = new();

    public void RegisterSensor(SensorProfile profile) => Sensors[profile.Id] = profile;

    public void PushLive(ITelemetryEnvelope envelope)
    {
        _liveFeed.Enqueue(envelope);
        while (_liveFeed.Count > LiveFeedCapacity && _liveFeed.TryDequeue(out _)) { }
    }

    public IEnumerable<ITelemetryEnvelope> GetLiveFeed(string? deviceId = null, int take = 50)
    {
        var snapshot = _liveFeed.ToArray().Reverse();
        if (!string.IsNullOrWhiteSpace(deviceId))
            snapshot = snapshot.Where(e => e.DeviceId == deviceId);
        return snapshot.Take(take);
    }

    /// <summary>
    /// Loads a raw sample batch for a sensor into the jagged array buffer, then
    /// promotes it into the flat archive List&lt;TelemetryPacket&lt;double&gt;&gt;
    /// used by charts and the health-score engine.
    /// </summary>
    public void IngestBatch(string deviceId, double[] rawSamples)
    {
        lock (_batchLock)
        {
            int index = _batchOwners.IndexOf(deviceId);
            if (index < 0)
            {
                _batchOwners.Add(deviceId);
                index = _batchOwners.Count - 1;
                Array.Resize(ref _rawBatches, _batchOwners.Count);
            }

            _rawBatches[index] = rawSamples;

            foreach (var sample in rawSamples)
            {
                _archive.Add(new TelemetryPacket<double>
                {
                    DeviceId = deviceId,
                    Value = sample,
                    Unit = "raw"
                });
            }
        }
    }

    public TelemetryArchive<double> Archive
    {
        get { lock (_batchLock) { return _archive; } }
    }
}
