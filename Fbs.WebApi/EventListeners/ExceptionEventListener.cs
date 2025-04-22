using HotChocolate.Execution;
using HotChocolate.Execution.Instrumentation;

namespace Fbs.WebApi.EventListeners;

public class ExceptionEventListener(ILogger<ExceptionEventListener> logger) : ExecutionDiagnosticEventListener
{
    public override void RequestError(IRequestContext context, Exception exception)
    {
        logger.LogError(exception, "A request error occurred!");
    }
}