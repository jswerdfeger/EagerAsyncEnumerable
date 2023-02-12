// Copyright (c) 2023 James Swerdfeger
// Licensed under the MIT license.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EagerAsyncEnumerable;

/// <summary>
/// Provides extensions on <see cref="IEnumerable{Task}"/> to obtain an eager-loading
/// implementation of <see cref="IAsyncEnumerable{bool}"/> and
/// <see cref="IAsyncEnumerator{bool}"/>.
/// </summary>
public static class IEnumerableTask
{
	/// <summary>
	/// Returns this <see cref="IEnumerable{Task}"/> as an eager-loading enumerable, which, in the
	/// background, eagerly advances to the next element before you request it, allowing you to do
	/// other work in parallel.
	/// </summary>
	/// <remarks>
	/// Because there is no typeless IAsyncEnumerable, we just return "true" on each
	/// iteration. You can ignore it.
	/// <para>Of note, this should only be used on deferred enumerables/state machines. If your
	/// source is a concrete implementation like List or Array, all its tasks will already be
	/// running when you call this, so there is no benefit to using this. In such a case, use
	/// Task.WhenAll instead.</para>
	/// </remarks>
	public static IAsyncEnumerable<bool> AsEagerEnumerable(this IEnumerable<Task> enumerable)
	{
		return new EagerEnumerable(enumerable);
	}

	/// <summary>
	/// Returns an enumerator that, in the background, eagerly advances to the next element before
	/// you request it, allowing you to do other work in parallel.
	/// </summary>
	/// <remarks>
	/// Because there is no typeless IAsyncEnumerator, we just return "true" on each
	/// iteration. You can ignore it.
	/// <para>Of note, this should only be used on deferred enumerables/state machines. If your
	/// source is a concrete implementation like List or Array, all its tasks will already be
	/// running when you call this, so there is no benefit to using this. In such a case, use
	/// Task.WhenAll instead.</para>
	/// <para>
	/// Also note that we cannot add a <see cref="CancellationToken"/> to an existing
	/// <see cref="Task"/>. The <paramref name="cancellationToken"/> you supply must be the same
	/// one you used to create all the tasks, or it will only have the capability to cancel
	/// between tasks, not during.
	/// </para>
	/// </remarks>
	public static IAsyncEnumerator<bool> GetEagerEnumerator(this IEnumerable<Task> enumerable,
		CancellationToken cancellationToken = default)
	{
		return new EagerEnumerator(enumerable, cancellationToken);
	}


	private class EagerEnumerable : IAsyncEnumerable<bool>
	{
		private readonly IEnumerable<Task> _enumerable;

		public EagerEnumerable(IEnumerable<Task> enumerable)
		{
			_enumerable = enumerable;
		}

		public IAsyncEnumerator<bool> GetAsyncEnumerator(CancellationToken cancellationToken = default)
		{
			return new EagerEnumerator(_enumerable, cancellationToken);
		}
	}

	private class EagerEnumerator : EagerEnumeratorBase<bool>
	{
		private readonly IEnumerator<Task> _source;
		protected override ValueTask ReadAllTask { get; }

		private int _queued;

		public EagerEnumerator(IEnumerable<Task> source, CancellationToken cancellationToken)
			: base(cancellationToken)
		{
			_queued = 0;
			_source = source.GetEnumerator();
			ReadAllTask = ReadAll();
		}

		public override async ValueTask DisposeAsync()
		{
			await base.DisposeAsync();
			_source.Dispose();
		}

		protected override async ValueTask<(bool HasMore, bool Item)> MoveEnumerator()
		{
			var source = _source;
			bool hasMore = source.MoveNext();
			if (hasMore) await source.Current.ConfigureAwait(false);
			return (hasMore, hasMore);
		}

		protected override void Enqueue(bool _)
			=> _queued++;

		protected override bool TryDequeue(out bool result)
		{
			result = _queued > 0;
			if (!result) return false;

			_queued--;
			return true;
		}
	}

}
