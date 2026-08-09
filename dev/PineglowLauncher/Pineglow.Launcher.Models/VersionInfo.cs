using System.Collections.Generic;

namespace Pineglow.Launcher.Models;

public sealed class VersionInfo
{
	public string Id { get; set; } = string.Empty;

	public string Type { get; set; } = string.Empty;

	public string JsonPath { get; set; } = string.Empty;

	public string JarPath { get; set; } = string.Empty;

	public string Status { get; set; } = string.Empty;

	public int ProblemCount { get; set; }

	public List<string> MissingFiles { get; set; } = new List<string>();

	public override string ToString()
	{
		return Id + "  [" + Type + "]";
	}
}
