using System.Diagnostics;
using FastEndpoints;

namespace Fbs.WebApi.Preprocessors;

public class TraceIdPreprocessor(InstrumentationSource instrumentation) : IGlobalPreProcessor
{
    public async Task PreProcessAsync(IPreProcessorContext context, CancellationToken ct)
    {
        if (Activity.Current is not null)
        {
            context.HttpContext.Response.Headers.Append("X-Fbs-Trace-Id", Activity.Current.Id);
        }
    }
}