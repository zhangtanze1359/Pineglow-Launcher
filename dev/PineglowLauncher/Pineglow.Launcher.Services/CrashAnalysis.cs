using System.Collections.Generic;

namespace Pineglow.Launcher.Services;

public sealed record CrashAnalysis(string Summary, IReadOnlyList<string> Suggestions, string? SourceFile);
