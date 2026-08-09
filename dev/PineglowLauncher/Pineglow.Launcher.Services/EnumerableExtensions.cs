using System.Collections.Generic;

namespace Pineglow.Launcher.Services;

internal static class EnumerableExtensions
{
	public static IEnumerable<T> DistinctPreserveOrder<T>(this IEnumerable<T> source)
	{
		HashSet<T> set = new HashSet<T>();
		foreach (T item in source)
		{
			if (set.Add(item))
			{
				yield return item;
			}
		}
	}
}
