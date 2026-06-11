using System.Security.Claims;
using AirCoverage.Api.Dtos;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace AirCoverage.Api.Endpoints;

public static class AuthEndpoints
{
    public const string DisplayName = "Dev Team";

    public static IEndpointRouteBuilder MapAuthApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        // v1: one shared credential from config. v2 swaps this group for OIDC/Entra
        // without touching the data model or the items endpoints.
        group.MapPost("/login", async (LoginRequest req, HttpContext http, IConfiguration cfg) =>
        {
            var user = cfg["Auth:Username"] ?? "devteam";
            var pass = cfg["Auth:Password"] ?? "aircoverage";

            if (!string.Equals(req.Username, user, StringComparison.Ordinal) ||
                !string.Equals(req.Password, pass, StringComparison.Ordinal))
            {
                return Results.Json(new { error = "Incorrect username or password." }, statusCode: 401);
            }

            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, req.Username) },
                CookieAuthenticationDefaults.AuthenticationScheme);

            await http.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity));

            return Results.Ok(new UserResponse(req.Username, DisplayName));
        });

        group.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok();
        });

        // Lets the SPA discover auth state on load. 401 (not a redirect) when signed out.
        group.MapGet("/me", (HttpContext http) =>
            http.User.Identity?.IsAuthenticated == true
                ? Results.Ok(new UserResponse(http.User.Identity!.Name ?? "", DisplayName))
                : Results.Unauthorized());

        return app;
    }
}
