using System.Collections;

namespace SmartX.Api.Models;

// =====================================================================
// ASSIGNMENT REQUIREMENT: COLLECTIONS OPTIMISATION (custom data structure)
// A bounded, generic archive collection. Unlike a plain List<T>, it keeps
// a running per-device count alongside the sequential data, so "how many
// samples does MeterA have" is an O(1) dictionary lookup instead of an
// O(n) scan over the whole archive. It also evicts the oldest entry once
// the configured capacity is reached, so memory use on a resource-
// constrained gateway stays bounded no matter how long ingestion runs.
// =====================================================================
public class TelemetryArchive<T> : IEnumerable<TelemetryPacket<T>> where T : IConvertible
{
    private readonly List<TelemetryPacket<T>> _items = new();
    private readonly Dictionary<string, int> _countsByDevice = new();
    private readonly int _capacity;

    public TelemetryArchive(int capacity = 5000)
    {
        _capacity = capacity;
    }

    public int Count => _items.Count;

    public void Add(TelemetryPacket<T> packet)
    {
        _items.Add(packet);
        _countsByDevice[packet.DeviceId] = _countsByDevice.GetValueOrDefault(packet.DeviceId) + 1;

        if (_items.Count > _capacity)
        {
            var evicted = _items[0];
            _items.RemoveAt(0);
            _countsByDevice[evicted.DeviceId] = Math.Max(0, _countsByDevice[evicted.DeviceId] - 1);
        }
    }

    /// <summary>O(1) lookup instead of scanning every archived packet.</summary>
    public int CountForDevice(string deviceId) => _countsByDevice.GetValueOrDefault(deviceId);

    public IEnumerable<TelemetryPacket<T>> ForDevice(string deviceId) => _items.Where(i => i.DeviceId == deviceId);

    public IEnumerator<TelemetryPacket<T>> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
