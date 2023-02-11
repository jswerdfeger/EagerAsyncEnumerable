using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace EagerAsyncEnumerable;

/// <summary>
/// Provides extensions to <see cref="Queue"/>.
/// </summary>
internal static class QueueExtensions
{
#if NETSTANDARD2_0
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static bool TryDequeue<T>(this Queue<T> queue, out T result)
	{
		if (queue.Count == 0)
		{
			result = default!;
			return false;
		} 
		else
		{
			result = queue.Dequeue();
			return true;
		}
	}
#endif

}
