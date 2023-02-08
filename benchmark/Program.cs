// Copyright (c) 2023 James Swerdfeger
// Licensed under the MIT license.

using Benchmark;
using BenchmarkDotNet.Running;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

#if !DEBUG
BenchmarkRunner.Run<Benchmarker>();
#else

await Test(100, 0, 0);
await Test(25, 0, 20);
await Test(25, 20, 0);
await Test(25, 20, 20);

static async ValueTask Test(int count, int producerDelay, int consumerDelay)
{
	var benchmarker = new Benchmarker(count, producerDelay, consumerDelay);
	Console.WriteLine($"Testing Count = {count}, ProducerDelay = {producerDelay}, ConsumerDelay = {consumerDelay}");

	int expected = (int)(benchmarker.Count * ((benchmarker.Count - 1) / 2.0));
	int actual;

	actual = await benchmarker.NoEager();
	Debug.Assert(expected == actual);
	actual = await benchmarker.CacheOne();
	Debug.Assert(expected == actual);
}

;
#endif

