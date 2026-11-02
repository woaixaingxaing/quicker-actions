# 网盘下载

剪贴板里出现百度网盘分享链接时，自动用已登录的 Chrome 打开分享页并点击「下载」，由百度网盘客户端接管任务。

## 流程

```text
剪贴板变化
  → 读取剪贴板文本
  → 正则提取 https?://pan\.baidu\.com/s/[...]
  → 为空？提示并结束
  → 非空 → 设置连接的浏览器为 Chrome
         → 打开分享页（active 标签页，不等待整页加载）
         → 拿到 tabId，等待右下角下载按钮可点击（最长 30 秒）
         → 按 CSS 选择器 button.bottom_download_btn 点击
  → 成功 / 失败提示
```

## 变量

| 变量 | 类型 | 说明 |
| --- | --- | --- |
| `clip` | text | 剪贴板原文 |
| `shareUrl` | text | 正则提取出的分享链接 |
| `okClick` | boolean | 点击结果 |
| `tabId` | integer | 打开的标签页 ID |

## 触发器

- 类型：事件触发 → 剪贴板内容改变 `ClipboardChanged`
- 内容类型：`TEXT`
- 文本匹配：`pan\.baidu\.com/s/`
- 忽略 Quicker 获取选中文本引起的剪贴板变化：是

## 关键实现说明

### 必须用 active 标签页

`chromecontrol` 的 `Wait` / `PageAction` 依赖扩展注入和可访问性树。实测把 `windowInfo` 设成 `{"active": false}` 开后台标签页时，扩展会报：

```text
操作超时，未获得插件响应(chrome|31260|)
No descriptor locator resolved to an actionable element.
```

改成 `"active": true` 后同一页面 6 ms 内就能定位到按钮。窗口本身不会被顶到前台，只是 Chrome 里多一个当前标签。

### 选择器要唯一

分享页有**两个** `title="下载"` 的按钮：

- 顶部工具栏：`button.share-list-toolbar__pill-btn`
- 右下角主按钮：`button.bottom_download_btn`

用 `[title="下载"]` 会匹配到两个，`PageAction` 点击时可能因歧义失败。现在固定用：

```css
button.bottom_download_btn
```

### 提取码页面

带 `?pwd=xxxx` 的链接通常会自动提取，但页面可能先短暂显示“提取文件”输入页（输入框 `#accessCode`）。因此：

- `OpenUrl` 使用 `waitComplete: false`，不阻塞在整页加载；
- `Wait` 超时放宽到 30 秒，给提取流程留时间。

如果链接本身不带提取码，页面会停在输入页，动作无法自动完成，需要手动输入。

## 依赖

- 「Quicker 连接器」浏览器扩展，且 Chrome 中已登录百度网盘账号
- 百度网盘客户端（网页点击下载后由客户端接管）

## 实测

- 2026-11-01：11.15 GB 文件夹，SVIP 加速，客户端接管成功。
- 2026-11-02：修复选择器与打开方式后，分享页 2 秒内提取完成，`button.bottom_download_btn` 定位成功（`found=True ok=True`）。

## 注意

- 客户端已运行时，网页点「下载」直接建任务；客户端未运行时，只会唤起客户端并打开「资源分享」页，需要在客户端里再点一次「下载」。
- `PageAction` 可能输出 `LOW_CONFIDENCE` 警告；只要 `isSuccess=true` 即表示操作已执行。
