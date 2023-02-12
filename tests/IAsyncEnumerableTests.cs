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


	// FYI, I don't test this one in synchronous operation, as like with any task, if you cancel
	// an operation after it's already complete, you wouldn't expect to have the exception get
	// raised.
	/// <summary>Assert cancellation works properly.</summary>
	[DataTestMethod, Timeout(30000)]
	[DataRow(10, 100, 100, DisplayName = "Slow Producer, Slow Consumer")]
	[DataRow(10, 100, 10, DisplayName = "Slow Producer, Fast Consumer")]
	[DataRow(10, 100, 0, DisplayName = "Slow Producer, Sync Consumer")]
	[DataRow(10, 10, 100, DisplayName = "Fast Producer, Slow Consumer")]
	[DataRow(10, 10, 10, DisplayName = "Fast Producer, Fast Consumer")]
	[DataRow(10, 10, 0, DisplayName = "Fast Producer, Sync Consumer")]
	[DataRow(10, 0, 100, DisplayName = "Sync Producer, Slow Consumer")]
	[DataRow(10, 0, 10, DisplayName = "Sync Producer, Fast Consumer")]
	public async Task AssertCancellation(int count, int producerDelay, int consumerDelay)
	{
		var cancellationTokenSource = new CancellationTokenSource();
		var source = Producer(count, producerDelay);

		int cancelAfter = (count / 2) * Math.Max(producerDelay, consumerDelay);
		cancellationTokenSource.CancelAfter(cancelAfter);
		try
		{
			var actual = await Consumer(source.AsEagerEnumerable(), consumerDelay, -1,
				cancellationTokenSource.Token);
		}
		catch (OperationCanceledException)
		{
			return;
		}

		Assert.Fail("OperationCanceledException was not thrown.");
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

	/// <summary>Assert if a consumer throws an exception, it flows to us.
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
	public async Task AssertConsumerException(int count, int producerDelay, int consumerDelay)
	{
		var source = Producer(count, producerDelay);

		try
		{
			var actual = await Consumer(source.AsEagerEnumerable(), consumerDelay, count / 2);
		}
		catch (MyException)
		{
			return;
		}
		catch (Exception e)
		{
			Assert.Fail($"A different exception, {e}, was raised.");
		}

		Assert.Fail("No exception was raised.");
	}

	/// <summary>Assert disposal mid-enumeration does not raise any exceptions.</summary>
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
	public async Task AssertDisposal(int count, int producerDelay, int consumerDelay)
	{
		var source = Producer(count, producerDelay);

		int i = 0, disposalCount = count / 2;
		await foreach (var item in source.AsEagerEnumerable())
		{
			await Task.Delay(consumerDelay);
			if (i++ == disposalCount)
			{
				break;
			}
		}

		// Make sure no other thread crashes in the background after a delay.
		await Task.Delay(1000);
	}

	/// <summary>Asserts correct operation if producer is empty.</summary>
	[TestMethod, Timeout(30000)]
	public async Task AssertNoEntries()
	{
		int count = 0, producerDelay = 100, consumerDelay = 100;
		var source = Producer(count, producerDelay);
		var expected = new List<int>();
		var actual = await Consumer(source.AsEagerEnumerable(), consumerDelay);

		CollectionAssert.AreEqual(expected, actual.List);
	}

	/// <summary>Assert the consumer and producer run in parallel with eachother.</summary>
	[TestMethod, Timeout(30000)]
	public async Task AssertParallel()
	{
		int count = 10, producerDelay = 100, consumerDelay = 100;
		var source = Producer(count, producerDelay);
		var expected = await Consumer(source, consumerDelay);
		var actual = await Consumer(source.AsEagerEnumerable(), consumerDelay);

		Assert.IsTrue((actual.Time / expected.Time) < 0.7);
	}

	/// <summary>Assert if a producer throws an exception, it flows to us.
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
	public async Task AssertProducerException(int count, int producerDelay, int consumerDelay)
	{
		var source = Producer(count, producerDelay, count / 2);

		try
		{
			var actual = await Consumer(source.AsEagerEnumerable(), consumerDelay);
		}
		catch (MyException)
		{
			return;
		}
		catch (Exception e)
		{
			Assert.Fail($"A different exception, {e}, was raised.");
		}

		Assert.Fail("No exception was raised.");
	}

	/// <summary>Assert there's no stack dive if it runs synchronously.</summary>
	[TestMethod, Timeout(30000)]
	public async Task AssertSafeStack()
	{
		int count = 1_000_000, producerDelay = 0, consumerDelay = 0;
		var source = Producer(count, producerDelay);
		var expected = await Consumer(source, consumerDelay);
		var actual = await Consumer(source.AsEagerEnumerable(), consumerDelay);

		CollectionAssert.AreEqual(expected.List, actual.List);
	}



}