namespace ImageGenerator.McpServer;

using ImageGenerator.McpServer.Services;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Client;

public sealed class TestApplicationFactory : WebApplicationFactory<Program>
{
    public FakeFoundryHandler Foundry { get; } = new();

    public string OutputDirectory { get; } = Path.Combine(Path.GetTempPath(), $"mcp-image-test-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("http_ports", string.Empty);
        builder.UseSetting("Prometheus:Uri", string.Empty);
        builder.UseSetting("ImageGenerator:Endpoint", "https://foundry.example.invalid/");
        builder.UseSetting("ImageGenerator:DeploymentName", "gpt-image-test");
        builder.UseSetting("ImageGenerator:ApiKey", "test-api-key");
        builder.UseSetting("ImageGenerator:OutputPath", OutputDirectory);
        builder.UseSetting("ImageGenerator:MaxRetries", "0");
        builder.UseSetting("ImageGenerator:RetentionDays", "0");

        // Foundry呼び出しを偽の応答に差し替える
        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient(ImageGenerationService.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Foundry);
        });
    }

    public async Task<McpClient> CreateMcpClientAsync(CancellationToken cancellationToken)
    {
        var options = new HttpClientTransportOptions { Endpoint = new Uri(Server.BaseAddress, "mcp") };
#pragma warning disable CA2000
        var transport = new HttpClientTransport(options, CreateClient());
#pragma warning restore CA2000
        return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(OutputDirectory))
        {
            try
            {
                Directory.Delete(OutputDirectory, true);
            }
            catch (IOException)
            {
                // Ignore
            }
        }
    }
}
