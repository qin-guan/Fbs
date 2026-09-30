using System.Diagnostics.Metrics;
using Fbs.WebApi.Telemetry;

namespace Fbs.WebApi.Tests.Helpers;

/// <summary>
/// Listens to what the API counts and times, the way an exporter does, and keeps it to be asked about. What is counted is
/// process-wide, so it only says what happened since it was made, and the tests all run one at a time so nothing else is adding to it.
/// </summary>
public sealed class MetricsRecorder : IDisposable
{
    public sealed record Measurement(string Instrument, double Value, IReadOnlyDictionary<string, string?> Tags);

    private readonly MeterListener _listener = new();
    private readonly List<Measurement> _measurements = [];

    public MetricsRecorder()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == FbsMetrics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.Start();
    }

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var recorded = new Dictionary<string, string?>();
        foreach (var tag in tags)
        {
            recorded[tag.Key] = tag.Value?.ToString();
        }

        lock (_measurements)
        {
            _measurements.Add(new Measurement(instrument.Name, value, recorded));
        }
    }

    private static bool Matches(Measurement measurement, string instrument, (string Key, string Value)[] tags) =>
        measurement.Instrument == instrument && tags.All(t => measurement.Tags.TryGetValue(t.Key, out var value) && value == t.Value);

    public List<Measurement> Of(string instrument, params (string Key, string Value)[] tags)
    {
        lock (_measurements)
        {
            return _measurements.Where(m => Matches(m, instrument, tags)).ToList();
        }
    }

    /// <summary>What a counter went up by, for those measurements that have all of these tags.</summary>
    public double Sum(string instrument, params (string Key, string Value)[] tags) => Of(instrument, tags).Sum(m => m.Value);

    /// <summary>How many times something was timed, or counted, for those with all of these tags.</summary>
    public int Recorded(string instrument, params (string Key, string Value)[] tags) => Of(instrument, tags).Count;

    /// <summary>What the gauges say now, by their tags, as an exporter would see them when it asks.</summary>
    public Dictionary<string, double> Observe(string instrument, string tag)
    {
        int from;
        lock (_measurements)
        {
            from = _measurements.Count;
        }

        _listener.RecordObservableInstruments();
        lock (_measurements)
        {
            return _measurements
                .Skip(from)
                .Where(m => m.Instrument == instrument)
                .ToDictionary(m => m.Tags.GetValueOrDefault(tag) ?? string.Empty, m => m.Value);
        }
    }

    /// <summary>What a gauge with no tags says now.</summary>
    public double? Observe(string instrument) => Observe(instrument, string.Empty).Values.Cast<double?>().SingleOrDefault();

    public void Dispose() => _listener.Dispose();
}
