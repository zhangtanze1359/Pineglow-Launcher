namespace Pineglow.Launcher.Services;

public sealed record MemoryRecommendation(int RecommendedMb, int MinimumMb, int MaximumMb, int TotalSystemMemoryMb, int EnabledModCount);
