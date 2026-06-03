using System.Security.Claims;
using DocumentProcessing.Functions.Configuration;
using DocumentProcessing.Functions.Middleware;
using Microsoft.Extensions.Options;

namespace DocumentProcessing.Tests.Middleware;

/// <summary>
/// Unit tests for AuthenticationMiddleware logic that don't require FunctionContext.
/// Full middleware flow is tested via integration (func start + HTTP call with tokens).
/// </summary>
public class AuthenticationMiddlewareTests
{
    // ── Bypass principal ──────────────────────────────────────────

    [Fact]
    public void CreateBypassPrincipal_ContainsRequiredClaims()
    {
        var principal = AuthenticationMiddleware.CreateBypassPrincipal();

        Assert.NotNull(principal);
        Assert.Contains(principal.Claims, c => c.Type == ClaimTypes.Upn && c.Value == "dev-user@local");
        Assert.Contains(principal.Claims, c => c.Type == "preferred_username" && c.Value == "dev-user@local");
        Assert.Contains(principal.Claims, c => c.Type == "roles" && c.Value == "DocumentContributor");
        Assert.Contains(principal.Claims, c => c.Type == "name" && c.Value == "Development User");
    }

    [Fact]
    public void CreateBypassPrincipal_HasDevelopmentAuthenticationType()
    {
        var principal = AuthenticationMiddleware.CreateBypassPrincipal();
        Assert.Equal("Development", principal.Identity?.AuthenticationType);
    }

    [Fact]
    public void CreateBypassPrincipal_IsDeterministic()
    {
        var p1 = AuthenticationMiddleware.CreateBypassPrincipal();
        var p2 = AuthenticationMiddleware.CreateBypassPrincipal();

        Assert.Equal(
            p1.FindFirst(ClaimTypes.Upn)?.Value,
            p2.FindFirst(ClaimTypes.Upn)?.Value);
    }

    // ── Constructor / options ─────────────────────────────────────

    [Fact]
    public void Constructor_WithCustomIssuer_UsesIt()
    {
        var options = Options.Create(new AuthOptions
        {
            Bypass = false,
            Issuer = "https://custom-issuer.example.com/v2.0",
            TenantId = "some-tenant"
        });

        var middleware = new AuthenticationMiddleware(options);
        Assert.NotNull(middleware);
    }

    [Fact]
    public void Constructor_WithTenantId_BuildsIssuer()
    {
        var options = Options.Create(new AuthOptions
        {
            Bypass = false,
            TenantId = "contoso-tenant-id",
            Issuer = ""
        });

        var middleware = new AuthenticationMiddleware(options);
        Assert.NotNull(middleware);
    }

    [Fact]
    public void Constructor_WithNothing_FallsBackToCommonIssuer()
    {
        var options = Options.Create(new AuthOptions
        {
            Bypass = false,
            TenantId = "",
            Issuer = ""
        });

        var middleware = new AuthenticationMiddleware(options);
        Assert.NotNull(middleware);
    }

    [Fact]
    public void Constructor_WithNullTokenHandler_CreatesDefault()
    {
        var options = Options.Create(new AuthOptions { Bypass = true });
        var middleware = new AuthenticationMiddleware(options, tokenHandler: null);
        Assert.NotNull(middleware);
    }
}
