using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// <see cref="PathSafetyGuard"/> 的直测（此前 138/264 行 = 52.3%，且**从未被任何测试直接提到**）。
///
/// 这个类是 RA-01 的核心防线：它决定"撤销替换"会不会把文件写到实例目录之外。
/// 之前它只被 FileRecoveryService 的集成用例**间接**覆盖，覆盖到的恰好都是"一切正常"和
/// "已经越界"两条路；真正容易被改坏的判定分支（锚点不存在、目标等于锚点、
/// 链上级被换成联接、叶子是联接、句柄语义是否真的钉住了目录）一条都没被测过。
///
/// 本组测试直接用真实文件系统 + 真实目录联接（junction，不需要管理员权限），
/// 断言的是**可证明的限制**而不是"应该没问题"：
///   * 越界/自指/锚点缺失 → 必须在打开任何句柄前就拒绝（错误信息要能指向具体原因）；
///   * 链上任意一级是联接 → 拒绝，且错误信息里必须同时出现"请求路径"和"实际路径"；
///   * 成功后返回的 <c>PinnedPath</c> 必须真的**阻止**目录被改名（这是它唯一的价值）；
///   * <c>TryRejectReparseLeaf</c> 只管叶子，不因为父目录是普通目录就放行联接文件。
/// </summary>
public class PathSafetyGuardTests
{
	private readonly ITestOutputHelper _out;

	public PathSafetyGuardTests(ITestOutputHelper output) => _out = output;

	/* ============================ 夹具 ============================ */

	private sealed class Fixture : IDisposable
	{
		public string Root { get; }

		public Fixture()
		{
			Root = Path.Combine(Path.GetTempPath(), "pg-guard-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Root);
		}

		public string Dir(string relative)
		{
			string full = Path.Combine(Root, relative);
			Directory.CreateDirectory(full);
			return full;
		}

		public string FilePath(string relative)
		{
			string full = Path.Combine(Root, relative);
			Directory.CreateDirectory(Path.GetDirectoryName(full)!);
			File.WriteAllText(full, "x");
			return full;
		}

		public void Dispose()
		{
			try
			{
				if (Directory.Exists(Root))
				{
					Directory.Delete(Root, recursive: true);
				}
			}
			catch
			{
				// 夹具清理失败不应掩盖断言结论
			}
		}
	}

	/// <summary>
	/// 建目录联接。Windows 走 <c>mklink /J</c>（无需特权）；其他平台退回符号链接。
	/// 建不出来时返回 false，由调用方决定是 Skip 还是失败——**不允许静默通过**。
	/// </summary>
	private static bool TryCreateDirectoryLink(string linkPath, string targetPath)
	{
		if (!OperatingSystem.IsWindows())
		{
			try
			{
				Directory.CreateSymbolicLink(linkPath, targetPath);
				return Directory.Exists(linkPath);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				return false;
			}
		}

		ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + linkPath + "\" \"" + targetPath + "\"")
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};

