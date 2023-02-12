// Copyright (c) 2023 James Swerdfeger
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace EagerAsyncEnumerable;

/// <summary>
/// Provides the base class for an eager enumerator over an asynchronous enumerable.
/// </summary>
internal abstract class EagerEnumeratorBase<T> : IAsyncEnumerator<T>
{
	private readonly MoveNextValueTaskSource _valueTaskSource;

	// Normally you wouldn't want to store a value task as a field, as you need to ensure you only
	// await it once. But that's exactly what we do: we only await it in disposal if not complete.
	// In normal operation, it'll run to completion before it hits that point anyway.
	protected abstract ValueTask ReadAllTask { get; }

	// And we store a separate bool for the completion of the ReadAllTask, otherwise a race
	// condition can come up where the consumer creates a new task to wait while the readAllTask
	// is finishing.
	private bool _readAllComplete = false;

	private ExceptionDispatchInfo? _readAllException;
	private readonly CancellationTokenSource _cancellationTokenSource;
	protected CancellationToken CancellationToken => _cancellationTokenSource.Token;

	public T Current { get; private set; }

	protected EagerEnumeratorBase(CancellationToken cancellationToken)
	{
		_valueTaskSource = new();
		Current = default!;

		// It's important we have control of a cancellation token, whether you supplied one or
		// not. This is because an IAsyncEnumerable state machine has built-in protection to not
		// let you call DisposeAsync if it's busy obtaining the next result. It'll throw a
		// NotSupportedException. Hence, if your program raises an unexpected exception, an await
		// using/try...finally block is going to end up calling DisposeAsync while the enumeration
		// is still running. That will cause your raised exception to effectively be replaced by
		// that NotSupportedException, making debugging all but impossible.
		//
		// Hence, we need to be able to cancel the enumeration as part of the DisposeAsync method.
		_cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
	}

	public virtual async ValueTask DisposeAsync()
	{
		// REM: DisposeAsync must be safe to call multiple times, per the MSDN.
		_cancellationTokenSource.Cancel();
		var readAllTask = ReadAllTask;
		if (!readAllTask.IsCompleted)
		{
			await readAllTask.ConfigureAwait(false);
		}
	}

	protected async ValueTask ReadAll()
	{
		var valueTaskSource = _valueTaskSource;
		var token = _cancellationTokenSource.Token;

		bool hasMore;
		T item;
		do
		{
			try
			{
				// Your underlying enumerator may very well not be using any cancellation tokens
				// at all. Should that be the case, the best we can do is check for cancellation
				// between elements.
				token.ThrowIfCancellationRequested();
				var moveResult = await MoveEnumerator().ConfigureAwait(false);
				hasMore = moveResult.HasMore;
				item = moveResult.Item;
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
				if (hasMore) Enqueue(item);
				else _readAllComplete = true;
				valueTaskSource.SetResult(hasMore);
			}
		} while (hasMore);
	}

	protected abstract ValueTask<(bool HasMore, T Item)> MoveEnumerator();
	protected abstract void Enqueue(T item);
	protected abstract bool TryDequeue(out T item);

	public ValueTask<bool> MoveNextAsync()
	{
		_cancellationTokenSource.Token.ThrowIfCancellationRequested();

		var valueTaskSource = _valueTaskSource;
		lock (valueTaskSource)
		{
			_readAllException?.Throw();

			if (TryDequeue(out var item))
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
			if (!TryDequeue(out item))
			{
				throw new InvalidOperationException("Failed to dequeue after our task reported success!");
			}
		}

		Current = item;
	}
}