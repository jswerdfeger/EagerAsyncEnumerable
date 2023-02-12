// Copyright (c) 2023 James Swerdfeger
// Licensed under the MIT license.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EagerAsyncEnumerable;

/// <summary>
/// Provides extensions on <see cref="IEnumerable{Task{T}}"/> to obtain an eager-loading
/// implementation of <see cref="IAsyncEnumerable{T}"/> and <see cref="IAsyncEnumerator{T}"/>.
/// </summary>
public static class IEnumerableTaskT
{
	/// <summary>
	/// Returns this <see cref="IEnumerable{Task{T}}"/> as an eager-loading enumerable, which,
	/// in the background, eagerly advances to the next element before you request it, allowing
	/// you to do other work in parallel.
	/// </summary>
	public static IAsyncEnumerable<T> AsEagerEnumerable<T>(this IEnumerable<Task<T>> enumerable)
	{
		return new EagerEnumerable<T>(enumerable);
	}

	/// <summary>
	/// Returns an enumerator that, in the background, eagerly advances to the next element before
	/// you request it, allowing you to do other work in parallel.
	/// </summary>
	public static IAsyncEnumerator<T> GetEagerEnumerator<T>(this IEnumerable<Task<T>> enumerable,
		CancellationToken cancellationToken = default)
	{
		return new EagerEnumerator<T>(enumerable, cancellationToken);
	}


	private class EagerEnumerable<T> : IAsyncEnumerable<T>
	{
		private readonly IEnumerable<Task<T>> _enumerable;

		public EagerEnumerable(IEnumerable<Task<T>> enumerable)
		{
			_enumerable = enumerable;
		}

		public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
		{
			return new EagerEnumerator<T>(_enumerable, cancellationToken);
		}
	}

	private class EagerEnumerator<T> : EagerEnumeratorBase<T>
	{
		private readonly IEnumerator<Task<T>> _source;
		protected override ValueTask ReadAllTask { get; }

		private readonly Queue<T> _queue;

		public EagerEnumerator(IEnumerable<Task<T>> source, CancellationToken cancellationToken)
			: base(cancellationToken)
		{
			_queue = new();
			_source = source.GetEnumerator();
			ReadAllTask = ReadAll();
		}

		public override async ValueTask DisposeAsync()
		{
			await base.DisposeAsync();
			_source.Dispose();
		}

		protected override async ValueTask<(bool HasMore, T Item)> MoveEnumerator()
		{
			var source = _source;
			bool hasMore = source.MoveNext();
			T item = (hasMore ? await source.Current.ConfigureAwait(false) : default!);
			return (hasMore, item);
		}

		protected override void Enqueue(T item)
			=> _queue.Enqueue(item);

		protected override bool TryDequeue(out T item)
			=> _queue.TryDequeue(out item);
	}

}
