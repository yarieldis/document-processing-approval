using System.Security.Claims;
using Azure.Messaging.ServiceBus;
using Moq;

namespace DocumentProcessing.Tests.Helpers;

/// <summary>
/// Factory methods for creating test dependencies.
/// The FunctionContext/HttpRequestData types from the Azure Functions Worker SDK
/// use extension methods extensively, making them unsuitable for unit-test mocking.
/// Tests for HTTP-triggered functions and middleware are best covered by
/// integration tests running the actual Functions host.
/// </summary>
public static class MockHelpers
{
    // ── ServiceBusClient ────────────────────────────────────────────

    public static (Mock<ServiceBusClient> Client, Mock<ServiceBusSender> Sender, List<ServiceBusMessage> SentMessages)
        CreateMockServiceBus()
    {
        var sentMessages = new List<ServiceBusMessage>();

        var senderMock = new Mock<ServiceBusSender>();
        senderMock
            .Setup(s => s.SendMessageAsync(
                It.IsAny<ServiceBusMessage>(),
                It.IsAny<CancellationToken>()))
            .Callback<ServiceBusMessage, CancellationToken>((msg, _) => sentMessages.Add(msg))
            .Returns(Task.CompletedTask);
        senderMock
            .Setup(s => s.DisposeAsync())
            .Returns(ValueTask.CompletedTask);

        var clientMock = new Mock<ServiceBusClient>();
        clientMock
            .Setup(c => c.CreateSender(It.Is<string>(n => n == "document-events")))
            .Returns(senderMock.Object);

        return (clientMock, senderMock, sentMessages);
    }

    // ── ClaimsPrincipal ─────────────────────────────────────────────

    public static ClaimsPrincipal CreateTestPrincipal(
        string? upn = null,
        string? preferredUsername = null,
        string? name = null,
        string? nameIdentifier = null,
        string[]? roles = null)
    {
        var claims = new List<Claim>();

        if (upn is not null)
            claims.Add(new Claim(ClaimTypes.Upn, upn));
        if (preferredUsername is not null)
            claims.Add(new Claim("preferred_username", preferredUsername));
        if (name is not null)
            claims.Add(new Claim(ClaimTypes.Name, name));
        if (nameIdentifier is not null)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, nameIdentifier));

        roles ??= ["DocumentContributor"];
        foreach (var role in roles)
            claims.Add(new Claim("roles", role));

        var identity = new ClaimsIdentity(claims, "Test");
        return new ClaimsPrincipal(identity);
    }
}
