using AirCoverage.Api;

namespace AirCoverage.Api.Tests;

public class HostingModeTests
{
    [Theory]
    [InlineData("8080", 8080)]
    [InlineData("3000", 3000)]
    public void ResolvePlatformPort_returns_port_when_valid(string env, int expected)
    {
        Assert.Equal(expected, HostingMode.ResolvePlatformPort(env));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("-1")]
    public void ResolvePlatformPort_returns_null_when_absent_or_invalid(string? env)
    {
        Assert.Null(HostingMode.ResolvePlatformPort(env));
    }
}
