// Copyright (c) 2023 James Swerdfeger
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace Benchmark;

/// <summary>
/// This implementation loads all elements from the source enumerable, immediately, into a
/// <see cref="Queue{T}"/>, using lock for thread safety. When the consumer is waiting for items,
/// it will create a <see cref="ValueTask{TResult}"/> by way of a custom
/// <see cref="IValueTaskSource{TResult}"/> backed by
/// <see cref="ManualResetValueTaskSourceCore{T}"/>.
/// </summary>
[SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly",
	Justification = "Loading is performed in the background and never directly awaited.")]
internal class ValueTaskSourceQueue<T> : IAsyncEnumerator<T>
{
	private class EnqueueWaiter : IValueTaskSource<bool>
	{
		// Rem: This has to be mutable in order to work, but if you state the field is readonly,
		// C# prevents it from being mutable, which will break the program for sure.
		private ManualResetValueTaskSourceCore<bool> _source;
		private bool _awaiting = false;

		public EnqueueWaiter()
		{
			_source = new();
		}

		public void SetResult(bool result)
		{
			if (!_awaiting) return;

			// Important we clear _awaiting before we set the result. The continuation could
			// execute synchronously, and a really fast consumer could create another task before
			// we move even one line.
			_awaiting = false;

			// Important to not call Reset immediately. Reset is immediately going to adjust the
			// token-tracking Version, but the task out there is going to have the previous value.
			// It needs a chance to call GetResult *before* we reset. Hence why we reset only
			// just before creating a new value task.
			_source.SetResult(result);
		}
		public ValueTask<bool> CreateTask()
		{
			_awaiting = true;
			_source.Reset();
			return new ValueTask<bool>(this, _source.Version);
		}

		ValueTaskSourceStatus IValueTaskSource<bool>.GetStatus(short token)
			=> _source.GetStatus(token);

		void IValueTaskSource<bool>.OnCompleted(Action<object?> continuation, object? state,
			short token, ValueTaskSourceOnCompletedFlags flags)
			=> _source.OnCompleted(continuation, state, token, flags);

		bool IValueTaskSource<bool>.GetResult(short token)
			=> _source.GetResult(token);
	}

	private readonly Queue<T> _queue;
	private readonly EnqueueWaiter _enqueueWaiter;
	private bool _completed = false;

	public T Current { get; private set; }

	public ValueTaskSourceQueue(IAsyncEnumerable<T> source)
	{
		Current = default!;
		_queue = new();
		_enqueueWaiter = new();

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
			lock (queue)
			{
				if (hasMore)
				{
					queue.Enqueue(enumerator.Current);
				}
				_enqueueWaiter.SetResult(hasMore);
			}
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
		ValueTask<bool> task;

		lock (queue)
		{
			if (queue.TryDequeue(out item!)) return SetCurrent(item);
			else if (_completed) return SetCurrent();
			else
			{
				task = _enqueueWaiter.CreateTask();
			}
		}

		bool hasMore = await task.ConfigureAwait(false);
		if (!hasMore) return SetCurrent();

		lock (queue)
		{
			if (!queue.TryDequeue(out item!))
			{
				throw new InvalidOperationException("Failed to dequeue after our task reported success!");
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
