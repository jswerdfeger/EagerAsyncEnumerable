using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Benchmark;

/// <summary>
/// This implementation loads all elements from the source enumerable, immediately, into a
/// <see cref="ConcurrentQueue{T}"/>. A <see cref="SemaphoreSlim"/> is used to easily provide
/// awaitable tasks for the consumer.
/// </summary>
internal class SemaphoreQueue<T> : IAsyncEnumerator<T>
{
	private readonly ConcurrentQueue<T> _queue;
	private readonly SemaphoreSlim _semaphoreSlim;

	// We need a way to signal the sempahore that the producer is "done", so that we don't end up
	// with a consumer potentially waiting forever for an element that won't come. We use a simple
	// cancellation token to notify any consumers that the producer is complete.
	private readonly CancellationTokenSource _cancellationTokenSource;
	private readonly CancellationToken _cancellationToken;

	public T Current { get; private set; }

	[SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly",
		Justification = "Loading is performed in the background and never directly awaited.")]
	public SemaphoreQueue(IAsyncEnumerable<T> source)
	{
		Current = default!;
		_queue = new();
		_semaphoreSlim = new(0);		
		_cancellationTokenSource = new();
		_cancellationToken = _cancellationTokenSource.Token;

		_ = ReadAll(source);
	}

	private async ValueTask ReadAll(IAsyncEnumerable<T> source)
	{
		await foreach (var item in source)
		{
			_queue.Enqueue(item);
			_semaphoreSlim.Release();
		}

		_cancellationTokenSource.Cancel();
	}

	public ValueTask DisposeAsync()
	{
		return default;
	}

	public async ValueTask<bool> MoveNextAsync()
	{
		// We always have to access the semaphore to ensure that each enqueue perfectly aligns to
		// a dequeue, or there will be too many spots available in the sempahore.
		try
		{
			await _semaphoreSlim.WaitAsync(_cancellationToken).ConfigureAwait(false);
			if (!_queue.TryDequeue(out var item))
			{
				throw new InvalidOperationException("Failed to dequeue after SemaphoreSlim was entered!");
			}

			return SetCurrent(item);
		}
		catch (OperationCanceledException)
		{
			// The semaphore is immediately disposed when adding is done. If we, the consumer, are
			// comparatively slow, there could be a bunch of items still waiting for us to
			// dequeue.
			if (_queue.TryDequeue(out var item)) return SetCurrent(item);
			return SetCurrent();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool SetCurrent()
	{
		Current = default!;
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool SetCurrent(T item)
	{
		Current = item;
		return true;
	}


}
