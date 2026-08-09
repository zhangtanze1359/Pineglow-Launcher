using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Pineglow.Launcher.Services;
using Pineglow.Launcher.Views.Shell;

namespace Pineglow.Launcher;

public sealed class App : Application
{
	public override void Initialize()
	{
		AvaloniaXamlLoader.Load(this);
	}

	public override void OnFrameworkInitializationCompleted()
	{
		LauncherLog.Init();
		LauncherLog.Info("=== Pineglow Launcher 启动 v" + LauncherService.LauncherVersion + " ===");

		if (Environment.GetCommandLineArgs().Contains("--no-api"))
		{
			// 显式禁用本地 API（调试用）
			LauncherLog.Info("--no-api 指定，本地 API 未启动");
		}
		else
		{
			try
			{
				LocalApiService.Start();
			}
			catch (Exception ex)
			{
				LauncherLog.Error("[LocalApi] 启动失败: " + ex.Message, ex);
				Console.Error.WriteLine("[LocalApi] 启动失败: " + ex.Message);
			}
		}

		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime classicDesktopStyleApplicationLifetime)
		{
			classicDesktopStyleApplicationLifetime.MainWindow = new WebShellWindow();
			classicDesktopStyleApplicationLifetime.ShutdownRequested += (_, _) =>
			{
				LauncherLog.Info("=== Pineglow Launcher 退出 ===");
				LocalApiService.Stop();
			};
		}
		base.OnFrameworkInitializationCompleted();
	}
}
