using Avalonia;
using Avalonia.Styling;

namespace Pineglow.Launcher.Services;

/// <summary>主题切换服务（设计文档 §5.1/§8.2）。</summary>
public static class ThemeService
{
	/// <summary>
	/// 应用主题模式。
	/// </summary>
	/// <param name="mode">"system" 跟随系统 / "dark" / "light"。</param>
	public static void Apply(string mode)
	{
		if (Application.Current is not { } app)
		{
			return;
		}
		app.RequestedThemeVariant = mode switch
		{
			"dark" => ThemeVariant.Dark,
			"light" => ThemeVariant.Light,
			_ => ThemeVariant.Default
		};
	}
}
