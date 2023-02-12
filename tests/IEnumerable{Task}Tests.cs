// Copyright (c) 2023 James Swerdfeger
// Licensed under the MIT license.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace EagerAsyncEnumerable.Tests;

[TestClass]
public class IEnumerableTaskTests : TestRoot<IEnumerable<Task>, bool>
{
	protected override IEnumerable<Task> Producer(int count, int delay,
		int exceptionIndex, CancellationToken cancellationToken)
	{
		for (int i = 0; i < count; i++)
		{
			if (i == exceptionIndex) throw new MyException();
			yield return Produce(i, delay, cancellationToken);
		}
	}

	private Task Produce(int i, int delay, CancellationToken cancellationToken)
	{
		return Task.Delay(delay, cancellationToken);
	}

	protected override IAsyncEnumerable<bool> AsEagerEnumerable(IEnumerable<Task> source)
		=> source.AsEagerEnumerable();

	protected override async ValueTask<ProcessResults> Consumer(IEnumerable<Task> source, int delay)
	{
		var actualList = new List<bool>();

		var stopwatch = new Stopwatch();
		stopwatch.Start();
		foreach (var task in source)
		{
			await task;
			await Task.Delay(delay);
			actualList.Add(true);
		}
		stopwatch.Stop();

		return new(actualList, (int)stopwatch.ElapsedMilliseconds);
	}

}