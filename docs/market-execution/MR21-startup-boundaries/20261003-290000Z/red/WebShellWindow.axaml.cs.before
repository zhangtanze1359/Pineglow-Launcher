using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Pineglow.Launcher.Views.Shell;

public sealed partial class WebShellWindow : Window
{
	[DllImport("user32.dll")]
	private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

	[DllImport("user32.dll")]
	private static extern bool GetCursorPos(out POINT pt);

	[DllImport("user32.dll")]
	private static extern bool GetWindowRect(IntPtr hWnd, out RECT rc);

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
	private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
	private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

	[DllImport("user32.dll")]
	private static extern int CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, ref int attrValue, int attrSize);

	// Win11 圆角：DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2
	private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
	private const int DWMWCP_ROUND = 2;

	private void ApplyRoundedCorners()
	{
		IntPtr handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
		if (handle == IntPtr.Zero)
		{
			return;
		}

		try
		{
			int pref = DWMWCP_ROUND;
			// 仅 Win11 (build 22000+) 支持；旧系统调用无害返回错误码，忽略即可
			DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
		}
		catch
		{
			// 非 Windows 或旧系统：忽略
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct POINT
	{
		public int X;
		public int Y;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct RECT
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	private delegate IntPtr Win32WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	private const uint SWP_NOSIZE = 0x0001;
	private const uint SWP_NOZORDER = 0x0004;

	private const int GWLP_WNDPROC = -4;
	private const uint WM_NCHITTEST = 0x0084;
	private const int HTLEFT = 10;
	private const int HTRIGHT = 11;
	private const int HTTOP = 12;
	private const int HTTOPLEFT = 13;
	private const int HTTOPRIGHT = 14;
	private const int HTBOTTOM = 15;
	private const int HTBOTTOMLEFT = 16;
	private const int HTBOTTOMRIGHT = 17;
	private const int RESIZE_BORDER_PX = 8;

	private IntPtr _prevWndProc;
	private Win32WndProc? _wndProcDelegate;

	private bool _dragging;
	private int _dragBaseX;
	private int _dragBaseY;
	private int _dragGrabX;
	private int _dragGrabY;

	public WebShellWindow()
	{
		InitializeComponent();
		Opened += OnWindowOpenedAsync;
		Closed += (_, _) => RestoreWindowProc();
	}

	private void InstallResizeHook()
	{
		IntPtr handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
		if (handle == IntPtr.Zero || _wndProcDelegate != null)
		{
			return;
		}

		_prevWndProc = GetWindowLongPtr(handle, GWLP_WNDPROC);
		_wndProcDelegate = WindowProc;
		SetWindowLongPtr(handle, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
	}

	private void RestoreWindowProc()
	{
		if (_wndProcDelegate == null)
		{
			return;
		}

		IntPtr handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
		if (handle != IntPtr.Zero)
		{
			SetWindowLongPtr(handle, GWLP_WNDPROC, _prevWndProc);
		}

		_wndProcDelegate = null;
	}

	private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
	{
		if (msg == WM_NCHITTEST && WindowState != WindowState.Maximized)
		{
			long raw = lParam.ToInt64();
			int x = (short)(raw & 0xFFFF);
			int y = (short)((raw >> 16) & 0xFFFF);

			if (GetWindowRect(hWnd, out RECT rc))
			{
				bool onLeft = x >= rc.Left && x <= rc.Left + RESIZE_BORDER_PX;
				bool onRight = x >= rc.Right - RESIZE_BORDER_PX && x <= rc.Right;
				bool onTop = y >= rc.Top && y <= rc.Top + RESIZE_BORDER_PX;
				bool onBottom = y >= rc.Bottom - RESIZE_BORDER_PX && y <= rc.Bottom;

				if (onTop && onLeft) return (IntPtr)HTTOPLEFT;
				if (onTop && onRight) return (IntPtr)HTTOPRIGHT;
				if (onBottom && onLeft) return (IntPtr)HTBOTTOMLEFT;
				if (onBottom && onRight) return (IntPtr)HTBOTTOMRIGHT;
				if (onLeft) return (IntPtr)HTLEFT;
				if (onRight) return (IntPtr)HTRIGHT;
				if (onTop) return (IntPtr)HTTOP;
				if (onBottom) return (IntPtr)HTBOTTOM;
			}
		}

		return (IntPtr)CallWindowProc(_prevWndProc, hWnd, msg, wParam, lParam);
	}

	private void OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs e)
	{
		string? debugPort = Environment.GetEnvironmentVariable("PINEGLOW_WEBVIEW_DEBUG");
		if (!string.IsNullOrEmpty(debugPort) && e is WindowsWebView2EnvironmentRequestedEventArgs webView2)
		{
			webView2.AdditionalBrowserArguments = "--remote-debugging-port=" + debugPort;
		}
	}

	private async void OnWindowOpenedAsync(object? sender, EventArgs e)
	{
		InstallResizeHook();
		ApplyRoundedCorners();

		// cache-busting：避免 WebView 缓存旧版 UI（每次启动加时间戳参数）
		string cacheBuster = "?v=" + DateTime.Now.ToString("yyyyMMddHHmmss");

		try
		{
			using var client = new HttpClient
			{
				Timeout = TimeSpan.FromSeconds(2)
			};
			using var response = await client.GetAsync("http://127.0.0.1:48123/api/health");
			if (!response.IsSuccessStatusCode)
			{
				throw new InvalidOperationException("API unavailable");
			}
			Web.Source = new Uri("http://127.0.0.1:48123/" + cacheBuster);
		}
		catch
		{
			string index = Path.Combine(AppContext.BaseDirectory, "UI", "index.html");
			Web.Source = File.Exists(index) ? new Uri(index) : new Uri("about:blank");
		}
	}

	private void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
	{
		string body = e.Body;
		if (string.IsNullOrEmpty(body))
		{
			return;
		}

		JsonDocument doc;
		try
		{
			doc = JsonDocument.Parse(body);
		}
		catch
		{
			return;
		}

		using (doc)
		{
			if (!doc.RootElement.TryGetProperty("type", out JsonElement typeElement) || typeElement.GetString() == null)
			{
				return;
			}

			string type = typeElement.GetString()!;
			if (type == "window-control")
			{
				string? action = doc.RootElement.TryGetProperty("action", out JsonElement actionElement) ? actionElement.GetString() : null;
				switch (action)
				{
					case "minimize":
						WindowState = WindowState.Minimized;
						break;
					case "toggle-maximize":
						WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
						break;
					case "close":
						Close();
						break;
				}
			}
			else if (type == "drag-start")
			{
				StartWindowDrag(doc);
			}
			else if (type == "drag-move")
			{
				ContinueWindowDrag(doc);
			}
			else if (type == "drag-end")
			{
				_dragging = false;
			}
		}
	}

	private void StartWindowDrag(JsonDocument doc)
	{
		if (WindowState == WindowState.Maximized)
		{
			return;
		}

		IntPtr handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
		if (handle == IntPtr.Zero)
		{
			return;
		}

		if (!GetCursorPos(out POINT pt))
		{
			return;
		}

		_dragging = true;
		_dragBaseX = Position.X;
		_dragBaseY = Position.Y;
		_dragGrabX = pt.X;
		_dragGrabY = pt.Y;
	}

	private void ContinueWindowDrag(JsonDocument doc)
	{
		if (!_dragging)
		{
			return;
		}

		IntPtr handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
		if (handle == IntPtr.Zero)
		{
			return;
		}

		if (!GetCursorPos(out POINT pt))
		{
			return;
		}

		SetWindowPos(handle, IntPtr.Zero,
			_dragBaseX + pt.X - _dragGrabX,
			_dragBaseY + pt.Y - _dragGrabY,
			0, 0, SWP_NOSIZE | SWP_NOZORDER);
	}
}
