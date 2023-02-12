using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace EagerAsyncEnumerable.Tests;

[TestClass]
public class IEnumerableTaskTTests : TestRoot<IEnumerable<Task<int>>, int>
{
	protected override IEnumerable<Task<int>> Producer(int count, int delay,
		int exceptionIndex, CancellationToken cancellationToken)
	{
		for (int i = 0; i < count; i++)
		{
			if (i == exceptionIndex) throw new MyException();
			yield return Produce(i, delay, cancellationToken);
		}
	}

	private async Task<int> Produce(int i, int delay, CancellationToken cancellationToken)
	{
		await Task.Delay(delay, cancellationToken);
		return i;
	}

	protected override IAsyncEnumerable<int> AsEagerEnumerable(IEnumerable<Task<int>> source)
		=> source.AsEagerEnumerable();

	protected override async ValueTask<ProcessResults> Consumer(IEnumerable<Task<int>> source,
		int delay)
	{
		var actualList = new List<int>();

		var stopwatch = new Stopwatch();
		stopwatch.Start();
		foreach (var task in source)
		{
			actualList.Add(await task);
			await Task.Delay(delay);			
		}
		stopwatch.Stop();

		return new(actualList, (int)stopwatch.ElapsedMilliseconds);
	}

}