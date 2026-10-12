// R2-03 entry-contract-rework (2026-09-07)：
// NewsLinkService 内部形状已明确不在 R2-03 RED 测试范围。
// 所有 R2-03 RED 行为测试已迁移至 NewsLinkHandlerTests.cs（外部 HTTP 行为）
// 和 ui/tests/news-link.test.cjs（外部 WebView 行为）。
//
// 本文件保留为命名空间占位，避免任何"是否需要 NewsLinkService 公开类 / 内部构造 / 特定命名空间"
// 的伪 RED 出现。R2-03 不再对服务类存在性、可见性、命名空间、方法名做断言。
namespace Pineglow.Launcher.Tests;
