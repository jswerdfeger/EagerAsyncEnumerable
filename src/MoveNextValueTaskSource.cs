// Copyright (c) 2023 James Swerdfeger
// Licensed under the MIT license.

using System;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace EagerAsyncEnumerable;

/// <summary>
/// Provides a <see cref="IValueTaskSource"/> implementation for when a MoveNext task completes.
/// <remarks>
/// This is for our internal use, only. It is coded with assumptions that do not make it suitable
/// for use outside this library.
/// </remarks>
internal class MoveNextValueTaskSource : IValueTaskSource<bool>
{
	// ManualResetValueTaskSourceCore is a mutable struct, hence we cannot make our field readonly
	// or it would never change and clearly wouldn't work.
	private ManualResetValueTaskSourceCore<bool> _source;

	private Action<bool>? _continuation;

	internal MoveNextValueTaskSource()
	{
		_source = new();
	}

	// Be aware that a race condition exists if two threads call CreateTask and SetResult
	// simultaneously. I take care of this when I use this class.

	/// <summary>
	/// Creates a new <see cref="ValueTask{T}"/>, to listen for when a MoveNext task has
	/// completed. This will synchronously call your supplied continuation function before the
	/// underlying continuation (ie, wherever you await the returned ValueTask.)
	/// </summary>
	public ValueTask<bool> CreateTask(Action<bool> continuation)
	{
		if (_continuation != null)
		{
			throw new InvalidOperationException($"A ValueTask already exists; you must await it before creating a new ValueTask.");
		}

		_continuation = continuation;

		_source.Reset();
		return new ValueTask<bool>(this, _source.Version);
	}

	/// <summary>
	/// Sets the result for a successfully completed MoveNext task.
	/// </summary>
	/// <remarks>This method has no effect if no <see cref="ValueTask{T}"/> has been created.
	/// </remarks>
	public void SetResult(bool result)
	{
		// ManualResetValueTaskSourceCore.SetResult can only be called once per "Reset". That
		// helps prevent the same continuation from being called multiple times. But, since your
		// producer might be very fast, it could possibly call SetResult multiple times before
		// your Consumer processes even one. Thus, I make sure to only continue if another thread
		// is actively awaiting.
		var continuation = _continuation;
		if (continuation == null) return;

		try
		{
			continuation(result);
			_continuation = null;

			// It's important we don't reset, here. Reset is immediately going to adjust the
			// token-tracking Version, but the task out there is going to have the previous token.
			// It needs a chance to call GetResult *before* we reset. Hence why we reset only just
			// before creating a new value task.
			_source.SetResult(result);
		}
		catch (Exception ex)
		{
			SetException(ex);
		}
	}

	/// <summary>
	/// Sets an exception for a task that failed.
	/// </summary>
	/// <remarks>This method has no effect if no <see cref="ValueTask{T}"/> has been created.
	/// </remarks>
	public void SetException(Exception exception)
	{
		if (_continuation == null) return;
		_continuation = null;
		_source.SetException(exception);
	}

	ValueTaskSourceStatus IValueTaskSource<bool>.GetStatus(short token)
		=> _source.GetStatus(token);

	void IValueTaskSource<bool>.OnCompleted(Action<object?> continuation, object? state,
		short token, ValueTaskSourceOnCompletedFlags flags)
		=> _source.OnCompleted(continuation, state, token, flags);

	bool IValueTaskSource<bool>.GetResult(short token)
		=> _source.GetResult(token);

}
