// Copyright (c) 2023 James Swerdfeger
// Licensed under the MIT license.

// This will test various implementation ideas for EagerAsyncEnumerable to find "the best one".

// * Summary *

// There's no real noticeable difference in speed between the eager implementations, which is
// expected. Of course the bottleneck is going to be the producer/consumer, or you wouldn't need
// this. Proof, at least, in terms of CPU, these methods aren't terribly different.
//
// But, in terms of allocations, I am surprised writing my own IValueTaskSource is better than
// what the compiler self-generates.


//BenchmarkDotNet=v0.13.4, OS=Windows 10 (10.0.19045.2486)
//Intel Core i7-7500U CPU 2.70GHz (Kaby Lake), 1 CPU, 4 logical and 2 physical cores
//.NET SDK=7.0.102
//  [Host]     : .NET 7.0.2 (7.0.222.60605), X64 RyuJIT AVX2
//  DefaultJob : .NET 7.0.2 (7.0.222.60605), X64 RyuJIT AVX2


//|             Method | ProducerDelay | ConsumerDelay |             Mean |          Error |         StdDev |              Min | Ratio | RatioSD |   Gen0 | Allocated | Alloc Ratio |
//|------------------- |-------------- |-------------- |-----------------:|---------------:|---------------:|-----------------:|------:|--------:|-------:|----------:|------------:|
//|            NoEager |             0 |             0 |         2.061 us |      0.0125 us |      0.0105 us |         2.042 us |  1.00 |    0.00 | 0.0534 |     112 B |        1.00 |
//|           CacheOne |             0 |             0 |         3.259 us |      0.0090 us |      0.0075 us |         3.247 us |  1.58 |    0.01 | 0.0763 |     160 B |        1.43 |
//| ValueTaskSourceOne |             0 |             0 |         2.740 us |      0.0075 us |      0.0066 us |         2.726 us |  1.33 |    0.01 | 0.1221 |     256 B |        2.29 |
//|                    |               |               |                  |                |                |                  |       |         |        |           |             |
//|            NoEager |             0 |            10 |   802,308.554 us |  3,353.9416 us |  2,800.6931 us |   795,076.400 us |  1.00 |    0.00 |      - |   10080 B |        1.00 |
//|           CacheOne |             0 |            10 |   803,509.823 us |  3,038.5654 us |  2,537.3397 us |   798,692.700 us |  1.00 |    0.00 |      - |    9840 B |        0.98 |
//| ValueTaskSourceOne |             0 |            10 |   802,478.614 us |  3,875.8520 us |  3,435.8418 us |   793,664.200 us |  1.00 |    0.01 |      - |   10224 B |        1.01 |
//|                    |               |               |                  |                |                |                  |       |         |        |           |             |
//|            NoEager |             0 |            30 | 1,614,804.473 us |  9,345.7426 us |  8,742.0134 us | 1,605,280.600 us |  1.00 |    0.00 |      - |    9792 B |        1.00 |
//|           CacheOne |             0 |            30 | 1,610,176.336 us |  6,686.0981 us |  5,927.0518 us | 1,600,223.800 us |  1.00 |    0.01 |      - |    9840 B |        1.00 |
//| ValueTaskSourceOne |             0 |            30 | 1,612,058.293 us |  8,832.7971 us |  8,262.2039 us | 1,600,704.900 us |  1.00 |    0.01 |      - |   10224 B |        1.04 |
//|                    |               |               |                  |                |                |                  |       |         |        |           |             |
//|            NoEager |            10 |             0 |   803,652.177 us |  1,747.3317 us |  1,459.1011 us |   800,018.800 us |  1.00 |    0.00 |      - |    9888 B |        1.00 |
//|           CacheOne |            10 |             0 |   802,825.757 us |  2,767.4739 us |  2,453.2936 us |   798,761.600 us |  1.00 |    0.00 |      - |   17024 B |        1.72 |
//| ValueTaskSourceOne |            10 |             0 |   803,671.285 us |  2,887.0350 us |  2,410.8050 us |   799,539.000 us |  1.00 |    0.00 |      - |   13520 B |        1.37 |
//|                    |               |               |                  |                |                |                  |       |         |        |           |             |
//|            NoEager |            10 |            10 | 1,607,805.964 us |  3,673.9016 us |  3,256.8181 us | 1,601,061.800 us |  1.00 |    0.00 |      - |   18288 B |        1.00 |
//|           CacheOne |            10 |            10 |   819,377.653 us |  4,981.6301 us |  4,659.8199 us |   808,925.500 us |  0.51 |    0.00 |      - |   25424 B |        1.39 |
//| ValueTaskSourceOne |            10 |            10 |   820,472.808 us |  2,600.0661 us |  2,171.1730 us |   816,658.900 us |  0.51 |    0.00 |      - |   21632 B |        1.18 |
//|                    |               |               |                  |                |                |                  |       |         |        |           |             |
//|            NoEager |            10 |            30 | 2,413,629.993 us |  3,289.4608 us |  3,076.9637 us | 2,410,494.900 us |  1.00 |    0.00 |      - |   18288 B |        1.00 |
//|           CacheOne |            10 |            30 | 1,685,140.653 us | 31,117.5552 us | 29,107.3805 us | 1,638,820.300 us |  0.70 |    0.01 |      - |   18472 B |        1.01 |
//| ValueTaskSourceOne |            10 |            30 | 1,666,166.557 us | 22,614.5560 us | 20,047.2149 us | 1,629,178.600 us |  0.69 |    0.01 |      - |   18784 B |        1.03 |
//|                    |               |               |                  |                |                |                  |       |         |        |           |             |
//|            NoEager |            30 |             0 | 1,642,713.460 us | 24,048.9467 us | 22,495.3996 us | 1,607,314.700 us |  1.00 |    0.00 |      - |    9888 B |        1.00 |
//|           CacheOne |            30 |             0 | 1,638,087.360 us | 24,342.9613 us | 22,770.4211 us | 1,602,379.800 us |  1.00 |    0.02 |      - |   16736 B |        1.69 |
//| ValueTaskSourceOne |            30 |             0 | 1,658,208.173 us | 29,336.1397 us | 27,441.0433 us | 1,610,281.300 us |  1.01 |    0.02 |      - |   13232 B |        1.34 |
//|                    |               |               |                  |                |                |                  |       |         |        |           |             |
//|            NoEager |            30 |            10 | 2,404,500.493 us | 19,906.0303 us | 18,620.1131 us | 2,376,272.200 us |  1.00 |    0.00 |      - |   18288 B |        1.00 |
//|           CacheOne |            30 |            10 | 1,631,975.733 us | 17,764.0440 us | 16,616.4978 us | 1,602,469.700 us |  0.68 |    0.01 |      - |   25424 B |        1.39 |
//| ValueTaskSourceOne |            30 |            10 | 1,634,365.613 us | 16,347.2023 us | 15,291.1832 us | 1,616,788.200 us |  0.68 |    0.01 |      - |   21920 B |        1.20 |
//|                    |               |               |                  |                |                |                  |       |         |        |           |             |
//|            NoEager |            30 |            30 | 3,219,549.843 us | 19,307.6827 us | 17,115.7578 us | 3,198,996.200 us |  1.00 |    0.00 |      - |   18288 B |        1.00 |
//|           CacheOne |            30 |            30 | 1,643,004.547 us | 13,712.8911 us | 12,827.0468 us | 1,618,323.900 us |  0.51 |    0.00 |      - |   25288 B |        1.38 |
//| ValueTaskSourceOne |            30 |            30 | 1,649,276.447 us | 11,178.8330 us | 10,456.6873 us | 1,632,064.700 us |  0.51 |    0.00 |      - |   21920 B |        1.20 |


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
	actual = await benchmarker.ValueTaskSourceOne();
	Debug.Assert(expected == actual);
	actual = await benchmarker.TaskCompletionSourceQueue();
	Debug.Assert(expected == actual);
	actual = await benchmarker.SempahoreQueue();
	Debug.Assert(expected == actual);
	actual = await benchmarker.ValueTaskSourceQueue();
	Debug.Assert(expected == actual);
}

;
#endif

