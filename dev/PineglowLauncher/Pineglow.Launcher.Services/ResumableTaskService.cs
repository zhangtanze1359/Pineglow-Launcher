using System;
using System.Collections.Generic;
using System.Linq;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public static class ResumableTaskService
{
	public static List<ResumableTaskItem> Restore(IEnumerable<ResumableTaskItem>? tasks, IEnumerable<string>? completedInstanceIds = null)
	{
		HashSet<string> completed = new HashSet<string>(completedInstanceIds ?? Array.Empty<string>(), StringComparer.Ordinal);
		return (from task in (tasks ?? Array.Empty<ResumableTaskItem>()).Where(IsValid)
			where string.IsNullOrWhiteSpace(task.ResultInstanceId) || !completed.Contains(task.ResultInstanceId)
			select task).Select(CloneForRestore).ToList();
	}

	public static bool IsValid(ResumableTaskItem task)
	{
		return task.Kind switch
		{
			ResumableTaskKind.JavaPreparation => !string.IsNullOrWhiteSpace(task.MinecraftVersion), 
			ResumableTaskKind.LoaderInstall => !string.IsNullOrWhiteSpace(task.MinecraftVersion) && !string.IsNullOrWhiteSpace(task.SourceInstanceId) && !string.IsNullOrWhiteSpace(task.LoaderVersion) && task.LoaderKind != ModLoaderKind.OptiFine, 
			ResumableTaskKind.ModrinthModpackInstall => HasModrinthSource(task), 
			ResumableTaskKind.ModrinthModpackUpdate => HasModrinthSource(task) && !string.IsNullOrWhiteSpace(task.SourceInstanceId), 
			_ => false, 
		};
	}

	public static List<ResumableTaskItem> CloneAll(IEnumerable<ResumableTaskItem> tasks)
	{
		return tasks.Select(Clone).ToList();
	}

	private static bool HasModrinthSource(ResumableTaskItem task)
	{
		if (!string.IsNullOrWhiteSpace(task.ModpackProject?.ProjectId) && !string.IsNullOrWhiteSpace(task.ModpackVersion?.VersionId))
		{
			return !string.IsNullOrWhiteSpace(task.ModpackVersion?.DownloadUrl);
		}
		return false;
	}

	private static ResumableTaskItem CloneForRestore(ResumableTaskItem task)
	{
		ResumableTaskItem resumableTaskItem = Clone(task);
		if (resumableTaskItem.State == ResumableTaskState.Running)
		{
			resumableTaskItem.State = ResumableTaskState.Paused;
			resumableTaskItem.Message = "启动器关闭后已暂停，可在任务中心继续";
		}
		return resumableTaskItem;
	}

	private static ResumableTaskItem Clone(ResumableTaskItem task)
	{
		return new ResumableTaskItem
		{
			Id = (string.IsNullOrWhiteSpace(task.Id) ? Guid.NewGuid().ToString("N") : task.Id),
			Kind = task.Kind,
			State = task.State,
			Title = task.Title,
			MinecraftVersion = task.MinecraftVersion,
			SourceInstanceId = task.SourceInstanceId,
			LoaderKind = task.LoaderKind,
			LoaderVersion = task.LoaderVersion,
			LoaderMavenVersion = task.LoaderMavenVersion,
			LoaderDownloadUrl = task.LoaderDownloadUrl,
			ResultInstanceId = task.ResultInstanceId,
			ModpackProject = ((task.ModpackProject == null) ? null : new ModpackProject
			{
				ProjectId = task.ModpackProject.ProjectId,
				Slug = task.ModpackProject.Slug,
				Title = task.ModpackProject.Title,
				Description = task.ModpackProject.Description,
				Author = task.ModpackProject.Author,
				IconUrl = task.ModpackProject.IconUrl,
				WebsiteUrl = task.ModpackProject.WebsiteUrl,
				Downloads = task.ModpackProject.Downloads,
				Categories = (task.ModpackProject.Categories?.ToList() ?? new List<string>()),
				GameVersions = (task.ModpackProject.GameVersions?.ToList() ?? new List<string>())
			}),
			ModpackVersion = ((task.ModpackVersion == null) ? null : new ModpackVersion
			{
				VersionId = task.ModpackVersion.VersionId,
				VersionNumber = task.ModpackVersion.VersionNumber,
				ReleaseType = task.ModpackVersion.ReleaseType,
				FileName = task.ModpackVersion.FileName,
				DownloadUrl = task.ModpackVersion.DownloadUrl,
				Sha1 = task.ModpackVersion.Sha1,
				Sha512 = task.ModpackVersion.Sha512,
				GameVersions = (task.ModpackVersion.GameVersions?.ToList() ?? new List<string>()),
				Loaders = (task.ModpackVersion.Loaders?.ToList() ?? new List<string>()),
				PublishedAt = task.ModpackVersion.PublishedAt
			}),
			Message = task.Message,
			CreatedAt = task.CreatedAt
		};
	}
}
