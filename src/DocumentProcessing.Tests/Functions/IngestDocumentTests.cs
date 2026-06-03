using System.Security.Claims;
using DocumentProcessing.Functions.Functions;
using DocumentProcessing.Tests.Helpers;

namespace DocumentProcessing.Tests.Functions;

/// <summary>
/// Unit tests for IngestDocument logic that don't require FunctionContext/HttpRequestData.
/// Full HTTP trigger flow is tested via integration (func start + HTTP call).
/// </summary>
public class IngestDocumentTests
{
    // ── ExtractIdentity ────────────────────────────────────────────

    [Fact]
    public void ExtractIdentity_FromUpn()
    {
        var principal = MockHelpers.CreateTestPrincipal(
            upn: "alice@contoso.com",
            preferredUsername: "alice_alt@contoso.com");

        var result = IngestDocument.ExtractIdentity(principal);

        Assert.Equal("alice@contoso.com", result);
    }

    [Fact]
    public void ExtractIdentity_FallsBackToPreferredUsername()
    {
        var principal = MockHelpers.CreateTestPrincipal(
            upn: null,
            preferredUsername: "bob@fabrikam.com");

        var result = IngestDocument.ExtractIdentity(principal);

        Assert.Equal("bob@fabrikam.com", result);
    }

    [Fact]
    public void ExtractIdentity_FallsBackToName()
    {
        var principal = MockHelpers.CreateTestPrincipal(
            upn: null,
            preferredUsername: null,
            name: "Charlie Brown");

        var result = IngestDocument.ExtractIdentity(principal);

        Assert.Equal("Charlie Brown", result);
    }

    [Fact]
    public void ExtractIdentity_FallsBackToNameIdentifier()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "sub-12345"),
                new Claim("roles", "DocumentContributor")
            }, "Test"));

        var result = IngestDocument.ExtractIdentity(principal);

        Assert.Equal("sub-12345", result);
    }

    [Fact]
    public void ExtractIdentity_FallsBackToUnknown_WhenNoIdentityClaims()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(new[]
            {
                new Claim("roles", "DocumentContributor")
            }, "Test"));

        var result = IngestDocument.ExtractIdentity(principal);

        Assert.Equal("unknown", result);
    }

    [Fact]
    public void ExtractIdentity_Upn_TakesPriorityOverAll()
    {
        var principal = MockHelpers.CreateTestPrincipal(
            upn: "primary@contoso.com",
            preferredUsername: "secondary@contoso.com",
            name: "Display Name",
            nameIdentifier: "sub-id");

        var result = IngestDocument.ExtractIdentity(principal);

        Assert.Equal("primary@contoso.com", result);
    }
}
