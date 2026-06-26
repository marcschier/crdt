// Copyright (c) marcschier. Licensed under the MIT License.

#if !NET8_0_OR_GREATER
namespace System.Threading;

/// <summary>
/// Minimal polyfill of <see cref="PeriodicTimer"/> for targets without it (netstandard). Backed by
/// <see cref="Task.Delay(TimeSpan, CancellationToken)"/>; matches the contract used by the transport:
/// returns <see langword="true"/> on each tick, <see langword="false"/> once disposed, and throws
/// <see cref="OperationCanceledException"/> when the supplied token is cancelled.
/// </summary>
internal sealed class PeriodicTimer : IDisposable
{
    private readonly TimeSpan _period;
    private readonly CancellationTokenSource _disposed = new();

    public PeriodicTimer(TimeSpan period)
    {
        if (period <= TimeSpan.Zero && period != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(period));
        }

        _period = period;
    }

    public async ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(_disposed.Token, cancellationToken);
        try
        {
            await Task.Delay(_period, linked.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
            when (_disposed.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public void Dispose() => _disposed.Cancel();
}
#endif
