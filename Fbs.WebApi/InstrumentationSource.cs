using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Fbs.WebApi;

public sealed class InstrumentationSource : IDisposable
{
    internal const string ActivitySourceName = "Fbs.WebApi";
    internal const string MeterName = "Fbs.Webapi";

    private readonly Meter _meter;

    public InstrumentationSource()
    {
        var version = typeof(InstrumentationSource).Assembly.GetName().Version?.ToString();
        ActivitySource = new ActivitySource(ActivitySourceName, version);
        _meter = new Meter(MeterName, version);
    }

    public ActivitySource ActivitySource { get; }

    public void Dispose()
    {
        ActivitySource.Dispose();
        _meter.Dispose();
    }
}