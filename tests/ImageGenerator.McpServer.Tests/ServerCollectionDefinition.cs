namespace ImageGenerator.McpServer;

// Start a single server per test process and share it across test classes
[CollectionDefinition(Name)]
public sealed class ServerCollectionDefinition : ICollectionFixture<TestApplicationFactory>
{
    public const string Name = "Server";
}
