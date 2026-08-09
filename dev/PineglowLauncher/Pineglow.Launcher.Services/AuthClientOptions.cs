using System;

namespace Pineglow.Launcher.Services;

public static class AuthClientOptions
{
	public const string BuiltInMicrosoftClientId = "60934999-9c8b-4731-8b78-a98d010edcde";

	public const string ClientIdEnvironmentVariable = "MCMINI_MS_CLIENT_ID";

	public static string ResolveMicrosoftClientId(string? existingAccountClientId = null)
	{
		if (IsGuidLike(existingAccountClientId))
		{
			return existingAccountClientId.Trim();
		}
		string environmentVariable = Environment.GetEnvironmentVariable("MCMINI_MS_CLIENT_ID");
		if (IsGuidLike(environmentVariable))
		{
			return environmentVariable.Trim();
		}
		if (IsGuidLike("60934999-9c8b-4731-8b78-a98d010edcde"))
		{
			return "60934999-9c8b-4731-8b78-a98d010edcde".Trim();
		}
		return string.Empty;
	}

	public static bool HasUsableMicrosoftClientId(string? existingAccountClientId = null)
	{
		return !string.IsNullOrWhiteSpace(ResolveMicrosoftClientId(existingAccountClientId));
	}

	private static bool IsGuidLike(string? value)
	{
		Guid result;
		if (!string.IsNullOrWhiteSpace(value))
		{
			return Guid.TryParse(value.Trim(), out result);
		}
		return false;
	}
}
