using System;

namespace Pineglow.Launcher.Models;

public sealed class ResumableTaskItem
{
	public string Id { get; set; } = Guid.NewGuid().ToString("N");

	public ResumableTaskKind Kind { get; set; }

	public ResumableTaskState State { get; set; }

	public string Title { get; set; } = string.Empty;

	public string MinecraftVersion { get; set; } = string.Empty;

	public string SourceInstanceId { get; set; } = string.Empty;

	public ModLoaderKind LoaderKind { get; set; }

	public string LoaderVersion { get; set; } = string.Empty;

	public string LoaderMavenVersion { get; set; } = string.Empty;

	public string LoaderDownloadUrl { get; set; } = string.Empty;

	public string ResultInstanceId { get; set; } = string.Empty;

	public ModpackProject? ModpackProject { get; set; }

	public ModpackVersion? ModpackVersion { get; set; }

	public string Message { get; set; } = "等待执行";

	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

	public bool CanRetry
	{
		get
		{
			ResumableTaskState state = State;
			if (state == ResumableTaskState.Waiting || (uint)(state - 2) <= 1u)
			{
				return true;
			}
			return false;
		}
	}
}
