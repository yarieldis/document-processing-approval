using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using DocumentProcessing.Functions.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace DocumentProcessing.Functions.Middleware;

public sealed class AuthenticationMiddleware : IFunctionsWorkerMiddleware
{
    private static readonly ConfigurationManager<OpenIdConnectConfiguration>?[] _configManagers = [null!, null!];

    private readonly AuthOptions _options;
    private readonly JwtSecurityTokenHandler _tokenHandler;

    public AuthenticationMiddleware(
        IOptions<AuthOptions> options,
        JwtSecurityTokenHandler? tokenHandler = null)
    {
        _options = options.Value;
        _tokenHandler = tokenHandler ?? new JwtSecurityTokenHandler();
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        // Only apply to HTTP-triggered functions
        var httpRequestData = await context.GetHttpRequestDataAsync();
        if (httpRequestData is null)
        {
            await next(context);
            return;
        }

        // Development bypass: create a synthetic identity
        if (_options.Bypass)
        {
            var bypassPrincipal = CreateBypassPrincipal();
            context.Items["AuthPrincipal"] = bypassPrincipal;
            await next(context);
            return;
        }

        // Extract Bearer token from Authorization header
        if (!httpRequestData.Headers.TryGetValues("Authorization", out var authHeaders) ||
            !authHeaders.Any(h => h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)))
        {
            var unauthorized = httpRequestData.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
            await unauthorized.WriteStringAsync("Missing or invalid Authorization header. Expected: Bearer <token>");
            context.GetInvocationResult().Value = unauthorized;
            return;
        }

        var bearerToken = authHeaders
            .First(h => h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            ["Bearer ".Length..];

        // Validate the JWT
        try
        {
            // Quick pre-check: can we at least read the token?
            if (!_tokenHandler.CanReadToken(bearerToken))
            {
                var badRequest = httpRequestData.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync("Invalid token format.");
                context.GetInvocationResult().Value = badRequest;
                return;
            }

            var validationParameters = await BuildValidationParameters();

            var principal = _tokenHandler.ValidateToken(
                bearerToken, validationParameters, out var validatedToken);

            // Authorization: check for the required role
            if (!principal.HasClaim(c => c.Type == "roles" &&
                                         c.Value == "DocumentContributor"))
            {
                var forbidden = httpRequestData.CreateResponse(System.Net.HttpStatusCode.Forbidden);
                await forbidden.WriteStringAsync(
                    "Insufficient permissions. Required role: DocumentContributor.");
                context.GetInvocationResult().Value = forbidden;
                return;
            }

            context.Items["AuthPrincipal"] = principal;
        }
        catch (SecurityTokenExpiredException)
        {
            var unauthorized = httpRequestData.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
            await unauthorized.WriteStringAsync("Token has expired.");
            context.GetInvocationResult().Value = unauthorized;
            return;
        }
        catch (SecurityTokenException ex)
        {
            var unauthorized = httpRequestData.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
            await unauthorized.WriteStringAsync($"Token validation failed: {ex.Message}");
            context.GetInvocationResult().Value = unauthorized;
            return;
        }

        await next(context);
    }

    internal static ClaimsPrincipal CreateBypassPrincipal()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Upn, "dev-user@local"),
            new Claim("preferred_username", "dev-user@local"),
            new Claim("roles", "DocumentContributor"),
            new Claim("name", "Development User")
        };

        var identity = new ClaimsIdentity(claims, "Development");
        return new ClaimsPrincipal(identity);
    }

    private async Task<TokenValidationParameters> BuildValidationParameters()
    {
        var issuer = GetEffectiveIssuer();

        var configManager = GetOrCreateConfigManager(issuer);
        var oidcConfig = await configManager.GetConfigurationAsync();

        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,

            ValidateAudience = true,
            ValidAudience = _options.ClientId,

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5),

            IssuerSigningKeys = oidcConfig.SigningKeys,

            RoleClaimType = "roles",

            NameClaimType = "name"
        };
    }

    private string GetEffectiveIssuer()
    {
        if (!string.IsNullOrWhiteSpace(_options.Issuer))
            return _options.Issuer;

        if (!string.IsNullOrWhiteSpace(_options.TenantId))
            return $"https://login.microsoftonline.com/{_options.TenantId}/v2.0";

        return "https://login.microsoftonline.com/common/v2.0";
    }

    private static ConfigurationManager<OpenIdConnectConfiguration> GetOrCreateConfigManager(string issuer)
    {
        var index = issuer.GetHashCode() % _configManagers.Length;
        if (index < 0) index += _configManagers.Length;

        lock (_configManagers)
        {
            var existing = _configManagers[index];
            if (existing is not null)
                return existing;

            var manager = new ConfigurationManager<OpenIdConnectConfiguration>(
                $"{issuer}/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever());

            _configManagers[index] = manager;
            return manager;
        }
    }
}
