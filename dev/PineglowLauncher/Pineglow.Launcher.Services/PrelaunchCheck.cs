using System.Collections.Generic;

namespace Pineglow.Launcher.Services;

/// <summary>
/// M2-1④：启动前自检的**结构化问题项**。
///
/// 由来（接口约束，见 v1.1.0 计划第三节）：
/// 原实现只返回 <c>errors</c> / <c>warnings</c> 两个中文字符串数组，前端要做
/// 「发现 Java 缺失 → 跳去选 Java」就只能去匹配中文文本，而计划明确禁止这种做法
/// （文案一改引导就失效，且中英文下必然错一半）。
///
/// 因此这里给每条问题加上三个机器可读字段：
///   <see cref="Code"/>     稳定问题码，用于日志/埋点/测试断言，**永不当文案用**；
///   <see cref="Severity"/> error | warning，决定是"拦住启动"还是"提示后可继续"；
///   <see cref="Action"/>   可选的处理动作标识，前端据此决定按钮去跳哪里。
///
/// 兼容性：<c>errors</c> / <c>warnings</c> 数组**原样保留**，只是内容改由本列表派生，
/// 保证旧前端（及任何按字符串处理的外部调用）行为不变。
/// </summary>
public sealed record PrelaunchIssue(
	string Code,
	string Severity,
	string Message,
	string? Action,
	string? ActionLabel)
{
	public const string SeverityError = "error";

	public const string SeverityWarning = "warning";

	public bool IsError => Severity == SeverityError;

	public static PrelaunchIssue Error(string code, string message, string? action = null, string? actionLabel = null) =>
		new PrelaunchIssue(code, SeverityError, message, action, actionLabel);

	public static PrelaunchIssue Warning(string code, string message, string? action = null, string? actionLabel = null) =>
		new PrelaunchIssue(code, SeverityWarning, message, action, actionLabel);
}

/// <summary>
/// 问题码常量。命名约定 <c>域.现象</c>，与 <see cref="PrelaunchActions"/> 一一对应。
/// 增加新问题时**必须**同时在这里登记，避免前端出现拼写漂移（测试会做白名单校验）。
/// </summary>
public static class PrelaunchIssueCodes
{
	public const string InstanceNoVersion = "instance.no-version";

	public const string JavaMissing = "java.missing";

	public const string JavaResolveFailed = "java.resolve-failed";

	public const string GameDirMissing = "gamedir.missing";

	public const string DiskLowSpace = "disk.low-space";

	public const string MemoryNearLimit = "memory.near-limit";

	/// <summary>全部已知问题码。测试用它做「新增问题码必须登记」的门槛。</summary>
	public static readonly string[] All =
	{
		InstanceNoVersion,
		JavaMissing,
		JavaResolveFailed,
		GameDirMissing,
		DiskLowSpace,
		MemoryNearLimit,
	};
}

/// <summary>
/// 处理动作标识。前端只按这些常量做分支，**不解析任何中文文案**。
///
/// 刻意做得很少：每个动作都必须对应界面上一个真实可达的入口，否则就是"点了没反应"，
/// 比不给按钮更糟。加动作前先确认目标界面存在。
/// </summary>
public static class PrelaunchActions
{
	/// <summary>去"下载/安装"页装一个游戏版本。</summary>
	public const string InstallVersion = "install-version";

	/// <summary>去设置页 Java 分区选择本地 Java。</summary>
	public const string SelectJava = "select-java";

	/// <summary>去该实例的设置调整内存 / 游戏目录。</summary>
	public const string OpenInstanceSettings = "open-instance-settings";

	public static readonly string[] All = { InstallVersion, SelectJava, OpenInstanceSettings };
}

/// <summary>
/// 启动前自检的输入事实。全部由 handler 采集（IO），判定逻辑**不看 IO**，
/// 因此本类型可以脱离文件系统直测。
/// </summary>
public sealed record PrelaunchFacts(
	string MinecraftVersion,
	string JavaPath,
	bool JavaPathExists,
	string JavaResolveError,
	string GameDirectory,
	bool GameDirectoryExists,
	long DriveFreeBytes,
	long AvailableMemoryBytes,
	int MaxMemoryMb)
{
	public const long LowDiskThresholdBytes = 512L * 1024 * 1024;

	/// <summary>可用内存占比达到该值时提示（不是错误，只是可能卡顿）。</summary>
	public const double MemoryNearLimitRatio = 0.9;
}

