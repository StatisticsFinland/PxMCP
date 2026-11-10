using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using PxMCP.Configuration;
using PxMCP.Services;

namespace PxMCP.Tests.Services;

/// <summary>
/// Unit tests for <see cref="McpRequestPolicy"/>.
/// </summary>
[TestFixture]
public class McpRequestPolicyTests
{
    [TestCase("localhost")]
    [TestCase("localhost:5277")]
    [TestCase("LocalHost:5277")]
    [TestCase("127.0.0.1:5277")]
    [TestCase("[::1]:5277")]
    public void DefaultPolicy_AcceptsLoopbackHosts(string host)
    {
        McpRequestPolicy policy = McpRequestPolicy.Create(null);

        Assert.That(policy.IsHostAllowed(new HostString(host)), Is.True);
    }

    [TestCase("")]
    [TestCase("evil.example")]
    [TestCase("localhost.evil.example")]
    [TestCase("127.0.0.2")]
    [TestCase("0.0.0.0")]
    public void DefaultPolicy_RejectsOtherHosts(string host)
    {
        McpRequestPolicy policy = McpRequestPolicy.Create(new McpSecurityOptions());

        Assert.That(policy.IsHostAllowed(new HostString(host)), Is.False);
    }

    [TestCase(null)]
    [TestCase("http://localhost")]
    [TestCase("http://localhost:6274")]
    [TestCase("https://127.0.0.1:7269")]
    [TestCase("http://[::1]:5277")]
    public void DefaultPolicy_AcceptsMissingOrLoopbackOrigin(string? origin)
    {
        McpRequestPolicy policy = McpRequestPolicy.Create(null);

        Assert.That(policy.IsOriginAllowed(new StringValues(origin)), Is.True);
    }

    [TestCase("null")]
    [TestCase(" ")]
    [TestCase("http://evil.example")]
    [TestCase("http://localhost.evil.example")]
    [TestCase("file://localhost")]
    [TestCase("http://localhost/path")]
    [TestCase("http://user@localhost")]
    public void DefaultPolicy_RejectsOtherOrigins(string origin)
    {
        McpRequestPolicy policy = McpRequestPolicy.Create(null);

        Assert.That(policy.IsOriginAllowed(new StringValues(origin)), Is.False);
    }

    [Test]
    public void IsOriginAllowed_RejectsMultipleValues()
    {
        McpRequestPolicy policy = McpRequestPolicy.Create(null);

        Assert.That(policy.IsOriginAllowed(new StringValues(["http://localhost", "http://localhost"])), Is.False);
    }

    [Test]
    public void ConfiguredPolicy_UsesConfiguredHostsAndExactOrigins()
    {
        McpRequestPolicy policy = McpRequestPolicy.Create(new McpSecurityOptions
        {
            AllowedHosts = ["pxmcp.example.org", "[2001:db8::1]"],
            AllowedOrigins = ["https://Chat.Example.org", "http://chat.example.org:8080/"]
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(policy.IsHostAllowed(new HostString("pxmcp.example.org:443")), Is.True);
            Assert.That(policy.IsHostAllowed(new HostString("[2001:db8::1]:443")), Is.True);
            Assert.That(policy.IsHostAllowed(new HostString("localhost")), Is.False);
            Assert.That(policy.IsOriginAllowed(new StringValues("https://chat.example.org")), Is.True);
            Assert.That(policy.IsOriginAllowed(new StringValues("https://chat.example.org:443")), Is.True);
            Assert.That(policy.IsOriginAllowed(new StringValues("http://chat.example.org:8080")), Is.True);
            Assert.That(policy.IsOriginAllowed(new StringValues("http://chat.example.org")), Is.False);
            Assert.That(policy.IsOriginAllowed(new StringValues("https://chat.example.org:8443")), Is.False);
        }
    }

    [Test]
    public void WildcardHost_AcceptsAnyHost()
    {
        McpRequestPolicy policy = McpRequestPolicy.Create(new McpSecurityOptions { AllowedHosts = ["*"] });

        Assert.That(policy.IsHostAllowed(new HostString("anything.internal:8080")), Is.True);
    }

    [TestCase("pxmcp.example.org:443")]
    [TestCase("*.example.org")]
    [TestCase("")]
    [TestCase("a b")]
    public void Create_RejectsInvalidHosts(string host)
    {
        Assert.That(() => McpRequestPolicy.Create(new McpSecurityOptions { AllowedHosts = [host] }),
            Throws.InvalidOperationException.With.Message.Contains("McpSecurity:AllowedHosts"));
    }

    [Test]
    public void Create_RejectsWildcardCombinedWithHosts()
    {
        Assert.That(() => McpRequestPolicy.Create(new McpSecurityOptions { AllowedHosts = ["*", "localhost"] }),
            Throws.InvalidOperationException.With.Message.Contains("McpSecurity:AllowedHosts"));
    }

    [TestCase("chat.example.org")]
    [TestCase("null")]
    [TestCase("ftp://chat.example.org")]
    [TestCase("https://chat.example.org/path")]
    [TestCase("https://chat.example.org?x=1")]
    [TestCase("https://user@chat.example.org")]
    public void Create_RejectsInvalidOrigins(string origin)
    {
        Assert.That(() => McpRequestPolicy.Create(new McpSecurityOptions { AllowedOrigins = [origin] }),
            Throws.InvalidOperationException.With.Message.Contains("McpSecurity:AllowedOrigins"));
    }
}
