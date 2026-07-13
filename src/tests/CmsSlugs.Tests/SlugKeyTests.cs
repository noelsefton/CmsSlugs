using Shouldly;
using CmsSlugs;
using Xunit;

namespace CmsSlugs.Tests;

public class SlugKeyTests
{
    [Theory]
    [InlineData("/Path/Slug/", "path/slug")]
    [InlineData("  SLUG  ", "slug")]
    [InlineData("Foo/Bar", "foo/bar")]
    [InlineData("", "")]
    public void NormalizeSlug_trims_strips_slashes_and_lowercases(string input, string expected)
        => SlugKey.NormalizeSlug(input).ShouldBe(expected);

    [Fact]
    public void Compose_builds_culture_pipe_slug()
        => SlugKey.Compose("/Blue-Widget/", "EN").ShouldBe("en|blue-widget");

    [Fact]
    public void Compose_handles_missing_culture()
        => SlugKey.Compose("slug", null).ShouldBe("|slug");

    [Fact]
    public void Reads_and_writes_agree_on_the_same_key()
    {
        var write = SlugKey.Compose("/A/B/", "en-US");
        var read = SlugKey.Compose("a/b", "EN-us");
        read.ShouldBe(write);
    }
}
