namespace Pineglow.Launcher.Services;

public sealed record AuthlibAccount(string UserName, string Uuid, string AccessToken, string ServerUrl, string SkinUrl = "", string CapeUrl = "", bool IsSlim = false);
