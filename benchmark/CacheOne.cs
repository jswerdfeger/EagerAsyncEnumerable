using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Benchmark;

/// <summary>
/// This implementation does not use a queue. It eagerly loads the next element, and will not move
/// again until the result is requested by the consumer.
/// </summary>
[SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly",
	Justification = "_moveNextTask is assured to only be awaited once, in the MoveNextAsync method.")]
internal class CacheOne<T> : IAsyncEnumerator<T>
{
	private readonly IAsyncEnumerator<T> _enumerator;
	private ValueTask<bool> _moveNextTask;

	public T Current { get; private set; }

	public CacheOne(IAsyncEnumerable<T> source)
	{
		Current = default!;
		_enumerator = source.GetAsyncEnumerator();
		_moveNextTask = _enumerator.MoveNextAsync();
	}

	public ValueTask DisposeAsync()
	{
		return _enumerator.DisposeAsync();
	}

	public async ValueTask<bool> MoveNextAsync()
	{
		var result = await _moveNextTask.ConfigureAwait(false);
		Current = _enumerator.Current;
		_moveNextTask = _enumerator.MoveNextAsync();
		return result;
	}

}
