using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using PxMCP.Services;

namespace PxMCP.Tests.Services;

/// <summary>
/// Unit tests for the request body size handling in <see cref="McpRequestValidationMiddleware"/>.
/// </summary>
[TestFixture]
public class McpRequestValidationMiddlewareTests
{
    [Test]
    public async Task McpRequest_LowersServerBodySizeLimitForBodiesWithoutDeclaredLength()
    {
        TestBodySizeFeature feature = new() { MaxRequestBodySize = 30_000_000 };
        (DefaultHttpContext context, bool nextCalled) = await InvokeAsync("/mcp", feature);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(nextCalled, Is.True);
            Assert.That(feature.MaxRequestBodySize, Is.EqualTo(McpRequestValidationMiddleware.MaxRequestBodyBytes));
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        }
    }

    [Test]
    public async Task McpRequest_LeavesReadOnlyBodySizeFeatureUnchanged()
    {
        TestBodySizeFeature feature = new() { MaxRequestBodySize = 30_000_000, IsReadOnly = true };
        (_, bool nextCalled) = await InvokeAsync("/mcp", feature);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(nextCalled, Is.True);
            Assert.That(feature.MaxRequestBodySize, Is.EqualTo(30_000_000));
        }
    }

    [Test]
    public async Task OtherPaths_DoNotChangeBodySizeLimit()
    {
        TestBodySizeFeature feature = new() { MaxRequestBodySize = 30_000_000 };
        (_, bool nextCalled) = await InvokeAsync("/health", feature);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(nextCalled, Is.True);
            Assert.That(feature.MaxRequestBodySize, Is.EqualTo(30_000_000));
        }
    }

    private static async Task<(DefaultHttpContext Context, bool NextCalled)> InvokeAsync(string path, TestBodySizeFeature feature)
    {
        bool nextCalled = false;
        McpRequestValidationMiddleware middleware = new(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            McpRequestPolicy.Create(null),
            NullLogger<McpRequestValidationMiddleware>.Instance);
        DefaultHttpContext context = new();
        context.Request.Path = path;
        context.Request.Host = new HostString("localhost");
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        await middleware.InvokeAsync(context);
        return (context, nextCalled);
    }

    private sealed class TestBodySizeFeature : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly { get; init; }

        public long? MaxRequestBodySize { get; set; }
    }
}
