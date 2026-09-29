using System.Diagnostics.CodeAnalysis;
using Fbs.WebApi.Tests.Data;

// FastEndpoints keeps the running app's services in a static, so apps started side by side end
// up sharing event handlers and send each other's notifications
[assembly: NotInParallel]
[assembly: ExcludeFromCodeCoverage]

namespace Fbs.WebApi.Tests
{
    public static class GlobalHooks
    {
        [After(TestSession)]
        public static Task DropSharedDatabase() => TestDatabase.DropSharedAsync();
    }
}
