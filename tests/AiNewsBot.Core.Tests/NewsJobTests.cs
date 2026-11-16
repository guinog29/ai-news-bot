namespace AiNewsBot.Core.Tests;

public class NewsJobTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task First_run_records_items_without_posting()
    {
        var source = new FakeSource("A");
        source.Returns(source.Item("1", 1), source.Item("2", 2));
        var store = new FakeStore();
        var publisher = new FakePublisher();

        var result = await Jobs.Create([source], store, publisher).RunAsync(Ct);

        Assert.Empty(publisher.Published);
        Assert.True(result.Sources[0].Seeded);
        Assert.True(store.State!.Sources["A"].Seeded);
        Assert.Equal(2, store.State.Sources["A"].SeenIds.Count);
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public async Task Second_run_posts_only_new_items_oldest_first()
    {
        var source = new FakeSource("A");
        source.Returns(source.Item("1", 1));
        source.Returns(source.Item("1", 1), source.Item("3", 3), source.Item("2", 2));
        var store = new FakeStore();
        var publisher = new FakePublisher();
        var job = Jobs.Create([source], store, publisher);

        await job.RunAsync(Ct);
        var result = await job.RunAsync(Ct);

        Assert.Equal(["2", "3"], publisher.Published);
        Assert.Equal(2, result.Published);
        Assert.Equal(["3", "2", "1"], store.State!.Sources["A"].SeenIds);
    }

    [Fact]
    public async Task A_third_run_with_nothing_new_posts_nothing()
    {
        var source = new FakeSource("A");
        source.Returns(source.Item("1", 1));
        source.Returns(source.Item("1", 1), source.Item("2", 2));
        source.Returns(source.Item("1", 1), source.Item("2", 2));
        var publisher = new FakePublisher();
        var job = Jobs.Create([source], new FakeStore(), publisher);

        await job.RunAsync(Ct);
        await job.RunAsync(Ct);
        await job.RunAsync(Ct);

        Assert.Equal(["2"], publisher.Published);
    }

    [Fact]
    public async Task A_lost_state_reseeds_instead_of_reposting_everything()
    {
        var source = new FakeSource("A");
        source.Returns(source.Item("1", 1), source.Item("2", 2));
        var publisher = new FakePublisher();

        await Jobs.Create([source], new FakeStore(null), publisher).RunAsync(Ct);

        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task A_new_source_is_seeded_while_existing_ones_keep_posting()
    {
        var a = new FakeSource("A");
        a.Returns(a.Item("1", 1));
        a.Returns(a.Item("1", 1), a.Item("2", 2));
        var b = new FakeSource("B");
        b.Returns(b.Item("x", 1));
        var store = new FakeStore();
        var publisher = new FakePublisher();

        await Jobs.Create([a], store, publisher).RunAsync(Ct);
        await Jobs.Create([a, b], store, publisher).RunAsync(Ct);

        Assert.Equal(["2"], publisher.Published);
        Assert.True(store.State!.Sources["B"].Seeded);
    }

    [Fact]
    public async Task Excess_items_are_dropped_and_not_posted_later()
    {
        var source = new FakeSource("A");
        source.Returns(source.Item("0", 0));
        source.Returns(Enumerable.Range(0, 6).Select(d => source.Item($"{d}", d)).ToArray());
        source.Returns(Enumerable.Range(0, 6).Select(d => source.Item($"{d}", d)).ToArray());
        var publisher = new FakePublisher();
        var job = Jobs.Create([source], new FakeStore(), publisher);

        await job.RunAsync(Ct);
        var second = await job.RunAsync(Ct);
        await job.RunAsync(Ct);

        Assert.Equal(["3", "4", "5"], publisher.Published);
        Assert.Equal(2, second.Sources[0].Dropped);
    }

    [Fact]
    public async Task A_failing_post_is_retried_next_run_and_later_items_wait_to_keep_order()
    {
        var source = new FakeSource("A");
        source.Returns(source.Item("0", 0));
        source.Returns(source.Item("0", 0), source.Item("1", 1), source.Item("2", 2));
        source.Returns(source.Item("0", 0), source.Item("1", 1), source.Item("2", 2));
        var store = new FakeStore();
        var publisher = new FakePublisher();
        var job = Jobs.Create([source], store, publisher);

        await job.RunAsync(Ct);
        publisher.FailOn.Add("1");
        var failed = await job.RunAsync(Ct);
        publisher.FailOn.Clear();
        await job.RunAsync(Ct);

        Assert.False(failed.Sources[0].Succeeded);
        Assert.Equal(["1", "2"], publisher.Published);
        Assert.Equal(0, store.State!.Sources["A"].ConsecutiveFailures);
    }

    [Fact]
    public async Task Zero_items_counts_as_a_failure()
    {
        var source = new FakeSource("A");
        source.Returns();
        var store = new FakeStore();

        var result = await Jobs.Create([source], store, new FakePublisher()).RunAsync(Ct);

        Assert.False(result.Sources[0].Succeeded);
        Assert.Contains("zero items", result.Sources[0].Error, StringComparison.Ordinal);
        Assert.Equal(1, store.State!.Sources["A"].ConsecutiveFailures);
    }

    [Fact]
    public async Task One_failing_source_does_not_stop_the_others_and_state_is_still_saved()
    {
        var bad = new FakeSource("Bad");
        bad.Throws("boom");
        var good = new FakeSource("Good");
        good.Returns(good.Item("1", 1));
        var store = new FakeStore();

        var result = await Jobs.Create([bad, good], store, new FakePublisher()).RunAsync(Ct);

        Assert.Equal(1, result.Failed);
        Assert.False(result.AllFailed);
        Assert.True(result.Sources[1].Succeeded);
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public async Task AllFailed_is_set_when_every_source_fails()
    {
        var a = new FakeSource("A");
        a.Throws("x");
        var b = new FakeSource("B");
        b.Throws("y");

        var result = await Jobs.Create([a, b], new FakeStore(), new FakePublisher()).RunAsync(Ct);

        Assert.True(result.AllFailed);
    }

    [Fact]
    public async Task Alert_goes_out_once_when_the_threshold_is_reached_and_a_recovery_note_follows()
    {
        var source = new FakeSource("A");
        source.Throws("down 1");
        source.Throws("down 2");
        source.Throws("down 3");
        source.Returns(source.Item("1", 1));
        var publisher = new FakePublisher();
        var job = Jobs.Create([source], new FakeStore(), publisher);

        await job.RunAsync(Ct);
        Assert.Empty(publisher.Statuses);
        await job.RunAsync(Ct);
        Assert.Single(publisher.Statuses);
        Assert.Contains("down 2", publisher.Statuses[0], StringComparison.Ordinal);
        await job.RunAsync(Ct);
        Assert.Single(publisher.Statuses);
        await job.RunAsync(Ct);

        Assert.Equal(2, publisher.Statuses.Count);
        Assert.Contains("working again", publisher.Statuses[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_broken_status_channel_does_not_break_the_run()
    {
        var source = new FakeSource("A");
        source.Throws("down");
        source.Throws("down");
        var publisher = new FakePublisher { FailStatus = true };
        var job = Jobs.Create([source], new FakeStore(), publisher);

        await job.RunAsync(Ct);
        var result = await job.RunAsync(Ct);

        Assert.Equal(1, result.Failed);
    }

    [Fact]
    public async Task Sources_removed_from_the_config_are_dropped_from_state()
    {
        var a = new FakeSource("A");
        a.Returns(a.Item("1", 1));
        var b = new FakeSource("B");
        b.Returns(b.Item("1", 1));
        b.Returns(b.Item("1", 1));
        var store = new FakeStore();

        await Jobs.Create([a, b], store, new FakePublisher()).RunAsync(Ct);
        await Jobs.Create([b], store, new FakePublisher()).RunAsync(Ct);

        Assert.Equal(["B"], store.State!.Sources.Keys);
    }

    [Fact]
    public async Task Seen_ids_are_capped()
    {
        var source = new FakeSource("A");
        source.Returns(Enumerable.Range(0, 10).Select(d => source.Item($"{d}", d)).ToArray());
        var store = new FakeStore();
        var options = new BotOptions { ConsiderLatest = 10, MaxSeenPerSource = 10, MaxPostsPerSourcePerRun = 10 };
        var job = new NewsJob([source], store, new FakePublisher(), options, TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<NewsJob>.Instance);
        await job.RunAsync(Ct);
        source.Returns(source.Item("new", 20), source.Item("9", 9));

        await job.RunAsync(Ct);

        var seen = store.State!.Sources["A"].SeenIds;
        Assert.Equal(10, seen.Count);
        Assert.Equal("new", seen[0]);
    }
}
