using WfWasm.CanvasHost;
using Xunit;

namespace WfWasm.CanvasHost.Tests;

public sealed class AppAssemblyLoaderPrefetchTelemetryTests
{
    [Fact]
    public void Empty_Snapshot_Returns_Zero_And_Default_Values()
    {
        var empty = PrefetchTelemetrySnapshot.Empty;
        Assert.Equal(0, empty.TotalPrefetches);
        Assert.Equal(0, empty.SuccessfulPrefetches);
        Assert.Equal(0, empty.FailedPrefetches);
        Assert.Equal(0, empty.CacheHits);
        Assert.Equal(0, empty.CacheMisses);
        Assert.Equal(0, empty.TotalBytesPrefetched);
        Assert.Equal(0.0, empty.TotalLatencyMs);
        Assert.Equal(0.0, empty.AverageLatencyMs);
        Assert.Equal(0.0, empty.MinLatencyMs);
        Assert.Equal(0.0, empty.MaxLatencyMs);
        Assert.Equal(0.0, empty.CacheHitRate);
    }

    [Fact]
    public void PrefetchTiming_Records_Latency_Min_Max_Average_And_Bytes()
    {
        string app = "testapp-" + Guid.NewGuid().ToString("N");
        AppAssemblyLoader.ResetPrefetchTelemetry(app);

        AppAssemblyLoader.RecordPrefetchTimingForTest(app, 10.0, 1024, true);
        AppAssemblyLoader.RecordPrefetchTimingForTest(app, 30.0, 2048, true);
        AppAssemblyLoader.RecordPrefetchTimingForTest(app, 20.0, 4096, true);

        var snapshot = AppAssemblyLoader.GetPrefetchTelemetry(app);
        Assert.Equal(3, snapshot.TotalPrefetches);
        Assert.Equal(3, snapshot.SuccessfulPrefetches);
        Assert.Equal(0, snapshot.FailedPrefetches);
        Assert.Equal(7168, snapshot.TotalBytesPrefetched);
        Assert.Equal(60.0, snapshot.TotalLatencyMs);
        Assert.Equal(20.0, snapshot.AverageLatencyMs);
        Assert.Equal(10.0, snapshot.MinLatencyMs);
        Assert.Equal(30.0, snapshot.MaxLatencyMs);
    }

    [Fact]
    public void PrefetchTiming_Separates_Failed_And_Successful_Prefetches()
    {
        string app = "testapp-" + Guid.NewGuid().ToString("N");
        AppAssemblyLoader.ResetPrefetchTelemetry(app);

        AppAssemblyLoader.RecordPrefetchTimingForTest(app, 15.0, 2000, true);
        AppAssemblyLoader.RecordPrefetchTimingForTest(app, 5.0, 0, false); // failed prefetch
        AppAssemblyLoader.RecordPrefetchTimingForTest(app, 25.0, 4000, true);

        var snapshot = AppAssemblyLoader.GetPrefetchTelemetry(app);
        Assert.Equal(3, snapshot.TotalPrefetches);
        Assert.Equal(2, snapshot.SuccessfulPrefetches);
        Assert.Equal(1, snapshot.FailedPrefetches);
        Assert.Equal(6000, snapshot.TotalBytesPrefetched);
        Assert.Equal(40.0, snapshot.TotalLatencyMs);
        Assert.Equal(20.0, snapshot.AverageLatencyMs);
    }

    [Fact]
    public void CacheHit_Tracking_Calculates_Exact_HitRate()
    {
        string app = "testapp-" + Guid.NewGuid().ToString("N");
        AppAssemblyLoader.ResetPrefetchTelemetry(app);

        AppAssemblyLoader.RecordResolutionHitForTest(app, true);
        AppAssemblyLoader.RecordResolutionHitForTest(app, true);
        AppAssemblyLoader.RecordResolutionHitForTest(app, true);
        AppAssemblyLoader.RecordResolutionHitForTest(app, false);

        var snapshot = AppAssemblyLoader.GetPrefetchTelemetry(app);
        Assert.Equal(3, snapshot.CacheHits);
        Assert.Equal(1, snapshot.CacheMisses);
        Assert.Equal(0.75, snapshot.CacheHitRate); // 3 / 4 = 75%
    }

