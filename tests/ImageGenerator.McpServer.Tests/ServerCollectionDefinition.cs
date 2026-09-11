namespace ImageGenerator.McpServer;

// テストプロセス内でサーバーを1つだけ起動し、全テストクラスで共有する
[CollectionDefinition(Name)]
public sealed class ServerCollectionDefinition : ICollectionFixture<TestApplicationFactory>
{
    public const string Name = "Server";
}
