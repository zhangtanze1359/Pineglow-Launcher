using System;
using System.Collections.Generic;
using System.Linq;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public static class VersionFilter
{
	public static List<VersionEntry> Apply(IEnumerable<VersionEntry> versions, string? query, string? type)
	{
		string normalizedQuery = query?.Trim() ?? string.Empty;
		string normalizedType = type?.Trim().ToLowerInvariant() ?? "all";
		return (from version in versions
			where normalizedType == "all" || version.Type.Equals(normalizedType, StringComparison.OrdinalIgnoreCase)
			where string.IsNullOrEmpty(normalizedQuery) || version.Id.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
			orderby ParseReleaseTime(version.ReleaseTime) descending
			select version).ToList();
	}

	private static DateTimeOffset ParseReleaseTime(string value)
	{
		if (!DateTimeOffset.TryParse(value, out var result))
		{
			return DateTimeOffset.MinValue;
		}
		return result;
	}
}
