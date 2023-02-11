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
//
// Due to the similarities between using a queue, and only caching one single task, I'm going to
// make my implementation use a queue. That way, if your producer or consumer are unpredictable,
// you'll definitely get beneifits. And if it's not, there's no real loss.

// * Summary *

//BenchmarkDotNet=v0.13.4, OS=Windows 10 (10.0.19045.2486)
//Intel Core i7-7500U CPU 2.70GHz (Kaby Lake), 1 CPU, 4 logical and 2 physical cores
//.NET SDK=7.0.102
//  [Host]     : .NET 7.0.2 (7.0.222.60605), X64 RyuJIT AVX2
//  DefaultJob : .NET 7.0.2 (7.0.222.60605), X64 RyuJIT AVX2


//|                    Method | ProducerDelay | ConsumerDelay |             Mean |          Error |         StdDev |           Median |              Min |  Ratio | RatioSD |    Gen0 | Allocated | Alloc Ratio |
//|-------------------------- |-------------- |-------------- |-----------------:|---------------:|---------------:|-----------------:|-----------------:|-------:|--------:|--------:|----------:|------------:|
//|                   NoEager |             0 |             0 |         2.062 us |      0.0049 us |      0.0044 us |         2.063 us |         2.052 us |   1.00 |    0.00 |  0.0534 |     112 B |        1.00 |
//|                  CacheOne |             0 |             0 |         3.788 us |      0.0041 us |      0.0037 us |         3.788 us |         3.783 us |   1.84 |    0.00 |  0.0763 |     160 B |        1.43 |
//|        ValueTaskSourceOne |             0 |             0 |         2.740 us |      0.0115 us |      0.0102 us |         2.738 us |         2.728 us |   1.33 |    0.01 |  0.1221 |     256 B |        2.29 |
//| TaskCompletionSourceQueue |             0 |             0 |         5.755 us |      0.0129 us |      0.0120 us |         5.751 us |         5.740 us |   2.79 |    0.01 |  0.3815 |     808 B |        7.21 |
//|            SempahoreQueue |             0 |             0 |       547.496 us |     10.4309 us |     15.6125 us |       539.080 us |       535.476 us | 268.77 |    9.46 | 17.5781 |   38777 B |      346.22 |
//|      ValueTaskSourceQueue |             0 |             0 |         5.855 us |      0.0084 us |      0.0070 us |         5.855 us |         5.838 us |   2.84 |    0.01 |  0.4196 |     880 B |        7.86 |
//|                           |               |               |                  |                |                |                  |                  |        |         |         |           |             |
//|                   NoEager |             0 |            10 |   806,957.069 us |  2,510.7899 us |  2,096.6233 us |   807,475.000 us |   803,942.700 us |   1.00 |    0.00 |       - |    9792 B |        1.00 |
//|                  CacheOne |             0 |            10 |   804,868.293 us |  8,620.5885 us |  8,063.7038 us |   806,346.000 us |   781,786.000 us |   1.00 |    0.01 |       - |    9840 B |        1.00 |
//|        ValueTaskSourceOne |             0 |            10 |   806,839.329 us |  4,824.6876 us |  4,276.9599 us |   807,061.300 us |   795,155.900 us |   1.00 |    0.01 |       - |    9936 B |        1.01 |
//| TaskCompletionSourceQueue |             0 |            10 |   807,415.007 us |  3,217.9063 us |  2,852.5901 us |   808,367.600 us |   803,212.200 us |   1.00 |    0.00 |       - |   10488 B |        1.07 |
//|            SempahoreQueue |             0 |            10 |   788,613.371 us |  5,255.7953 us |  4,659.1256 us |   789,304.300 us |   777,855.000 us |   0.98 |    0.01 |       - |   48744 B |        4.98 |
//|      ValueTaskSourceQueue |             0 |            10 |   786,394.993 us | 11,810.1360 us | 11,047.2085 us |   788,605.500 us |   754,746.500 us |   0.98 |    0.01 |       - |   10848 B |        1.11 |
//|                           |               |               |                  |                |                |                  |                  |        |         |         |           |             |
//|                   NoEager |             0 |            30 | 1,613,385.880 us | 12,255.9005 us | 11,464.1769 us | 1,614,328.600 us | 1,590,473.600 us |   1.00 |    0.00 |       - |   10080 B |        1.00 |
//|                  CacheOne |             0 |            30 | 1,611,291.493 us |  9,640.6784 us |  9,017.8965 us | 1,614,617.000 us | 1,598,399.200 us |   1.00 |    0.01 |       - |   10128 B |        1.00 |
//|        ValueTaskSourceOne |             0 |            30 | 1,614,751.667 us | 11,723.8828 us | 10,966.5272 us | 1,613,030.800 us | 1,599,205.400 us |   1.00 |    0.01 |       - |   10224 B |        1.01 |
//| TaskCompletionSourceQueue |             0 |            30 | 1,606,196.973 us |  9,180.7686 us |  8,587.6966 us | 1,603,455.000 us | 1,594,560.100 us |   1.00 |    0.01 |       - |   10776 B |        1.07 |
//|            SempahoreQueue |             0 |            30 | 1,605,425.193 us | 10,618.2874 us |  9,932.3526 us | 1,603,353.000 us | 1,587,439.200 us |   1.00 |    0.01 |       - |   48792 B |        4.84 |
//|      ValueTaskSourceQueue |             0 |            30 | 1,604,854.307 us | 10,946.9212 us | 10,239.7569 us | 1,610,050.400 us | 1,590,182.100 us |   0.99 |    0.01 |       - |   10848 B |        1.08 |
//|                           |               |               |                  |                |                |                  |                  |        |         |         |           |             |
//|                   NoEager |            10 |             0 |   793,148.862 us |  3,417.7226 us |  2,853.9532 us |   792,557.100 us |   789,375.900 us |   1.00 |    0.00 |       - |   10176 B |        1.00 |
//|                  CacheOne |            10 |             0 |   794,123.138 us |  1,869.0768 us |  1,560.7637 us |   793,806.900 us |   791,643.400 us |   1.00 |    0.00 |       - |   16736 B |        1.64 |
//|        ValueTaskSourceOne |            10 |             0 |   793,911.015 us |  1,639.2879 us |  1,368.8797 us |   794,547.800 us |   792,166.100 us |   1.00 |    0.00 |       - |   13520 B |        1.33 |
//| TaskCompletionSourceQueue |            10 |             0 |   793,403.700 us |  3,830.1818 us |  3,395.3564 us |   793,147.800 us |   787,385.200 us |   1.00 |    0.01 |       - |   22424 B |        2.20 |
//|            SempahoreQueue |            10 |             0 |   792,871.150 us |  2,781.6123 us |  2,171.6999 us |   792,499.550 us |   788,630.000 us |   1.00 |    0.01 |       - |   41008 B |        4.03 |
//|      ValueTaskSourceQueue |            10 |             0 |   794,791.480 us |  4,618.2577 us |  4,319.9212 us |   794,449.000 us |   789,899.500 us |   1.00 |    0.01 |       - |   17600 B |        1.73 |
//|                           |               |               |                  |                |                |                  |                  |        |         |         |           |             |
//|                   NoEager |            10 |            10 | 1,587,576.283 us |  3,779.6812 us |  2,950.9265 us | 1,588,363.550 us | 1,580,183.300 us |   1.00 |    0.00 |       - |   18576 B |        1.00 |
//|                  CacheOne |            10 |            10 |   809,834.783 us |  1,970.4865 us |  1,538.4263 us |   809,934.100 us |   807,270.000 us |   0.51 |    0.00 |       - |   25136 B |        1.35 |
//|        ValueTaskSourceOne |            10 |            10 |   808,783.925 us |  2,969.0901 us |  2,318.0703 us |   809,888.050 us |   804,406.100 us |   0.51 |    0.00 |       - |   21984 B |        1.18 |
//| TaskCompletionSourceQueue |            10 |            10 |   809,243.743 us |  4,161.8395 us |  3,689.3623 us |   809,367.750 us |   803,441.000 us |   0.51 |    0.00 |       - |   25832 B |        1.39 |
//|            SempahoreQueue |            10 |            10 |   810,412.785 us |  3,605.6877 us |  3,010.9125 us |   810,395.300 us |   806,332.200 us |   0.51 |    0.00 |       - |   48128 B |        2.59 |
//|      ValueTaskSourceQueue |            10 |            10 |   806,999.164 us |  4,251.1173 us |  3,768.5048 us |   807,961.100 us |   798,419.900 us |   0.51 |    0.00 |       - |   22688 B |        1.22 |
//|                           |               |               |                  |                |                |                  |                  |        |         |         |           |             |
//|                   NoEager |            10 |            30 | 2,385,129.707 us | 29,989.0888 us | 28,051.8122 us | 2,392,569.700 us | 2,346,070.800 us |   1.00 |    0.00 |       - |   18288 B |        1.00 |
//|                  CacheOne |            10 |            30 | 1,629,936.633 us |  5,532.4274 us |  5,175.0360 us | 1,630,616.100 us | 1,619,062.400 us |   0.68 |    0.01 |       - |   18472 B |        1.01 |
//|        ValueTaskSourceOne |            10 |            30 | 1,630,241.346 us |  4,565.0170 us |  3,811.9960 us | 1,630,095.100 us | 1,625,959.200 us |   0.69 |    0.01 |       - |   18784 B |        1.03 |
//| TaskCompletionSourceQueue |            10 |            30 | 1,629,284.321 us |  6,136.5859 us |  5,439.9236 us | 1,630,369.250 us | 1,613,893.600 us |   0.68 |    0.01 |       - |   19120 B |        1.05 |
//|            SempahoreQueue |            10 |            30 | 1,629,882.713 us |  5,567.6640 us |  5,207.9964 us | 1,628,830.400 us | 1,621,412.300 us |   0.68 |    0.01 |       - |   38880 B |        2.13 |
//|      ValueTaskSourceQueue |            10 |            30 | 1,629,759.553 us |  3,616.7214 us |  3,383.0834 us | 1,629,632.000 us | 1,624,069.300 us |   0.68 |    0.01 |       - |   19096 B |        1.04 |
//|                           |               |               |                  |                |                |                  |                  |        |         |         |           |             |
//|                   NoEager |            30 |             0 | 1,590,375.179 us |  9,306.9003 us |  8,250.3247 us | 1,591,316.200 us | 1,575,048.600 us |   1.00 |    0.00 |       - |    9888 B |        1.00 |
//|                  CacheOne |            30 |             0 | 1,601,803.700 us | 12,157.4190 us | 11,372.0573 us | 1,599,165.300 us | 1,583,597.000 us |   1.01 |    0.01 |       - |   16736 B |        1.69 |
//|        ValueTaskSourceOne |            30 |             0 | 1,617,981.567 us |  7,816.5854 us |  7,311.6388 us | 1,615,203.400 us | 1,610,489.200 us |   1.02 |    0.01 |       - |   13232 B |        1.34 |
//| TaskCompletionSourceQueue |            30 |             0 | 1,617,516.847 us |  6,603.8227 us |  6,177.2198 us | 1,616,256.400 us | 1,609,942.100 us |   1.02 |    0.01 |       - |   22424 B |        2.27 |
//|            SempahoreQueue |            30 |             0 | 1,618,752.520 us |  7,621.9560 us |  7,129.5823 us | 1,617,185.100 us | 1,605,447.200 us |   1.02 |    0.01 |       - |   41088 B |        4.16 |
//|      ValueTaskSourceQueue |            30 |             0 | 1,618,341.220 us |  7,293.5168 us |  6,822.3602 us | 1,615,368.200 us | 1,610,965.200 us |   1.02 |    0.01 |       - |   17600 B |        1.78 |
//|                           |               |               |                  |                |                |                  |                  |        |         |         |           |             |
//|                   NoEager |            30 |            10 | 2,427,247.000 us |  6,137.9018 us |  5,741.3972 us | 2,427,729.600 us | 2,418,365.300 us |   1.00 |    0.00 |       - |   18288 B |        1.00 |
//|                  CacheOne |            30 |            10 | 1,621,430.821 us | 15,150.8533 us | 13,430.8368 us | 1,619,508.400 us | 1,599,547.300 us |   0.67 |    0.00 |       - |   25136 B |        1.37 |
//|        ValueTaskSourceOne |            30 |            10 | 1,621,430.200 us | 14,791.9094 us | 13,836.3613 us | 1,626,533.400 us | 1,594,756.200 us |   0.67 |    0.01 |       - |   21632 B |        1.18 |
//| TaskCompletionSourceQueue |            30 |            10 | 1,612,221.747 us | 14,551.6613 us | 13,611.6330 us | 1,614,739.000 us | 1,581,122.600 us |   0.66 |    0.01 |       - |   30584 B |        1.67 |
//|            SempahoreQueue |            30 |            10 | 1,621,375.180 us | 16,638.9332 us | 15,564.0684 us | 1,616,924.300 us | 1,594,968.200 us |   0.67 |    0.01 |       - |   49408 B |        2.70 |
//|      ValueTaskSourceQueue |            30 |            10 | 1,621,557.800 us | 11,691.4777 us | 10,936.2155 us | 1,621,766.600 us | 1,600,341.300 us |   0.67 |    0.00 |       - |   25856 B |        1.41 |
//|                           |               |               |                  |                |                |                  |                  |        |         |         |           |             |
//|                   NoEager |            30 |            30 | 3,195,076.267 us | 22,112.0221 us | 20,683.5992 us | 3,189,182.900 us | 3,147,776.100 us |   1.00 |    0.00 |       - |   18288 B |        1.00 |
//|                  CacheOne |            30 |            30 | 1,650,682.780 us |  7,902.0729 us |  7,391.6039 us | 1,648,457.900 us | 1,643,143.900 us |   0.52 |    0.00 |       - |   25136 B |        1.37 |
//|        ValueTaskSourceOne |            30 |            30 | 1,650,140.533 us |  7,398.1031 us |  6,920.1903 us | 1,646,647.700 us | 1,643,038.100 us |   0.52 |    0.00 |       - |   21632 B |        1.18 |
//| TaskCompletionSourceQueue |            30 |            30 | 1,651,018.020 us |  7,489.5499 us |  7,005.7297 us | 1,648,713.800 us | 1,641,418.700 us |   0.52 |    0.00 |       - |   24824 B |        1.36 |
//|            SempahoreQueue |            30 |            30 | 1,650,105.527 us |  8,891.0952 us |  8,316.7360 us | 1,648,016.800 us | 1,639,988.300 us |   0.52 |    0.00 |       - |   49328 B |        2.70 |
//|      ValueTaskSourceQueue |            30 |            30 | 1,649,999.153 us |  7,527.2208 us |  7,040.9670 us | 1,647,028.100 us | 1,641,034.100 us |   0.52 |    0.00 |       - |   22400 B |        1.22 |

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

