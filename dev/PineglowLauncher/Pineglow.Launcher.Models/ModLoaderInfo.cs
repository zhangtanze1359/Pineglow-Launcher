using System;

namespace Pineglow.Launcher.Models;

public static class ModLoaderInfo
{
	public static readonly ModLoaderKind[] SupportedKinds = new ModLoaderKind[5]
	{
		ModLoaderKind.Fabric,
		ModLoaderKind.Forge,
		ModLoaderKind.NeoForge,
		ModLoaderKind.Quilt,
		ModLoaderKind.OptiFine
	};

	public static string DisplayName(ModLoaderKind kind)
	{
		if (kind == ModLoaderKind.OptiFine)
		{
			return "OptiFine";
		}
		return kind.ToString();
	}

	public static bool TryParse(string? value, out ModLoaderKind kind)
	{
		if (Enum.TryParse<ModLoaderKind>(value, ignoreCase: true, out kind))
		{
			return true;
		}
		if (string.Equals(value, "Optifine", StringComparison.OrdinalIgnoreCase))
		{
			kind = ModLoaderKind.OptiFine;
			return true;
		}
		return false;
	}

	public static bool IsModLoader(string? value)
	{
		ModLoaderKind kind;
		return TryParse(value, out kind);
	}

	public static string Normalize(string? value)
	{
		if (!TryParse(value, out var kind))
		{
			return "Vanilla";
		}
		return DisplayName(kind);
	}
}
