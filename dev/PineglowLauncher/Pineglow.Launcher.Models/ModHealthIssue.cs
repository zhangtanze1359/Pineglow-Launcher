namespace Pineglow.Launcher.Models;

public sealed record ModHealthIssue(ModHealthSeverity Severity, string Title, string Description);
