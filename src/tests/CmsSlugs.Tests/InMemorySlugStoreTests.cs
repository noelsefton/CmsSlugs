using Shouldly;
using CmsSlugs;
using Xunit;

namespace CmsSlugs.Tests;

public class InMemorySlugStoreTests
{
    private static SlugEntry Entry(string slug, string contentId, string culture = "en",
        IReadOnlyDictionary<string, string>? data = null)
        => new(slug, culture, contentId, data);

    [Fact]
    public void Set_then_TryResolve_returns_the_entry()
    {
        var store = new InMemorySlugStore();
        store.Set(Entry("blue-widget", "1|"));

        store.TryResolve("blue-widget", "en", out var entry).ShouldBeTrue();
        entry!.ContentId.ShouldBe("1|");
    }

    [Fact]
    public void TryResolve_is_normalized_lookup()
    {
        var store = new InMemorySlugStore();
        store.Set(Entry("Blue-Widget", "1|"));

        store.TryResolve("/BLUE-WIDGET/", "EN", out var entry).ShouldBeTrue();
        entry!.ContentId.ShouldBe("1|");
    }

    [Fact]
    public void Miss_returns_false_and_null()
    {
        var store = new InMemorySlugStore();
        store.TryResolve("nope", "en", out var entry).ShouldBeFalse();
        entry.ShouldBeNull();
    }

    [Fact]
    public void Data_round_trips()
    {
        var store = new InMemorySlugStore();
        store.Set(Entry("x", "1|", data: new Dictionary<string, string> { ["title"] = "Blue" }));

        store.TryResolve("x", "en", out var entry).ShouldBeTrue();
        entry!.Data!["title"].ShouldBe("Blue");
    }

    [Fact]
    public void Slug_change_leaves_no_ghost_entry()
    {
        var store = new InMemorySlugStore();
        store.Set(Entry("old-slug", "1|"));

        // Re-index content 1 under a new slug, the way the event module does it.
        store.RemoveByContent("1|");
        store.Set(Entry("new-slug", "1|"));

        store.TryResolve("old-slug", "en", out _).ShouldBeFalse();
        store.TryResolve("new-slug", "en", out var entry).ShouldBeTrue();
        entry!.ContentId.ShouldBe("1|");
    }

    [Fact]
    public void RemoveByContent_clears_all_slugs_for_that_content()
    {
        var store = new InMemorySlugStore();
        store.Set(Entry("a", "1|"));
        store.Set(Entry("b", "1|"));
        store.Set(Entry("c", "2|"));

        store.RemoveByContent("1|");

        store.TryResolve("a", "en", out _).ShouldBeFalse();
        store.TryResolve("b", "en", out _).ShouldBeFalse();
        store.TryResolve("c", "en", out _).ShouldBeTrue();
    }

    [Fact]
    public void Collision_last_write_wins_and_is_logged()
    {
        var warnings = new List<string>();
        CmsSlugsLog.OnWarning = warnings.Add;
        try
        {
            var store = new InMemorySlugStore();
            store.Set(Entry("dup", "1|"));
            store.Set(Entry("dup", "2|"));   // same slug+culture, different content

            store.TryResolve("dup", "en", out var entry).ShouldBeTrue();
            entry!.ContentId.ShouldBe("2|");
            warnings.ShouldNotBeEmpty();
        }
        finally
        {
            CmsSlugsLog.OnWarning = null;
        }
    }

    [Fact]
    public void Collision_then_remove_loser_does_not_drop_winner()
    {
        var store = new InMemorySlugStore();
        store.Set(Entry("dup", "1|"));
        store.Set(Entry("dup", "2|"));   // 2 now owns "dup"

        store.RemoveByContent("1|");      // removing the original owner must not drop the key

        store.TryResolve("dup", "en", out var entry).ShouldBeTrue();
        entry!.ContentId.ShouldBe("2|");
    }

    [Fact]
    public void Rebuild_replaces_state_and_marks_ready()
    {
        var store = new InMemorySlugStore();
        store.IsReady.ShouldBeFalse();
        store.Set(Entry("stale", "9|"));

        store.Rebuild(new[] { Entry("fresh", "1|"), Entry("other", "2|") });

        store.IsReady.ShouldBeTrue();
        store.Count.ShouldBe(2);
        store.TryResolve("stale", "en", out _).ShouldBeFalse();
        store.TryResolve("fresh", "en", out _).ShouldBeTrue();
    }

    [Fact]
    public void RequiresBootScan_is_true_for_in_memory()
        => new InMemorySlugStore().RequiresBootScan.ShouldBeTrue();
}
