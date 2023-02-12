// Copyright (c) 2023 James Swerdfeger
// Licensed under the MIT license.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace EagerAsyncEnumerable.Tests;

[TestClass]
public class IAsyncEnumerableTests : TestRoot<IAsyncEnumerable<int>, int>
{
	protected override async IAsyncEnumerable<int> Producer(int count, int delay,
		int exceptionIndex, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		for (int i = 0; i < count; i++)
		{
			await Task.Delay(delay, cancellationToken);
			if (i == exceptionIndex) throw new MyException();
			yield return i;
		}
	}

	protected override IAsyncEnumerable<int> AsEagerEnumerable(IAsyncEnumerable<int> source)
		=> source.AsEagerEnumerable();

	protected override ValueTask<ProcessResults> Consumer(IAsyncEnumerable<int> source, int delay)
		=> base.Consumer(source, delay, -1, default);

}