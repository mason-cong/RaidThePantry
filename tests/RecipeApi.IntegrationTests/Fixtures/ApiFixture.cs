namespace RecipeApi.IntegrationTests.Fixtures;

/// <summary>
/// One application and one database for the whole run. Building the host and
/// migrating costs a few seconds, so it is not worth paying per class.
/// </summary>
public class ApiFixture : IAsyncLifetime
{
    public RecipeApiFactory Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Factory = new RecipeApiFactory();
        await Factory.InitializeAsync();
        Client = Factory.CreateClient();
    }

    public Task ResetAsync() => Factory.ResetAsync();

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
    }
}

/// <summary>
/// A single collection, so every test class shares the fixture and xunit runs
/// them sequentially. Parallel classes against one database would race on the
/// reset between them; a database per class would be isolated but pays the
/// migration cost repeatedly. Sequential is the honest trade at this size.
/// </summary>
[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}

/// <summary>
/// Base for test classes: re-seeds before each class so tests never depend on
/// what ran before them.
/// </summary>
[Collection(ApiCollection.Name)]
public abstract class ApiTestBase(ApiFixture fixture) : IAsyncLifetime
{
    protected ApiFixture Fixture { get; } = fixture;
    protected HttpClient Client => Fixture.Client;

    public Task InitializeAsync() => Fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;
}
