// Copyright (c) 2023 James Swerdfeger
// Licensed under the MIT license.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace EagerAsyncEnumerable.Tests;

[TestClass]
public class IAsyncEnumerableTests
{
	protected record struct ProcessResults(List<int> List, int Time);

	protected class MyException : Exception { }

	protected async IAsyncEnumerable<int> Producer(int count, int delay,
		int exceptionIndex = -1, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		for (int i = 0; i < count; i++)
		{
			await Task.Delay(delay, cancellationToken);
			if (i == exceptionIndex) throw new MyException();
			yield return i;
		}
	}

	protected async ValueTask<ProcessResults> Consumer(IAsyncEnumerable<int> source, int delay,
		int exceptionIndex = -1, CancellationToken cancellationToken = default)
	{
		var actualList = new List<int>();

		int count = 0;
		var stopwatch = new Stopwatch();
		stopwatch.Start();
		//await foreach (var item in source.WithCancellation(cancellationToken))
		await foreach (var item in source)
		{
			await Task.Delay(delay, cancellationToken);
			if (count++ == exceptionIndex) throw new MyException();

			actualList.Add(item);
		}
		stopwatch.Stop();

		return new(actualList, (int)stopwatch.ElapsedMilliseconds);
	}


	/// <summary>Assert the enumerable returns all items.</summary>
	[DataTestMethod, Timeout(30000)]
	[DataRow(10, 100, 100, DisplayName = "Slow Producer, Slow Consumer")]
	[DataRow(10, 100, 10, DisplayName = "Slow Producer, Fast Consumer")]
	[DataRow(10, 100, 0, DisplayName = "Slow Producer, Sync Consumer")]
	[DataRow(10, 10, 100, DisplayName = "Fast Producer, Slow Consumer")]
	[DataRow(10, 10, 10, DisplayName = "Fast Producer, Fast Consumer")]
	[DataRow(10, 10, 0, DisplayName = "Fast Producer, Sync Consumer")]
	[DataRow(10, 0, 100, DisplayName = "Sync Producer, Slow Consumer")]
	[DataRow(10, 0, 10, DisplayName = "Sync Producer, Fast Consumer")]
	[DataRow(10, 0, 0, DisplayName = "Sync Producer, Sync Consumer")]
	public async Task AssertComplete(int count, int producerDelay, int consumerDelay)
	{
		var source = Producer(count, producerDelay);
		var expected = await Consumer(source, consumerDelay);
		var actual = await Consumer(source.AsEagerEnumerable(), consumerDelay);

		CollectionAssert.AreEqual(expected.List, actual.List);
	}


}