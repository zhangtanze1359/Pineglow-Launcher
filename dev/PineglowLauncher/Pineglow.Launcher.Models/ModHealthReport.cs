using System;
using System.Collections.Generic;
using System.Linq;

namespace Pineglow.Launcher.Models;

public sealed class ModHealthReport
{
	public int EnabledCount { get; init; }

	public int DisabledCount { get; init; }

	public int OnlineManagedCount { get; init; }

	public IReadOnlyList<ModHealthIssue> Issues { get; init; } = Array.Empty<ModHealthIssue>();

	public bool CanLaunch => Issues.All((ModHealthIssue issue) => issue.Severity != ModHealthSeverity.Error);
}
