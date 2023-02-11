// Copyright (c) 2023 James Swerdfeger
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace EagerAsyncEnumerable;

/// <summary>
/// Provides extensions on <see cref="IAsyncEnumerable{T}"/> to obtain an eager-loading
/// implementation of <see cref="IAsyncEnumerable{T}"/> and <see cref="IAsyncEnumerator{T}"/>.
/// </summary>
public static class IAsyncEnumerable
{
	/// <summary>
	/// Returns this <see cref="IAsyncEnumerable{T}"/> as an eager-loading enumerable, which,
	/// in the background, eagerly advances to the next element before you request it, allowing
	/// you to do other work in parallel.
	/// </summary>
	public static IAsyncEnumerable<T> AsEagerEnumerable<T>(this IAsyncEnumerable<T> enumerable)
	{
		return new EagerEnumerable<T>(enumerable);
	}

	/// <summary>
	/// Returns an enumerator that, in the background, eagerly advances to the next element before
	/// you request it, allowing you to do other work in parallel.
	/// </summary>
	public static IAsyncEnumerator<T> GetEagerEnumerator<T>(this IAsyncEnumerable<T> enumerable,
		CancellationToken cancellationToken = default)
	{
		return new EagerEnumerator<T>(enumerable, cancellationToken);
	}


	private class EagerEnumerable<T> : IAsyncEnumerable<T>
	{
		private readonly IAsyncEnumerable<T> _enumerable;

		public EagerEnumerable(IAsyncEnumerable<T> enumerable)
		{
			_enumerable = enumerable;
		}

		public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
		{
			return new EagerEnumerator<T>(_enumerable, cancellationToken);
		}
	}

	private class EagerEnumerator<T> : IAsyncEnumerator<T>
	{
		private readonly Queue<T> _queue;
		private readonly MoveNextValueTaskSource _valueTaskSource;
		private readonly IAsyncEnumerator<T> _sourceEnumerator;

		// Normally you wouldn't want to store a value task as a field, as you need to ensure you
		// only await it once. But that's exactly what we do: we only await it in disposal if not
		// complete. In normal operation, it'll run to completion before it hits that point
		// anyway.
		private readonly ValueTask _readAllTask;

		// And we store a separate bool for the completion of the ReadAllTask, otherwise a race
		// condition can come up where the consumer creates a new task to wait while the
		// readAllTask is finishing.
		private bool _readAllComplete = false;

		private ExceptionDispatchInfo? _readAllException;
		private readonly CancellationTokenSource _cancellationTokenSource;
		protected CancellationToken CancellationToken => _cancellationTokenSource.Token;

		public T Current { get; private set; }

		public EagerEnumerator(IAsyncEnumerable<T> sourceEnumberale, CancellationToken cancellationToken)
		{
			_queue = new();
			_valueTaskSource = new();
			Current = default!;

			// It's important we have control of a cancellation token, whether you supplied one or
			// not. This is because an IAsyncEnumerable state machine has built-in protection to
			// not let you call DisposeAsync if it's busy obtaining the next result. It'll throw a
			// NotSupportedException. Hence, if your program raises an unexpected exception, an
			// await using/try...finally block is going to end up calling DisposeAsync while the
			// enumeration is still running. That will cause your raised exception to effectively
			// be replaced by that NotSupportedException, making debugging all but impossible.
			//
			// Hence, we need to be able to cancel the enumeration as part of the DisposeAsync
			// method.
			_cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

			_sourceEnumerator = sourceEnumberale.GetAsyncEnumerator(CancellationToken);
			_readAllTask = ReadAll();
		}

		public virtual async ValueTask DisposeAsync()
		{
			// REM: DisposeAsync must be safe to call multiple times, per the MSDN.
			_cancellationTokenSource.Cancel();
			var readAllTask = _readAllTask;
			if (!readAllTask.IsCompleted)
			{
				await readAllTask.ConfigureAwait(false);
			}
		}

		protected async ValueTask ReadAll()
		{
			var sourceEnumerator = _sourceEnumerator;
			var valueTaskSource = _valueTaskSource;
			var token = _cancellationTokenSource.Token;

			bool hasMore;
			do
			{
				T item = default!;
				try
				{
					// Your underlying enumerator may very well not be using any cancellation
					// tokens at all. Should that be the case, the best we can do is check for
					// cancellation between elements.
					token.ThrowIfCancellationRequested();
					hasMore = await sourceEnumerator.MoveNextAsync().ConfigureAwait(false);
					if (hasMore) item = sourceEnumerator.Current;
					token.ThrowIfCancellationRequested();
				}
				catch (Exception ex)
				{
					// Raising the exception to just the valueTaskSource won't accomplish a thing
					// if the consumer isn't listening. Hence, we also cache it locally.
					lock (valueTaskSource)
					{
						_readAllComplete = true;
						_readAllException = ExceptionDispatchInfo.Capture(ex);
						valueTaskSource.SetException(ex);
					}
					break;
				}

				lock (valueTaskSource)
				{
					if (hasMore) _queue.Enqueue(item);

					else _readAllComplete = true;
					valueTaskSource.SetResult(hasMore);
				}
			} while (hasMore);
		}

		public ValueTask<bool> MoveNextAsync()
		{
			_cancellationTokenSource.Token.ThrowIfCancellationRequested();

			var valueTaskSource = _valueTaskSource;
			lock (valueTaskSource)
			{
				_readAllException?.Throw();

				if (_queue.TryDequeue(out var item))
				{
					Current = item;
					return new ValueTask<bool>(true);
				}
				else if (_readAllComplete)
				{
					Current = default!;
					return new ValueTask<bool>(false);
				}
				else
				{
					return _valueTaskSource.CreateTask(MoveNextContinuation);
				}
			}
		}

		private void MoveNextContinuation(bool result)
		{
			_cancellationTokenSource.Token.ThrowIfCancellationRequested();

			if (!result)
			{
				Current = default!;
				return;
			}

			T item;
			lock (_valueTaskSource)
			{
				if (_queue.TryDequeue(out item))
				{
					throw new InvalidOperationException("Failed to dequeue after our task reported success!");
				}
			}

			Current = item;
		}
	}
}
