namespace Pineglow.Launcher.Models;

public sealed class VersionDownloadQueueItem
{
	public string VersionId { get; init; } = string.Empty;

	public VersionDownloadQueueState State { get; set; }

	public string Message { get; set; } = "等待下载";

	public bool CanRemove
	{
		get
		{
			switch (State)
			{
			case VersionDownloadQueueState.Waiting:
			case VersionDownloadQueueState.Paused:
			case VersionDownloadQueueState.Failed:
				return true;
			default:
				return false;
			}
		}
	}
}
