namespace Pineglow.Launcher.Services;

public static class DownloadTaskStatus
{
	public static string Running(string version)
	{
		return "正在安装 Minecraft " + version + "，请保持网络连接…";
	}

	public static string Completed(string version)
	{
		return "Minecraft " + version + " 安装完成，可前往启动页使用。";
	}

	public static string Failed(string version)
	{
		return "Minecraft " + version + " 安装失败，请展开日志查看错误详情后重试。";
	}

	public static string Cancelled(string version)
	{
		return "Minecraft " + version + " 安装已取消。";
	}
}
