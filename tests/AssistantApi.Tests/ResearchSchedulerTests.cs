using AssistantApi.Instagram;
using AssistantApi.Research;
using AssistantApi.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssistantApi.Tests;

public sealed class ResearchSchedulerTests
{
    private sealed class FakeTokenStore : IInstagramTokenStore
    {
        public bool HasToken { get; set; } = true;

        public bool TryGetAccessToken(out string accessToken)
        {
            if (!HasToken)
            {
                accessToken = string.Empty;
                return false;
            }

            accessToken = "test-token-not-real";
            return true;
        }
    }

    private sealed class CountingCapture : IInstagramResearchCapture
    {
        public int Calls { get; private set; }
        public InstagramFetchStatus Status { get; set; } = InstagramFetchStatus.Ok;
        public string? ErrorCode { get; set; }
        public bool SaveArtifacts { get; set; } = true;

        public Task<ResearchCaptureResult> CaptureAsync(
            string userId,
            string? conversationId = null,
            string? traceId = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ResearchCaptureResult
            {
                SnapshotSaved = SaveArtifacts && Status == InstagramFetchStatus.Ok,
                PlanSaved = SaveArtifacts && Status == InstagramFetchStatus.Ok,
                EpisodeSaved = SaveArtifacts && Status == InstagramFetchStatus.Ok,
                FetchStatus = Status,
                ErrorCode = ErrorCode,
                Message = ErrorCode is null ? null : "soft fail",
                SnapshotId = SaveArtifacts ? 1 : null,
                PlanId = SaveArtifacts ? 1 : null
            });
        }
    }

    private static ResearchSchedulerJob CreateJob(
        IResearchSettingsStore settings,
        IResearchScheduleRunStore runs,
        CountingCapture capture,
        FakeTokenStore tokens) =>
        new(
            settings,
            runs,
            capture,
            tokens,
            new NoOpResearchNotifyHook(),
            NullLogger<ResearchSchedulerJob>.Instance);

    [Fact]
    public void Cadence_math_defaults_to_14_and_advances_next_run()
    {
        Assert.Equal(14, ResearchCadence.NormalizeDays(0));
        Assert.Equal(14, ResearchCadence.NormalizeDays(-3));
        Assert.Equal(1, ResearchCadence.NormalizeDays(1));
        Assert.Equal(90, ResearchCadence.NormalizeDays(999));

        var due = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(due.AddDays(14), ResearchCadence.AdvanceNextRun(due, 0));
        Assert.Equal(due.AddDays(14), ResearchCadence.AdvanceNextRun(due, 14));
        Assert.Equal("2026-08-17", ResearchCadence.PeriodKey(due));
    }

    [Fact]
    public async Task Skip_if_not_due_does_not_capture()
    {
        var settingsStore = new InMemoryResearchSettingsStore();
        var runs = new InMemoryResearchScheduleRunStore();
        var capture = new CountingCapture();
        var tokens = new FakeTokenStore { HasToken = true };
        var now = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

        await settingsStore.UpsertAsync(new ResearchSettings
        {
            UserId = "u1",
            Enabled = true,
            CadenceDays = 14,
            NextRunAt = now.AddDays(3)
        }, CancellationToken.None);

        var job = CreateJob(settingsStore, runs, capture, tokens);
        await job.TickAsync(now, CancellationToken.None);

        Assert.Equal(0, capture.Calls);
        var one = await job.ProcessOneAsync(
            (await settingsStore.GetAsync("u1", CancellationToken.None))!,
            now,
            CancellationToken.None);
        Assert.Equal(ResearchScheduleOutcome.SkippedNotDue, one.Outcome);
        Assert.Equal(0, capture.Calls);
    }

    [Fact]
    public async Task Tick_is_noop_without_token_or_when_disabled()
    {
        var settingsStore = new InMemoryResearchSettingsStore();
        var runs = new InMemoryResearchScheduleRunStore();
        var capture = new CountingCapture();
        var now = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

        await settingsStore.UpsertAsync(new ResearchSettings
        {
            UserId = "u-disabled",
            Enabled = false,
            CadenceDays = 14,
            NextRunAt = now.AddMinutes(-1)
        }, CancellationToken.None);
        await settingsStore.UpsertAsync(new ResearchSettings
        {
            UserId = "u-due",
            Enabled = true,
            CadenceDays = 14,
            NextRunAt = now.AddMinutes(-1)
        }, CancellationToken.None);

        var noToken = CreateJob(settingsStore, runs, capture, new FakeTokenStore { HasToken = false });
        await noToken.TickAsync(now, CancellationToken.None);
        Assert.Equal(0, capture.Calls);

        var withToken = CreateJob(settingsStore, runs, capture, new FakeTokenStore { HasToken = true });
        await withToken.TickAsync(now, CancellationToken.None);
        Assert.Equal(1, capture.Calls); // only enabled+due
    }

    [Fact]
    public async Task Idempotent_second_tick_same_period_does_not_recapture()
    {
        var settingsStore = new InMemoryResearchSettingsStore();
        var runs = new InMemoryResearchScheduleRunStore();
        var capture = new CountingCapture();
        var tokens = new FakeTokenStore { HasToken = true };
        var due = new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero);
        var now = due.AddHours(1);

        await settingsStore.UpsertAsync(new ResearchSettings
        {
            UserId = "u-idem",
            Enabled = true,
            CadenceDays = 14,
            NextRunAt = due
        }, CancellationToken.None);

        var job = CreateJob(settingsStore, runs, capture, tokens);

        var first = await job.ProcessOneAsync(
            (await settingsStore.GetAsync("u-idem", CancellationToken.None))!,
            now,
            CancellationToken.None);
        Assert.Equal(ResearchScheduleOutcome.Captured, first.Outcome);
        Assert.Equal(1, capture.Calls);

        var afterFirst = await settingsStore.GetAsync("u-idem", CancellationToken.None);
        Assert.NotNull(afterFirst);
        Assert.Equal(now, afterFirst!.LastRunAt);
        Assert.Null(afterFirst.LastError);
        Assert.Equal(ResearchCadence.AdvanceNextRun(due, 14), afterFirst.NextRunAt);

        // Force same period again (as if nextRunAt not yet moved / concurrent tick).
        await settingsStore.UpsertAsync(new ResearchSettings
        {
            UserId = "u-idem",
            Enabled = true,
            CadenceDays = 14,
            NextRunAt = due,
            LastRunAt = afterFirst.LastRunAt
        }, CancellationToken.None);

        var second = await job.ProcessOneAsync(
            (await settingsStore.GetAsync("u-idem", CancellationToken.None))!,
            now.AddMinutes(5),
            CancellationToken.None);
        Assert.Equal(ResearchScheduleOutcome.SkippedAlreadyRan, second.Outcome);
        Assert.Equal(1, capture.Calls);
        Assert.True(await runs.HasSuccessfulRunAsync("u-idem", ResearchCadence.PeriodKey(due), CancellationToken.None));
    }

    [Fact]
    public async Task Graph_fail_writes_lastError_keeps_nextRunAt_and_does_not_mark_period()
    {
        var settingsStore = new InMemoryResearchSettingsStore();
        var runs = new InMemoryResearchScheduleRunStore();
        var capture = new CountingCapture
        {
            Status = InstagramFetchStatus.RateLimited,
            ErrorCode = "instagram-rate-limited",
            SaveArtifacts = false
        };
        var tokens = new FakeTokenStore { HasToken = true };
        var due = new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero);
        var now = due.AddHours(1);

        await settingsStore.UpsertAsync(new ResearchSettings
        {
            UserId = "u-fail",
            Enabled = true,
            CadenceDays = 14,
            NextRunAt = due
        }, CancellationToken.None);

        var job = CreateJob(settingsStore, runs, capture, tokens);
        var result = await job.ProcessOneAsync(
            (await settingsStore.GetAsync("u-fail", CancellationToken.None))!,
            now,
            CancellationToken.None);

        Assert.Equal(ResearchScheduleOutcome.Failed, result.Outcome);
        var loaded = await settingsStore.GetAsync("u-fail", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(due, loaded!.NextRunAt);
        Assert.Null(loaded.LastRunAt);
        Assert.Contains("instagram-rate-limited", loaded.LastError, StringComparison.Ordinal);
        Assert.False(await runs.HasSuccessfulRunAsync("u-fail", ResearchCadence.PeriodKey(due), CancellationToken.None));
    }

    [Fact]
    public async Task Hosted_service_tick_exception_does_not_propagate()
    {
        var throwing = new ThrowingJob();
        var hosted = new ResearchSchedulerHostedService(
            throwing,
            Microsoft.Extensions.Options.Options.Create(new ResearchSchedulerOptions
            {
                Enabled = true,
                PollIntervalSeconds = 5
            }),
            NullLogger<ResearchSchedulerHostedService>.Instance);

        using var cts = new CancellationTokenSource();
        var run = hosted.StartAsync(cts.Token);
        await Task.Delay(50);
        cts.Cancel();
        await hosted.StopAsync(CancellationToken.None);
        await run;
        Assert.True(throwing.Calls >= 1);
    }

    private sealed class ThrowingJob : IResearchSchedulerJob
    {
        public int Calls { get; private set; }

        public Task TickAsync(DateTimeOffset now, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("boom");
        }

        public Task<ResearchScheduleTickResult> ProcessOneAsync(
            ResearchSettings settings,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ResearchScheduleTickResult { Outcome = ResearchScheduleOutcome.NoOp });
    }
}
