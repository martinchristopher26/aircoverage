using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;

namespace AirCoverage.Api.Ado;

/// <summary>
/// Injects the PAT as Basic auth on every ADO request. This is the single seam to
/// replace when moving to Entra ID (swap for an OAuth/managed-identity token handler).
/// </summary>
public class PatAuthHandler : DelegatingHandler
{
    private readonly string _header;

    public PatAuthHandler(IOptions<AdoOptions> options)
    {
        var pat = options.Value.Pat;
        // Fix 5: RFC 7617 mandates UTF-8 encoding for Basic auth credentials.
        _header = Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + pat));
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _header);
        return base.SendAsync(request, ct);
    }
}
