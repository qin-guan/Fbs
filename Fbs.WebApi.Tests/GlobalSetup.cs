using System.Diagnostics.CodeAnalysis;

// FastEndpoints keeps the running app's services in a static, so apps started side by side end
// up sharing event handlers and send each other's notifications
[assembly: NotInParallel]
[assembly: ExcludeFromCodeCoverage]
