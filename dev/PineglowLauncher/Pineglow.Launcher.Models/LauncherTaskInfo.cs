using System;

namespace Pineglow.Launcher.Models;

public sealed class LauncherTaskInfo
{
	public string Title { get; set; } = string.Empty;

	public string Status { get; set; } = "进行中";

	public string Detail { get; set; } = string.Empty;

	public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;

	public DateTimeOffset? FinishedAt { get; set; }
}
