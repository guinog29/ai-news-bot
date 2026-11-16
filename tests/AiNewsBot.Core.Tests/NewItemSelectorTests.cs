namespace AiNewsBot.Core.Tests;

public class NewItemSelectorTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly BotOptions Options = new() { ConsiderLatest = 5, MaxSeenPerSource = 20, MaxPostsPerSourcePerRun = 3 };

    private static FeedItem Item(string id, int day) =>
        new(id, "src", "official", $"Title {id}", new Uri($"https://example.com/{id}"), Start.AddDays(day));

    private static SourceState Seeded(params string[] seen)
    {
        var state = new SourceState { Seeded = true };
        state.SeenIds.AddRange(seen);
        return state;
    }

    [Fact]
    public void Missing_state_seeds_without_posting()
    {
        var result = NewItemSelector.Select([Item("a", 1), Item("b", 2)], null, Options);

        Assert.True(result.IsSeed);
        Assert.Empty(result.ToPublish);
        Assert.Equal(["b", "a"], result.Considered.Select(i => i.Id));
    }

    [Fact]
    public void An_unseeded_state_entry_is_also_a_seed()
    {
        var result = NewItemSelector.Select([Item("a", 1)], new SourceState(), Options);

        Assert.True(result.IsSeed);
        Assert.Empty(result.ToPublish);
    }

    [Fact]
    public void New_items_are_returned_oldest_first_regardless_of_feed_order()
    {
        var result = NewItemSelector.Select([Item("c", 3), Item("a", 1), Item("b", 2)], Seeded("old"), Options);

        Assert.False(result.IsSeed);
        Assert.Equal(["a", "b", "c"], result.ToPublish.Select(i => i.Id));
    }

    [Fact]
    public void Seen_items_are_not_returned_again()
    {
        var result = NewItemSelector.Select([Item("a", 1), Item("b", 2), Item("c", 3)], Seeded("a", "b"), Options);

        Assert.Equal(["c"], result.ToPublish.Select(i => i.Id));
    }

    [Fact]
    public void Duplicates_inside_one_fetch_count_once()
    {
        var result = NewItemSelector.Select([Item("a", 1), Item("a", 1)], Seeded(), Options);

        Assert.Single(result.ToPublish);
    }

    [Fact]
    public void The_cap_keeps_the_newest_and_reports_the_rest_as_skipped()
    {
        var items = Enumerable.Range(1, 5).Select(d => Item($"i{d}", d));

        var result = NewItemSelector.Select(items, Seeded(), Options);

        Assert.Equal(["i3", "i4", "i5"], result.ToPublish.Select(i => i.Id));
        Assert.Equal(["i1", "i2"], result.Skipped.Select(i => i.Id));
    }

    [Fact]
    public void Only_the_newest_window_is_considered_so_old_items_of_huge_feeds_never_resurface()
    {
        var items = Enumerable.Range(1, 40).Select(d => Item($"i{d}", d));

        var result = NewItemSelector.Select(items, Seeded("i40", "i39", "i38", "i37", "i36"), Options);

        Assert.Empty(result.ToPublish);
        Assert.Equal(5, result.Considered.Count);
    }

    [Fact]
    public void Items_without_a_date_are_treated_as_the_newest_and_keep_feed_order()
    {
        var undated1 = Item("u1", 0) with { PublishedAt = null };
        var undated2 = Item("u2", 0) with { PublishedAt = null };

        // A feed lists newest first, so u1 (listed first) is newer than u2 and must be posted last.
        var result = NewItemSelector.Select([undated1, undated2, Item("dated", 1)], Seeded(), Options);

        Assert.Equal(["dated", "u2", "u1"], result.ToPublish.Select(i => i.Id));
    }
}
