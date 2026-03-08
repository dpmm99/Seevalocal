using Xunit;

namespace Seevalocal.Pipelines.Tests;

public sealed class PipelineRegistryTests
{
    private readonly PipelineRegistry _registry = PipelineRegistry.CreateDefault(TestHelpers.LoggerFactory);

    [Theory]
    [InlineData("Translation")]
    [InlineData("CSharpCoding")]
    [InlineData("CasualQA")]
    public void AllThreeBuiltInPipelinesAreRegistered(string name)
    {
        var factory = _registry.Get(name);
        Assert.Equal(name, factory.PipelineName);
    }

    [Fact]
    public void Get_UnknownName_ThrowsInvalidOperationWithHelpfulMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => _registry.Get("NotARealPipeline"));
        Assert.Contains("NotARealPipeline", ex.Message);
        Assert.Contains("Known pipelines", ex.Message);
    }

    [Fact]
    public void TryGet_KnownName_ReturnsTrueAndFactory()
    {
        var found = _registry.TryGet("CasualQA", out var factory);
        Assert.True(found);
        Assert.NotNull(factory);
    }

    [Fact]
    public void TryGet_UnknownName_ReturnsFalse()
    {
        var found = _registry.TryGet("Bogus", out var factory);
        Assert.False(found);
        Assert.Null(factory);
    }

    [Fact]
    public void All_ReturnsThreeFactories()
    {
        Assert.Equal(3, _registry.All.Count);
    }

    [Fact]
    public void Get_IsCaseInsensitive()
    {
        var f1 = _registry.Get("translation");
        var f2 = _registry.Get("TRANSLATION");
        Assert.Equal(f1.PipelineName, f2.PipelineName);
    }
}
