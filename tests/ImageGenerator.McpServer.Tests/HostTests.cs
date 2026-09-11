namespace ImageGenerator.McpServer;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

public sealed class HostTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory factory;

    public HostTests(TestApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task HealthReturnsOk()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        // Assert
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public void StartupFailsWithoutEndpoint()
    {
        // Arrange
        using var invalid = factory.WithWebHostBuilder(static builder => builder.UseSetting("ImageGenerator:Endpoint", string.Empty));

        // Act & Assert
        Assert.Throws<OptionsValidationException>(invalid.CreateClient);
    }
}
