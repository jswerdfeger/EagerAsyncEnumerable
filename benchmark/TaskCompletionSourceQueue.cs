// Copyright (c) 2023 James Swerdfeger
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Benchmark;

/// <summary>
/// This implementation loads all elements from the source enumerable, immediately, into a
/// <see cref="Queue{T}"/>, using lock for thread safety. When the consumer is waiting for items,
/// it will create a <see cref="TaskCompletionSource{T}"/>.
/// </summary>
[SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly",
	Justification = "Loading is performed in the background and never directly awaited.")]
internal class TaskCompletionSourceQueue<T> : IAsyncEnumerator<T>
{
	private readonly Queue<T> _queue;
	private TaskCompletionSource<bool>? _tcs;
	private bool _completed = false;

	public T Current { get; private set; }

	public TaskCompletionSourceQueue(IAsyncEnumerable<T> source)
	{
		Current = default!;
		_queue = new();

		_ = ReadAll(source);
	}

	private async ValueTask ReadAll(IAsyncEnumerable<T> source)
	{
		var queue = _queue;
		await using var enumerator = source.GetAsyncEnumerator();
		bool hasMore = false;
		do
		{
			hasMore = await enumerator.MoveNextAsync().ConfigureAwait(false);
			TaskCompletionSource<bool>? tcs = null;
			lock (queue)
			{
				if (hasMore) queue.Enqueue(enumerator.Current);
				tcs = _tcs;

				// Important that, if there is a tcs, we clear it, so that if the consumer is
				// slow, the producer (this thread) won't try and reuse the same tcs.
				_tcs = null;
			}

			tcs?.SetResult(hasMore);
		} while (hasMore);

		_completed = true;
	}

	public ValueTask DisposeAsync()
	{
		return default;
	}

	public async ValueTask<bool> MoveNextAsync()
	{
		var queue = _queue;
		T item = default!;
		TaskCompletionSource<bool> tcs = null!;

		lock (queue)
		{
			if (queue.TryDequeue(out item!)) return SetCurrent(item);
			else if (_completed) return SetCurrent();
			else
			{
				_tcs = tcs = new TaskCompletionSource<bool>();
			}
		}

		bool hasMore = await tcs.Task.ConfigureAwait(false);
		if (!hasMore) return SetCurrent();

		lock (queue)
		{
			if (!queue.TryDequeue(out item!))
			{
				throw new InvalidOperationException("Failed to dequeue after TaskCompletionSource reported success!");
			}
		}

		return SetCurrent(item);
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
