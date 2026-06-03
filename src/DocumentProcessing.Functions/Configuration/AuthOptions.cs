namespace DocumentProcessing.Functions.Configuration;

public sealed class AuthOptions
{
    public const string SectionName = "Authentication";

    /// <summary>
    /// When true, bypasses JWT validation and creates a synthetic dev identity.
    /// Set in local.settings.json for development; must be false in production.
    /// </summary>
    public bool Bypass { get; set; }

    /// <summary>
    /// Azure AD Tenant ID used to build the issuer URL.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// The Application (Client) ID of the Function App's App Registration.
    /// Used as the expected 'aud' claim value.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Full issuer URL. If empty, built from TenantId as
    /// "https://login.microsoftonline.com/{TenantId}/v2.0".
    /// </summary>
    public string Issuer { get; set; } = string.Empty;
}
