using System;
using System.Collections.Generic;
using System.Linq;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public static class ModpackLifecycleService
{
	public const string ModrinthSource = "modrinth";

	public static bool IsTrackableModrinth(GameInstance? instance)
	{
		ModpackInstallation modpackInstallation = instance?.Modpack;
		if (modpackInstallation != null && modpackInstallation.Source.Equals("modrinth", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(modpackInstallation.ProjectId))
		{
			return !string.IsNullOrWhiteSpace(modpackInstallation.VersionId);
		}
		return false;
	}

	public static void SetModrinthSource(GameInstance instance, ModpackProject project, ModpackVersion version)
	{
		instance.Modpack = new ModpackInstallation
		{
			Source = "modrinth",
			ProjectId = project.ProjectId,
			ProjectName = project.Title,
			VersionId = version.VersionId,
			VersionName = version.VersionNumber,
			WebsiteUrl = project.WebsiteUrl,
			InstalledAt = DateTimeOffset.UtcNow
		};
	}

	public static ModpackVersion? FindAvailableUpdate(GameInstance instance, IEnumerable<ModpackVersion> versions)
	{
		if (!IsTrackableModrinth(instance))
		{
			return null;
		}
		return (from version in versions
			where !version.VersionId.Equals(instance.Modpack.VersionId, StringComparison.OrdinalIgnoreCase)
			orderby version.ReleaseType.Equals("release", StringComparison.OrdinalIgnoreCase) descending, version.PublishedAt descending
			select version).FirstOrDefault();
	}

	public static ModpackInstallation? CloneSource(ModpackInstallation? source)
	{
		if (source != null)
		{
			return new ModpackInstallation
			{
				Source = source.Source,
				ProjectId = source.ProjectId,
				ProjectName = source.ProjectName,
				VersionId = source.VersionId,
				VersionName = source.VersionName,
				WebsiteUrl = source.WebsiteUrl,
				InstalledAt = source.InstalledAt
			};
		}
		return null;
	}
}
