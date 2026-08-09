using System;
using System.IO;
using Avalonia;
using Avalonia.Logging;

namespace Pineglow.Launcher;

internal static class Program
{
	[STAThread]
	public static void Main(string[] args)
	{
		try
		{
			BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
		}
		catch (Exception ex)
		{
			try
			{
				string path = Path.Combine(AppContext.BaseDirectory, "crash.log");
				File.WriteAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + ex);
			}
			catch
			{
			}
			throw;
		}
	}

	public static AppBuilder BuildAvaloniaApp()
	{
		return AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont()
			.LogToTrace(LogEventLevel.Warning);
	}
}
