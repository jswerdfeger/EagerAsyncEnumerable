using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace Benchmark;

/// <summary>
/// This implementation is like CacheOne, only instead of using async and await, it uses a
/// custom <see cref="IValueTaskSource{TResult}"/> backed by
/// <see cref="ManualResetValueTaskSourceCore{T}"/> to set a continuation on the underlying
/// <see cref="ValueTask{TResult}"/>.
/// </summary>
[SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly",
	Justification = "We control the IValueTaskSource.")]
internal class ValueTaskSourceOne<T> : IAsyncEnumerator<T>
{
	private class MoveNextWaiter : IValueTaskSource<bool>
	{
		[SuppressMessage("Style", "IDE0044:Add readonly modifier",
			Justification = "This is a mutable struct. It cannot be made readonly or it will cease to function at all.")]
		private ValueTaskSourceOne<T> _owner;
		private ManualResetValueTaskSourceCore<bool> _source;
		private ValueTaskAwaiter<bool> _awaiter;

		public MoveNextWaiter(ValueTaskSourceOne<T> owner)
		{
			_owner = owner;
			_source = new();
		}

		public ValueTask<bool> WaitFor(ValueTaskAwaiter<bool> awaiter)
		{
			_source.Reset();

			var task = new ValueTask<bool>(this, _source.Version);
			_awaiter = awaiter;
			awaiter.UnsafeOnCompleted(Continuation);
			return task;
		}

		private void Continuation()
		{
			var result = _awaiter.GetResult();
			_owner.OnMoveNextCompleted();
			_source.SetResult(result);
		}

		ValueTaskSourceStatus IValueTaskSource<bool>.GetStatus(short token)
			=> _source.GetStatus(token);

		void IValueTaskSource<bool>.OnCompleted(Action<object?> continuation, object? state,
			short token, ValueTaskSourceOnCompletedFlags flags)
			=> _source.OnCompleted(continuation, state, token, flags);

		bool IValueTaskSource<bool>.GetResult(short token)
			=> _source.GetResult(token);
	}

	private readonly IAsyncEnumerator<T> _enumerator;
	private readonly MoveNextWaiter _moveNextWaiter;
	private ValueTaskAwaiter<bool> _moveNextTaskAwaiter;

	public T Current { get; private set; }

	public ValueTaskSourceOne(IAsyncEnumerable<T> source)
	{
		Current = default!;
		_enumerator = source.GetAsyncEnumerator();

		_moveNextWaiter = new(this);
		_moveNextTaskAwaiter = _enumerator.MoveNextAsync().GetAwaiter();
	}

	public ValueTask DisposeAsync()
	{
		return _enumerator.DisposeAsync();
	}

	private void OnMoveNextCompleted()
	{
		Current = _enumerator.Current;
		_moveNextTaskAwaiter = _enumerator.MoveNextAsync().GetAwaiter();
	}

	public ValueTask<bool> MoveNextAsync()
	{
		var awaiter = _moveNextTaskAwaiter;
		if (awaiter.IsCompleted)
		{
			bool result = awaiter.GetResult();
			OnMoveNextCompleted();
			return new ValueTask<bool>(result);
		}

		return _moveNextWaiter.WaitFor(awaiter);

	}

}
