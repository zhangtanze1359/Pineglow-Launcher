# 记录 104：UI 入口的**锚点全部取到** —— 它跨 **3 个文件**（不是 2 个）

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-12 08:30–08:44（+08:00）
性质：**只读审计，产品目录零改动**（0 staged）。

---

## 上一轮说"两件套"，那是**功能**数；**文件**数是 3

| 文件 | 要改什么 | 锚点 |
| --- | --- | --- |
| `ui/js/api.js` | 加一个 API 方法 | 插在 `getInstances()`（`:421-424`）之后 |
| `ui/js/launcher.js` | ① 实例卡片加按钮；② `bindInstallations` 里接线 | 卡片模板 `:501-504`；`bindInstallations` `:3129` |
| **`ui/js/i18n.js`** | **加文案键**（这个我上一轮没想到） | UI 全靠 `t('key')`，新按钮/弹窗必须有键 |

**第 3 个文件是这轮才发现的**：这个 UI 的**所有可见文字都走 i18n**（中英各一份）。
不加键就会在界面上显示 `inst.installLoader` 这样的原始键名。

## 确切的改动清单（下一轮照做）

**① `api.js`**，插在 `:424` 之后：

```js
		async installLoader(instanceId, loaderType, loaderVersion) {
			return await _post('/api/instances/' + encodeURIComponent(instanceId) + '/loader',
				{ loaderType, loaderVersion });
		},
```
（`_post` 是现成的 helper —— `api.js:413` 的 `await _post('/api/config', patch);` 就是它。）

**② `launcher.js` 卡片模板**（`:501-504` 的 `.inst-actions` 里加一个按钮）：

```js
				<button class="inst-more" data-loader="${esc(i.id)}" title="${esc(t('inst.installLoader'))}">…</button>
```

**③ `launcher.js` 的 `bindInstallations`**（`:3144` 那种 `el.querySelector('#btnAddExisting')?.addEventListener(...)` 旁边）：

```js
	el.querySelectorAll('[data-loader]').forEach(b => b.addEventListener('click', () => {
		const id = b.dataset.loader;
		openModal(`…选择加载器（Fabric/Quilt/Forge/NeoForge）+ 版本 + 确认…`);
		document.getElementById('mOk').onclick = async () => {
			try {
				await SG.api.installLoader(id, type, version);
				closeModal(); await loadState(true); invalidate('home');
				const v = document.getElementById('view-installations');
				v.innerHTML = await renderInstallations(); bindInstallations(v);
				toast(t('inst.installLoaderDone'));
			} catch (err) { toast(t('inst.installLoaderFailed') + (err.message || t('common.unknownError'))); }
		};
	}));
```

**④ `i18n.js`**：中英各加 3–4 个键（`inst.installLoader` / `inst.installLoaderTitle` / `inst.installLoaderDone` / `inst.installLoaderFailed`）。

## 本轮没做什么（如实）

**没有写补丁。** 三个文件、其中一个是 203 KB 的 JS、还要动 i18n —— 不是本轮预算能走完并验证的。
**UI 入口仍未做**，但**锚点与改动清单已经齐了**，下一轮可以纯转录。

## 一个判断（明确标注）

**这一步的"设计取舍"比后端小得多**：按钮放哪（卡片右侧的 `.inst-actions`，与"更多"并列）、
弹窗长什么样（照抄 `btnAddExisting` 那个 modal 的结构）—— **都有现成的样板可抄**。
所以它更接近"照抄"，而不是"我来定外观"。

**如果你希望我做，我下一轮就能做**；如果你觉得该由你或别的人来做，这份清单也足够直接交接。

## 当前状态

| 项 | 状态 |
| --- | --- |
| 已就绪且十八条合并验证通过的补丁 | 18 条 |
| 缺陷 | 15 条；无补丁的 0 条 |
| 缺陷 1 后端 | **端到端验证通过** |
| 缺陷 1 UI 入口 | **未做**（锚点齐、清单齐、跨 3 文件） |
| 缺陷 13 ③（设置页 Java 安装入口） | 未做 |
| 待你决定 | ① 提交（(a) / **(a′) 已验证安全** / (b)）；② C 的范围；③ 是否做这两处 UI |

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