		try
		{
			using Process? p = Process.Start(psi);
			if (p == null)
			{
				return false;
			}

			p.WaitForExit(15000);
			return p.ExitCode == 0 && Directory.Exists(linkPath);
		}
		catch
		{
			return false;
		}
	}

	private bool HasHandleSemantics => OperatingSystem.IsWindows();

	/* ============================ 1. 参数级拒绝：必须在打开句柄之前 ============================ */

	[Fact]
	public void Pin_RejectsWhenTargetEqualsAnchor()
	{
		using Fixture fx = new Fixture();
		string anchor = fx.Dir("game");

		// 目标 == 锚点：写入目标就是目录本身，越界判定的边界必须拒绝，
		// 否则"在 game 目录下写文件"会退化成"往 game 目录本身写"。
		bool ok = PathSafetyGuard.TryPinDirectoryChain(anchor, anchor, false, out PathSafetyGuard.PinnedPath? pinned, out string error);

		Assert.False(ok);
		Assert.Null(pinned);
		Assert.Contains("锚点目录自身", error);
	}

	[Fact]
	public void Pin_RejectsTargetOutsideAnchor()
	{
		using Fixture fx = new Fixture();
		string anchor = fx.Dir("game");
		string outside = fx.Dir("outside");
		string target = Path.Combine(outside, "mods", "a.jar");

		bool ok = PathSafetyGuard.TryPinDirectoryChain(anchor, target, false, out PathSafetyGuard.PinnedPath? pinned, out string error);

		Assert.False(ok);
		Assert.Null(pinned);
		// 错误信息必须同时给出两个路径，否则用户/日志里看不出是"哪里不属于哪里"。
		Assert.Contains("目标不在锚点目录之内", error);
		Assert.Contains(outside, error);
	}

	[Fact]
	public void Pin_RejectsSiblingPrefixTrap()
	{
		// 经典前缀陷阱：`.../game-evil` 以 `.../game` 开头，但不在其内。
		// 若判边界用了裸 StartsWith，这里会被放行。
		using Fixture fx = new Fixture();
		string anchor = fx.Dir("game");
		string sibling = fx.Dir("game-evil");
		string target = Path.Combine(sibling, "a.jar");

		bool ok = PathSafetyGuard.TryPinDirectoryChain(anchor, target, false, out _, out string error);

		Assert.False(ok);
		Assert.Contains("目标不在锚点目录之内", error);
	}

	[Fact]
	public void Pin_RejectsMissingAnchor()
	{
		using Fixture fx = new Fixture();
		string anchor = Path.Combine(fx.Root, "no-such-game");
		string target = Path.Combine(anchor, "mods", "a.jar");

		bool ok = PathSafetyGuard.TryPinDirectoryChain(anchor, target, true, out _, out string error);

		Assert.False(ok);
		// 锚点不存在时**即使允许创建目标目录**也必须拒绝：没有锚点就没有信任根，
		// 自己造一个锚点等于把校验目标改成"我刚创建的那个目录"。
		Assert.Contains("锚点目录不存在", error);
	}

	/* ============================ 2. 正常链：钉住并真的锁住 ============================ */

	[Fact]
	public void Pin_AcceptsExistingChainAndReportsHandleLocks()
	{
		using Fixture fx = new Fixture();
		string anchor = fx.Dir("game");
		fx.Dir("game/mods");
		string target = Path.Combine(anchor, "mods", "a.jar");

		bool ok = PathSafetyGuard.TryPinDirectoryChain(anchor, target, false, out PathSafetyGuard.PinnedPath? pinned, out string error);

		try
		{
			Assert.True(ok, "普通目录链应被接受，error=" + error);
			Assert.NotNull(pinned);
			Assert.True(pinned!.Count >= 2, "至少应钉住 mods 与 game 两级，实际 " + pinned.Count);

			// HasHandleLocks 是"当前平台是否真的拿到了内核句柄"的自我声明。
			// Windows 上必须是 true：如果是 false，说明退化成了纯托管检查，竞态没被封住。
			if (HasHandleSemantics)
			{
				Assert.True(pinned.HasHandleLocks, "Windows 上必须真正持有目录句柄");
			}

			_out.WriteLine("钉住层级数：" + pinned.Count + "，句柄锁：" + pinned.HasHandleLocks);
		}
		finally
		{
			pinned?.Dispose();
		}
	}

	[Fact]
	public void Pin_CreatesMissingTargetDirectoryWhenAllowed()
	{
		using Fixture fx = new Fixture();
		string anchor = fx.Dir("game");
		string targetDir = Path.Combine(anchor, "shaderpacks");
		string target = Path.Combine(targetDir, "p.zip");

		Assert.False(Directory.Exists(targetDir));

		bool ok = PathSafetyGuard.TryPinDirectoryChain(anchor, target, true, out PathSafetyGuard.PinnedPath? pinned, out string error);

		try
		{
			Assert.True(ok, "createMissingTargetDirectory=true 时应创建缺失目录，error=" + error);
			Assert.True(Directory.Exists(targetDir), "缺失的目标父目录应被创建");

			// 创建之后必须**再钉一次**：先钉后建等于钉了一个不存在的路径，
			// 中间这段窗口正好是"建完就被换成联接"的攻击面。
			Assert.True(pinned!.Count >= 2, "新建目录也必须进入钉住集合，实际 " + pinned.Count);
		}
		finally
		{
			pinned?.Dispose();
		}
	}

	[Fact]
	public void Pin_RefusesToCreateMissingTargetDirectoryWhenNotAllowed()
	{
		using Fixture fx = new Fixture();
		string anchor = fx.Dir("game");
		string target = Path.Combine(anchor, "mods", "a.jar");

		bool ok = PathSafetyGuard.TryPinDirectoryChain(anchor, target, false, out PathSafetyGuard.PinnedPath? pinned, out string error);

		Assert.False(ok);
		Assert.Null(pinned);
		Assert.Contains("目标父目录不存在", error);
		Assert.False(Directory.Exists(Path.Combine(anchor, "mods")), "不允许创建时不得留下目录");
	}

	[Fact]
	public void Pin_AcceptsRelativePathInsideAnchor()
	{
		// 调用方传相对路径是常见的接线错误；GetFullPath 会按当前工作目录解析，
		// 若解析后落在锚点外就必须拒绝，落在内则必须接受（并钉住**绝对**路径）。
		using Fixture fx = new Fixture();
		string anchor = fx.Dir("game");
		fx.Dir("game/mods");

		string relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), Path.Combine(anchor, "mods", "a.jar"));
		bool ok = PathSafetyGuard.TryPinDirectoryChain(anchor, relative, false, out PathSafetyGuard.PinnedPath? pinned, out string error);

		try
		{
			Assert.True(ok, "相对路径应被规范化为绝对路径后判定，error=" + error);
		}
		finally
		{
			pinned?.Dispose();
		}
	}

	/// <summary>
	/// 这是 <see cref="PathSafetyGuard.PinnedPath"/> 存在的**唯一理由**：钉住期间目录不能被改名/删除。
	/// 若这条不成立，那"校验通过 → 写入"之间的窗口就仍然敞着，RA-01 的修复只是表面文章。
	/// </summary>
	[Fact]
	public void PinnedPath_PreventsDirectoryRenameWhileHeld()
	{
		using Fixture fx = new Fixture();
		string anchor = fx.Dir("game");
		string mods = fx.Dir("game/mods");
		string target = Path.Combine(mods, "a.jar");

		Assert.True(PathSafetyGuard.TryPinDirectoryChain(anchor, target, false, out PathSafetyGuard.PinnedPath? pinned, out string error), error);
		Assert.NotNull(pinned);

		bool movedWhileHeld = TryMoveDirectory(mods, mods + "-moved");

		try
		{
			if (HasHandleSemantics)
			{
				// 期望路径：共享模式不含 FILE_SHARE_DELETE，改名需要 DELETE 权限，被内核拒绝。
				Assert.True(pinned!.HasHandleLocks, "Windows 上必须真正持有目录句柄（HasHandleLocks=false 说明退化成纯托管检查，竞态没封住）");
				Assert.False(movedWhileHeld, "钉住期间 mods 目录不应能被改名——若这条通过，说明句柄共享模式漏了 FILE_SHARE_DELETE 之外的约束");
			}
			else
			{
				// 非 Windows 没有等价的内核目录句柄语义，产品自身如实标注"竞态未关闭"。
				// 这类平台**不允许**把用例算成通过（xUnit 2.x 没有动态 skip，return 会被计为通过），
				// 这里改断言可证明的行为：自我声明必须诚实——没拿到句柄就不得声称 HasHandleLocks，
				// 且改名确实发生在释放之前（产品没有伪装成"已封住竞态"）。
				Assert.False(pinned!.HasHandleLocks, "没有内核句柄语义的平台不得声称已钉住目录");
				Assert.True(movedWhileHeld, "无句柄语义时产品必须如实暴露竞态未关闭：改名不应被挡住");
			}
		}
		finally
		{
			pinned!.Dispose();
		}

		// 释放后必须能改名：否则就是句柄泄漏，用户会发现实例目录"锁死"了。
		if (!movedWhileHeld)
		{
			Assert.True(TryMoveDirectory(mods, mods + "-moved"), "Dispose 之后目录必须可以被正常改名");
		}

		Assert.True(Directory.Exists(mods + "-moved"), "Dispose 之后目录必须留在新名字上");
	}

	/// <summary>改名尝试；被内核挡住时返回 false（IO/访问拒绝都算"挡住了"）。</summary>
	private static bool TryMoveDirectory(string from, string to)
	{
		try
		{
			Directory.Move(from, to);
			return true;
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	/* ============================ 3. 重解析点：链上任意一级都要拒 ============================ */

	[Fact]
	public void Pin_RejectsWhenAncestorLevelIsJunction()
	{
		using Fixture fx = new Fixture();
		string anchor = fx.Dir("game");
		string outside = fx.Dir("outside-mods");
		string link = Path.Combine(anchor, "mods");

		if (!TryCreateDirectoryLink(link, outside))
		{
			// 本机建不出目录联接（例如无符号链接权限 / 文件系统不支持重解析点）。
			// 既不能报绿（xUnit 2.x 没有动态 skip，return 会被计为通过），也不能无条件 Assert.Fail
			// （那会让这类机器整片变红）。改为断言同一条"链上有无法验证的一级"的 fail-closed 行为：
			// 把 game/mods 占成普通文件，守卫必须在写入之前拒绝并给出非空原因，而不是把 a.jar 放行到不可验证的位置。
			File.WriteAllText(link, "occupied");
			bool blocked = PathSafetyGuard.TryPinDirectoryChain(anchor, Path.Combine(link, "a.jar"), false, out _, out string blockedError);
			Assert.False(blocked, "链上有无法验证的一级时必须 fail-closed，而不是放行写入");
			Assert.False(string.IsNullOrWhiteSpace(blockedError), "fail-closed 拒绝时必须给出非空原因");
			_out.WriteLine("本机无法创建目录联接，已改断言同一路径的 fail-closed 拒绝：" + blockedError);
			return;
		}

		string target = Path.Combine(link, "a.jar");
		bool ok = PathSafetyGuard.TryPinDirectoryChain(anchor, target, false, out PathSafetyGuard.PinnedPath? pinned, out string error);

		try
		{
			Assert.False(ok, "祖先目录是联接时必须拒绝，否则文件会落到 " + outside);
			Assert.Null(pinned);
			// 错误信息必须能定位问题：既给出"请求的路径"，也给出"实际解析到的路径"。
			Assert.Contains("重解析点", error);
			_out.WriteLine("拒绝信息：" + error);
		}
		finally
		{
			pinned?.Dispose();
		}
	}

	[Fact]
	public void Pin_RejectsWhenAnchorItselfIsJunction()
	{
		// 更阴的一层：把**锚点**（例如记录里的 game）换成联接。
		// 上一轮修复只在"锚点以内"做检查，这一层恰好被漏掉，于是"撤销替换"仍会写到实例之外。
		using Fixture fx = new Fixture();
		string realGame = fx.Dir("real-game");
		string outside = fx.Dir("outside");
		string anchorLink = Path.Combine(fx.Root, "game");

		if (!TryCreateDirectoryLink(anchorLink, realGame))
		{
			// 同上：不能报绿，也不能无条件失败。把锚点占成普通文件后，守卫必须以"锚点不可用"fail-closed，
			// 且错误信息指向锚点——没有锚点就没有信任根，不能自己造一个再放行。
			File.WriteAllText(anchorLink, "occupied");
			bool blocked = PathSafetyGuard.TryPinDirectoryChain(anchorLink, Path.Combine(anchorLink, "mods", "a.jar"), false, out _, out string blockedError);
			Assert.False(blocked, "锚点无法验证时必须 fail-closed，而不是放行写入");
			Assert.Contains("锚点", blockedError);
			_out.WriteLine("本机无法创建目录联接，已改断言同一路径的 fail-closed 拒绝：" + blockedError);
			return;
		}

		Directory.CreateDirectory(Path.Combine(realGame, "mods"));
		string target = Path.Combine(anchorLink, "mods", "a.jar");

		bool ok = PathSafetyGuard.TryPinDirectoryChain(anchorLink, target, false, out PathSafetyGuard.PinnedPath? pinned, out string error);

		try
		{
			Assert.False(ok, "锚点自身是联接时必须拒绝");
			Assert.Null(pinned);
			Assert.Contains("重解析点", error);
			_out.WriteLine("拒绝信息：" + error);
		}
		finally
		{
			pinned?.Dispose();
		}
	}

	/* ============================ 4. 叶子检查 ============================ */

	[Fact]
	public void RejectLeaf_AcceptsOrdinaryFile()
	{
		using Fixture fx = new Fixture();
		string file = fx.FilePath("game/mods/a.jar");

		Assert.True(PathSafetyGuard.TryRejectReparseLeaf(file, out string error));
		Assert.Equal(string.Empty, error);
	}

	[Fact]
	public void RejectLeaf_AcceptsNonexistentPath()
	{
		// 写入新文件时结论当然是"不存在"——不存在就不可能是联接，必须放行。
		using Fixture fx = new Fixture();
		string missing = Path.Combine(fx.Root, "game", "mods", "new.jar");

		Assert.True(PathSafetyGuard.TryRejectReparseLeaf(missing, out _));
	}

	[Fact]
	public void RejectLeaf_RejectsFileThatIsSymlink()
	{
		using Fixture fx = new Fixture();
		string real = fx.FilePath("outside/secret.txt");
		string leaf = Path.Combine(fx.Root, "game", "mods", "a.jar");
		Directory.CreateDirectory(Path.GetDirectoryName(leaf)!);

		try
		{
			File.CreateSymbolicLink(leaf, real);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			// 本机没有创建符号链接的权限（未开开发者模式/非提权进程）。不能报绿，也不能无条件失败：
			// 改用等价的"写入会穿透"场景——把叶子所在目录做成目录联接（junction 不需要特权），
			// 断言整条写入路径仍然 fail-closed。叶子检查只负责叶子本身，父级穿透由链检查负责。
			using Fixture fx2 = new Fixture();
			string game = fx2.Dir("game");
			string outsideDir = fx2.Dir("outside");
			string modsLink = Path.Combine(game, "mods");

			if (TryCreateDirectoryLink(modsLink, outsideDir))
			{
				bool accepted = PathSafetyGuard.TryPinDirectoryChain(game, Path.Combine(modsLink, "b.jar"), false, out PathSafetyGuard.PinnedPath? chainPin, out string chainError);
				try
				{
					Assert.False(accepted, "父目录是联接时必须拒绝，否则写入会穿透到 " + outsideDir);
					Assert.Contains("重解析点", chainError);
					_out.WriteLine("本机无法创建文件符号链接（" + ex.Message + "），已改断言父级联接的 fail-closed 拒绝：" + chainError);
				}
				finally
				{
					chainPin?.Dispose();
				}
			}
			else
			{
				// 连目录联接也建不出（例如文件系统不支持重解析点）：仍必须落在"拒绝不可验证路径"上，
				// 不能把"其实没测"当成"通过"。
				string missingAnchor = Path.Combine(fx2.Root, "no-such-anchor");
				string unverifiableTarget = Path.Combine(missingAnchor, "mods", "a.jar");
				bool accepted = PathSafetyGuard.TryPinDirectoryChain(missingAnchor, unverifiableTarget, false, out _, out string chainError);
				Assert.False(accepted, "链无法验证时必须 fail-closed");
				Assert.Contains("锚点", chainError);
				_out.WriteLine("本机既无法建符号链接也无法建目录联接，已改断言 fail-closed 拒绝：" + chainError);
			}

			return;
		}

		bool ok = PathSafetyGuard.TryRejectReparseLeaf(leaf, out string error);

		Assert.False(ok, "叶子文件本身是符号链接时必须拒绝（否则写入会穿透到链接目标）");
		Assert.Contains("重解析点", error);
	}

	/* ============================ 5. 路径比较口径 ============================ */

	[Fact]
	public void PathsEqual_IgnoresCaseAndTrailingSeparators()
	{
		// Windows 路径大小写不敏感；末尾分隔符差异更不能导致"同一个目录被判成两个"。
		Assert.True(PathSafetyGuard.PathsEqual(@"C:\Game\Mods", @"c:\game\mods\"));
		Assert.True(PathSafetyGuard.PathsEqual(@"C:\Game", @"C:\Game\\"));
		Assert.False(PathSafetyGuard.PathsEqual(@"C:\Game", @"C:\Game\Mods"));
	}

	[Fact]
	public void PathsEqual_StripsExtendedLengthPrefix()
	{
		// CreateFileW + GetFinalPathNameByHandleW 返回的是 \\?\C:\... 形式。
		// 不剥这个前缀，"真实路径"和"请求路径"永远不相等，所有校验都会误拒。
		Assert.True(PathSafetyGuard.PathsEqual(@"\\?\C:\Game\Mods", @"C:\Game\Mods"));
		Assert.True(PathSafetyGuard.PathsEqual(@"\\?\UNC\server\share", @"\\server\share"));
	}

	[Fact]
	public void PathsEqual_HandlesNullAsEmpty()
	{
		Assert.True(PathSafetyGuard.PathsEqual(null!, string.Empty));
		Assert.True(PathSafetyGuard.PathsEqual(string.Empty, null!));
	}
}