/// <summary>
/// M2-1④ 的判定核心：把事实转成结构化问题列表。纯函数，无 IO、无状态。
///
/// 顺序即优先级：**错误按"越靠前越该先修"排列**，前端可据此决定引导先跳哪里。
/// 版本缺失排第一，因为它会让 Java 解析本身失去意义（没有版本就不知道要哪个 Java）。
/// </summary>
public static class PrelaunchCheckEvaluator
{
	public static List<PrelaunchIssue> Evaluate(PrelaunchFacts facts)
	{
		var issues = new List<PrelaunchIssue>();

		if (string.IsNullOrWhiteSpace(facts.MinecraftVersion))
		{
			issues.Add(PrelaunchIssue.Error(
				PrelaunchIssueCodes.InstanceNoVersion,
				"实例未关联游戏版本，请先在设置中安装版本",
				PrelaunchActions.InstallVersion,
				"去安装版本"));
		}

		if (!string.IsNullOrWhiteSpace(facts.JavaResolveError))
		{
			// 解析阶段就抛异常，通常意味着配置里的 Java 路径不可用或实例配置损坏。
			// 与"路径存在但文件没了"分开成两个码：一个该去重新选，一个该去检查磁盘。
			issues.Add(PrelaunchIssue.Error(
				PrelaunchIssueCodes.JavaResolveFailed,
				facts.JavaResolveError,
				PrelaunchActions.SelectJava,
				"选择 Java"));
		}
		else if (!facts.JavaPathExists)
		{
			issues.Add(PrelaunchIssue.Error(
				PrelaunchIssueCodes.JavaMissing,
				"Java 运行时不存在：" + facts.JavaPath,
				PrelaunchActions.SelectJava,
				"选择 Java"));
		}

		if (!facts.GameDirectoryExists)
		{
			issues.Add(PrelaunchIssue.Warning(
				PrelaunchIssueCodes.GameDirMissing,
				"游戏目录不存在（首次启动将自动创建）：" + facts.GameDirectory,
				PrelaunchActions.OpenInstanceSettings,
				"打开实例设置"));
		}

		if (facts.DriveFreeBytes > 0 && facts.DriveFreeBytes < PrelaunchFacts.LowDiskThresholdBytes)
		{
			issues.Add(PrelaunchIssue.Warning(
				PrelaunchIssueCodes.DiskLowSpace,
				$"磁盘剩余空间不足：{FormatBytes(facts.DriveFreeBytes)}，建议保留至少 512MB",
				PrelaunchActions.OpenInstanceSettings,
				"打开实例设置"));
		}

		// 注意单位：MaxMemoryMb 是 MB，AvailableMemoryBytes 是字节。
		// 原实现两边都是 MB 所以直接比；这里统一换成字节，避免"看起来对但差 1024² 倍"这类错误
		// （改成字节比较时曾漏乘，被 PrelaunchCheckEvaluatorTests 的边界用例抓到）。
		long requestedBytes = (long)facts.MaxMemoryMb * 1024 * 1024;
		if (facts.AvailableMemoryBytes > 0
			&& requestedBytes >= facts.AvailableMemoryBytes * PrelaunchFacts.MemoryNearLimitRatio)
		{
			issues.Add(PrelaunchIssue.Warning(
				PrelaunchIssueCodes.MemoryNearLimit,
				$"分配内存 {facts.MaxMemoryMb}MB 接近当前可用内存 {facts.AvailableMemoryBytes / (1024 * 1024)}MB，可能导致卡顿或启动失败",
				PrelaunchActions.OpenInstanceSettings,
				"打开实例设置"));
		}

		return issues;
	}

	/// <summary>与 <c>LocalApiService</c> 里同名方法保持一致的口径（测试会对照两者的输出）。</summary>
	internal static string FormatBytes(long bytes)
	{
		string[] units = { "B", "KB", "MB", "GB" };
		double value = bytes;
		int idx = 0;
		while (value >= 1024.0 && idx < units.Length - 1)
		{
			value /= 1024.0;
			idx++;
		}

		return $"{value:0.##} {units[idx]}";
	}
}