    [Fact]
    public void PerApp_Telemetry_Isolation()
    {
        string app1 = "app1-" + Guid.NewGuid().ToString("N");
        string app2 = "app2-" + Guid.NewGuid().ToString("N");

        AppAssemblyLoader.ResetPrefetchTelemetry(app1);
        AppAssemblyLoader.ResetPrefetchTelemetry(app2);

        AppAssemblyLoader.RecordPrefetchTimingForTest(app1, 10.0, 1000, true);
        AppAssemblyLoader.RecordResolutionHitForTest(app1, true);

        AppAssemblyLoader.RecordPrefetchTimingForTest(app2, 50.0, 5000, true);
        AppAssemblyLoader.RecordResolutionHitForTest(app2, false);

        var snap1 = AppAssemblyLoader.GetPrefetchTelemetry(app1);
        var snap2 = AppAssemblyLoader.GetPrefetchTelemetry(app2);

        Assert.Equal(1, snap1.TotalPrefetches);
        Assert.Equal(1000, snap1.TotalBytesPrefetched);
        Assert.Equal(10.0, snap1.TotalLatencyMs);
        Assert.Equal(1, snap1.CacheHits);
        Assert.Equal(0, snap1.CacheMisses);
        Assert.Equal(1.0, snap1.CacheHitRate);

        Assert.Equal(1, snap2.TotalPrefetches);
        Assert.Equal(5000, snap2.TotalBytesPrefetched);
        Assert.Equal(50.0, snap2.TotalLatencyMs);
        Assert.Equal(0, snap2.CacheHits);
        Assert.Equal(1, snap2.CacheMisses);
        Assert.Equal(0.0, snap2.CacheHitRate);
    }

    [Fact]
    public void ResetPrefetchTelemetry_Clears_PerApp()
    {
        string app = "testapp-" + Guid.NewGuid().ToString("N");
        AppAssemblyLoader.ResetPrefetchTelemetry(app);

        AppAssemblyLoader.RecordPrefetchTimingForTest(app, 25.0, 5000, true);
        AppAssemblyLoader.RecordResolutionHitForTest(app, true);

        var before = AppAssemblyLoader.GetPrefetchTelemetry(app);
        Assert.Equal(1, before.TotalPrefetches);
        Assert.Equal(1, before.CacheHits);

        AppAssemblyLoader.ResetPrefetchTelemetry(app);
        var after = AppAssemblyLoader.GetPrefetchTelemetry(app);
        Assert.Equal(0, after.TotalPrefetches);
        Assert.Equal(0, after.CacheHits);
        Assert.Equal(0, after.TotalBytesPrefetched);
    }

    [Fact]
    public void ThreadSafety_Concurrent_Telemetry_Recordings()
    {
        string app = "conctest-" + Guid.NewGuid().ToString("N");
        AppAssemblyLoader.ResetPrefetchTelemetry(app);

        const int threadCount = 10;
        const int opsPerThread = 50;

        Parallel.For(0, threadCount, _ =>
        {
            for (int i = 0; i < opsPerThread; i++)
            {
                AppAssemblyLoader.RecordPrefetchTimingForTest(app, 5.0, 100, true);
                AppAssemblyLoader.RecordResolutionHitForTest(app, i % 2 == 0);
            }
        });

        var snapshot = AppAssemblyLoader.GetPrefetchTelemetry(app);
        Assert.Equal(threadCount * opsPerThread, snapshot.TotalPrefetches);
        Assert.Equal(threadCount * opsPerThread, snapshot.SuccessfulPrefetches);
        Assert.Equal((long)threadCount * opsPerThread * 100, snapshot.TotalBytesPrefetched);
        Assert.Equal(threadCount * opsPerThread / 2, snapshot.CacheHits);
        Assert.Equal(threadCount * opsPerThread / 2, snapshot.CacheMisses);
        Assert.Equal(0.5, snapshot.CacheHitRate);
    }
}
