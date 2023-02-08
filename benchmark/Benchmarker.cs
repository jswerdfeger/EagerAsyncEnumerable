// Copyright (c) 2023 James Swerdfeger
// Licensed under the MIT license.

using BenchmarkDotNet.Attributes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Benchmark;

[MemoryDiagnoser, MinColumn]
public class Benchmarker
{
	[Params(0, 10, 30)]
	public int ProducerDelay { get; set; }
	[Params(0, 10, 30)]
	public int ConsumerDelay { get; set; }

	public int Count { get; set; } = 50;

	public Benchmarker() { }

	internal Benchmarker(int count, int producerDelay, int consumerDelay)
	{
		Count = count;
		ProducerDelay = producerDelay;
		ConsumerDelay = consumerDelay;
		GlobalSetup();
	}

	[GlobalSetup]
	public void GlobalSetup()
	{
	}


	private async IAsyncEnumerable<int> ProduceInts()
	{
		int count = Count;
		for (int i = 0; i < count; i++)
		{
			await Task.Delay(ProducerDelay);
			yield return i;
		}
	}

	private async ValueTask<int> ConsumeInts(IAsyncEnumerator<int> enumerator)
	{
		int total = 0;
		while (await enumerator.MoveNextAsync())
		{
			await Task.Delay(ConsumerDelay);
			total += enumerator.Current;
		}
		return total;
	}


	[Benchmark(Baseline = true)]
	public async ValueTask<int> NoEager()
	{
		var source = ProduceInts();
		await using var enumerator = source.GetAsyncEnumerator();
		return await ConsumeInts(enumerator);
	}

	[Benchmark]
	public async ValueTask<int> CacheOne()
	{
		var source = ProduceInts();
		await using var enumerator = new CacheOne<int>(source);
		return await ConsumeInts(enumerator);
	}

	[Benchmark]
	public async ValueTask<int> ValueTaskSourceOne()
	{
		var source = ProduceInts();
		await using var enumerator = new ValueTaskSourceOne<int>(source);
		return await ConsumeInts(enumerator);
	}

	[Benchmark]
	public async ValueTask<int> TaskCompletionSourceQueue()
	{
		var source = ProduceInts();
		await using var enumerator = new TaskCompletionSourceQueue<int>(source);
		return await ConsumeInts(enumerator);
	}

	[Benchmark]
	public async ValueTask<int> SempahoreQueue()
	{
		var source = ProduceInts();
		await using var enumerator = new SemaphoreQueue<int>(source);
		return await ConsumeInts(enumerator);
	}


}