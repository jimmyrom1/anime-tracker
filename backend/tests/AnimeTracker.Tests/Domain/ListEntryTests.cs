using AnimeTracker.Domain;

namespace AnimeTracker.Tests.Domain;

public class ListEntryTests
{
    private static readonly DateOnly Today = new(2026, 9, 27);

    private static ListEntry NewEntry() => new() { UserId = "u1" };

    [Fact]
    public void Watching_the_first_episode_of_a_planned_show_starts_it()
    {
        var entry = NewEntry();

        var advanced = entry.SetProgress(1, total: 28, Today);

        Assert.Equal(ListStatus.Current, entry.Status);
        Assert.Equal(Today, entry.StartedOn);
        Assert.Equal(1, advanced);
    }

    [Fact]
    public void Reaching_the_last_episode_completes_it()
    {
        var entry = NewEntry();
        entry.SetProgress(27, 28, Today.AddDays(-30));

        entry.Increment(28, Today);

        Assert.Equal(ListStatus.Completed, entry.Status);
        Assert.Equal(28, entry.Progress);
        Assert.Equal(Today.AddDays(-30), entry.StartedOn);
        Assert.Equal(Today, entry.FinishedOn);
    }

    [Fact]
    public void Progress_cannot_go_past_the_total_or_below_zero()
    {
        var entry = NewEntry();
        Assert.Equal("out_of_range", Assert.Throws<DomainException>(() => entry.SetProgress(29, 28, Today)).Code);
        Assert.Equal("negative", Assert.Throws<DomainException>(() => entry.SetProgress(-1, 28, Today)).Code);
        Assert.Equal(0, entry.Progress);
    }

    [Fact]
    public void Airing_shows_without_a_known_total_accept_any_progress()
    {
        var entry = NewEntry();
        entry.SetProgress(1200, total: null, Today); // One Piece

        Assert.Equal(ListStatus.Current, entry.Status);
        Assert.Equal(1200, entry.Progress);
    }

    [Fact]
    public void Marking_as_completed_fills_the_progress_and_dates()
    {
        var entry = NewEntry();
        entry.SetStatus(ListStatus.Completed, total: 12, Today);

        Assert.Equal(12, entry.Progress);
        Assert.Equal(Today, entry.StartedOn);
        Assert.Equal(Today, entry.FinishedOn);
    }

    [Fact]
    public void Paused_or_dropped_shows_resume_when_progress_moves()
    {
        var entry = NewEntry();
        entry.SetProgress(5, 12, Today);
        entry.SetStatus(ListStatus.Dropped, 12, Today);

        entry.Increment(12, Today);

        Assert.Equal(ListStatus.Current, entry.Status);
        Assert.Equal(6, entry.Progress);
    }

    [Fact]
    public void Lowering_the_progress_of_a_completed_show_is_a_correction_not_a_rewatch()
    {
        var entry = NewEntry();
        entry.SetStatus(ListStatus.Completed, 12, Today);

        var advanced = entry.SetProgress(11, 12, Today);

        Assert.Equal(ListStatus.Current, entry.Status);
        Assert.Equal(0, entry.RepeatCount);
        Assert.Equal(0, advanced);
    }

    [Fact]
    public void Rewatching_is_explicit_and_counted()
    {
        var entry = NewEntry();
        Assert.Throws<DomainException>(entry.StartRewatch);

        entry.SetStatus(ListStatus.Completed, 12, Today);
        entry.StartRewatch();

        Assert.Equal(1, entry.RepeatCount);
        Assert.Equal(0, entry.Progress);
        Assert.Equal(ListStatus.Current, entry.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Scores_go_from_1_to_10(int score)
    {
        var entry = NewEntry();
        Assert.Throws<DomainException>(() => entry.SetScore(score));
        entry.SetScore(10);
        entry.SetScore(null);
        Assert.Null(entry.Score);
    }

    [Fact]
    public void Platform_is_trimmed_and_blank_means_none()
    {
        var entry = NewEntry();
        entry.SetPlatform("  Crunchyroll ");
        Assert.Equal("Crunchyroll", entry.Platform);
        entry.SetPlatform("   ");
        Assert.Null(entry.Platform);
        Assert.Throws<DomainException>(() => entry.SetPlatform(new string('x', 41)));
    }

    [Fact]
    public void Dates_must_make_sense()
    {
        var entry = NewEntry();
        Assert.Equal("in_the_future", Assert.Throws<DomainException>(() => entry.SetDates(Today.AddDays(1), null, Today)).Code);
        Assert.Equal("before_start", Assert.Throws<DomainException>(() => entry.SetDates(Today, Today.AddDays(-1), Today)).Code);
        entry.SetDates(Today.AddDays(-10), Today, Today);
        Assert.Equal(Today, entry.FinishedOn);
    }
}
