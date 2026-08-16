using System.Collections.Concurrent;
using BLight.Blare.Audio.Boost;

namespace BLight.Blare.Audio.Analysis;

/// <summary>
/// Runs a per-process loopback capture per watched app and feeds it through
/// a <see cref="SpectrumAnalyzer"/>, so the UI can read live frequency bands
/// for each app.
///
/// This is genuinely not free: each watched app means one live WASAPI
/// capture stream plus an FFT per frame. Callers should watch only what's
/// actually on screen and call <see cref="StopAll"/> when the view is hidden.
/// </summary>
public sealed class SpectrumMonitor : IDisposable
{
    private readonly ConcurrentDictionary<uint, WatchState> _watches = new();
    private readonly int _bandCount;

    public SpectrumMonitor(int bandCount = 14)
    {
        _bandCount = bandCount;
    }

    public int BandCount => _bandCount;

    /// <summary>Begins watching a process. Safe to call repeatedly for an already-watched process.</summary>
    public void Watch(uint processId)
    {
        _watches.GetOrAdd(processId, StartWatch);
    }

    /// <summary>Copies the latest band levels (0..1) for a process into <paramref name="destination"/>. Returns false when that process isn't being watched yet.</summary>
    public bool TryGetBands(uint processId, Span<double> destination)
    {
        if (!_watches.TryGetValue(processId, out var watch))
        {
            return false;
        }

        lock (watch.Gate)
        {
            var bands = watch.Analyzer.Bands;
            var count = Math.Min(destination.Length, bands.Length);
            bands[..count].CopyTo(destination);

            // Nothing arrived since the last read — let the bars fall rather
            // than freeze at their last value.
            if (!watch.ReceivedSinceLastRead)
            {
                watch.Analyzer.Decay();
            }

            watch.ReceivedSinceLastRead = false;
        }

        return true;
    }

    public void Stop(uint processId)
    {
        if (_watches.TryRemove(processId, out var watch))
        {
            watch.Cancellation.Cancel();
            watch.Cancellation.Dispose();
        }
    }

    public void StopAll()
    {
        foreach (var processId in _watches.Keys.ToList())
        {
            Stop(processId);
        }
    }

    public void Dispose() => StopAll();

    private WatchState StartWatch(uint processId)
    {
        var watch = new WatchState(new SpectrumAnalyzer(bandCount: _bandCount), new CancellationTokenSource());

        _ = Task.Run(async () =>
        {
            try
            {
                var capture = new ProcessLoopbackCapture();
                await capture.RunAsync(
                    processId,
                    block =>
                    {
                        lock (watch.Gate)
                        {
                            watch.Analyzer.AddSamples(block);
                            watch.ReceivedSinceLastRead = true;
                        }
                    },
                    watch.Cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                // expected when the watch is stopped
            }
            catch (Exception)
            {
                // A process can exit, refuse capture, or be protected — a dead
                // visualiser must never take the app down with it.
                _watches.TryRemove(processId, out _);
            }
        });

        return watch;
    }

    private sealed class WatchState(SpectrumAnalyzer analyzer, CancellationTokenSource cancellation)
    {
        public SpectrumAnalyzer Analyzer { get; } = analyzer;

        public CancellationTokenSource Cancellation { get; } = cancellation;

        public object Gate { get; } = new();

        public bool ReceivedSinceLastRead { get; set; }
    }
}
