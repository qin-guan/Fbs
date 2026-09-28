using System.Net;
using TUnit.Assertions.Attributes;
using TUnit.Assertions.Core;

namespace Fbs.WebApi.Tests.Helpers;

public static class HttpResponseAssertions
{
    /// <summary>
    /// Like <c>HasStatusCode</c>, but a failure shows the response body, such as the validation errors.
    /// </summary>
    [GenerateAssertion(ExpectationMessage = "to have status code {expected}")]
    public static async Task<AssertionResult> HasStatus(this HttpResponseMessage response, HttpStatusCode expected) =>
        response.StatusCode == expected
            ? AssertionResult.Passed
            : AssertionResult.Failed($"found {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
}
