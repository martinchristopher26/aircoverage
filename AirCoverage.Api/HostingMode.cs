namespace AirCoverage.Api;

/// <summary>
/// Decides how the app should bind. TLS-terminating platforms (Railway, Heroku, etc.)
/// set the <c>PORT</c> environment variable and forward plain HTTP to the container,
/// handling HTTPS at their edge. Locally (no PORT) we serve HTTPS ourselves.
/// </summary>
public static class HostingMode
{
    /// <summary>
    /// The HTTP port to bind when running behind a TLS-terminating platform, or
    /// <c>null</c> to use the local HTTPS dev binding. Guards against a missing or
    /// invalid PORT value.
    /// </summary>
    public static int? ResolvePlatformPort(string? portEnv) =>
        int.TryParse(portEnv, out var port) && port > 0 ? port : null;
}
