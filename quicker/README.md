# Quicker 动作合集

本目录汇集本仓库的全部 Quicker 动作。每个动作一个子目录，结构统一：`info.yaml`（标题 / 描述 / 图标）、`program.source.yaml`（组合动作源码）或对应脚本与配置，以及 `README.md`（安装、参数和触发器说明）。

## 动作一览

| 动作 | 类型 | 触发 | 说明 |
| --- | --- | --- | --- |
| [网盘下载](netdisk-download/README.md) | 组合动作 | 剪贴板含 `pan.baidu.com/s/` | 用已登录的 Chrome 打开百度网盘分享页并自动点击下载 |
| [夸克下载](quark-download/README.md) | 组合动作 | 剪贴板含 `(pan\|drive).quark.cn/s/` | 同上，针对夸克网盘 |
| [网页打开并点击](browser-open-click/README.md) | 公共子程序 | 被其他动作调用 | 通用浏览器操作：打开网址 + 按 role / text / css 定位并点击 |
| [传输空闲自动清后台](transfer-idle-watchdog/README.md) | C# 脚本动作 | 进程启动 / 定时自检 | 监控指定软件的上传下载，连续空闲后结束后台进程 |
| 米家插座 开启 / 关闭 | 运行或打开 | 锁屏 / 解锁 / 双击右 Ctrl | 由 `米家插座-*.operation.json` 提供，接线说明见 [触发器配置](触发器配置.md) |

## 触发器总览

| 动作 | 事件 | 关键参数 |
| --- | --- | --- |
| 网盘下载 | 剪贴板内容改变 `ClipboardChanged` | 内容类型 `TEXT`；文本匹配 `pan\.baidu\.com/s/` |
| 夸克下载 | 剪贴板内容改变 `ClipboardChanged` | 内容类型 `TEXT`；文本匹配 `(pan\|drive)\.quark\.cn/s/` |
| 传输空闲自动清后台 | 进程启动 `ProcessStarted` | `BaiduNetdisk;quark_cloud_drive` |
| 米家插座 关闭 | 计算机锁定 `SessionLock` | — |
| 米家插座 开启 | 计算机解锁 `SessionUnlock` | — |
| 米家插座 开启 | 按键双击 | 右 Ctrl（VK 163） |

> Quicker 免费版限制 2 条自定义事件触发。若同时启用「网盘下载」和「夸克下载」的剪贴板规则，配额刚好用满；再接新的事件规则需要先合并或删除。

## 安装通用步骤

1. 打开 Quicker → 动作编辑器 → 新建组合动作（或新建公共子程序）。
2. 把 `program.source.yaml` 的内容写入 YAML 源（或按对应 README 逐步骤搭建）。
3. 保存，并把动作移出「暂存区」，放进目标场景（如 `#_后台`）。
4. 按「触发器总览」添加对应事件。
5. 浏览器类动作需要安装并启用「Quicker 连接器」扩展（Chrome / Edge），并在浏览器中已登录对应网盘账号。

## 注意事项

- 动作还在「暂存区」时，事件触发会报「未能找到动作」。必须先保存为正式动作。
- 浏览器操作依赖你浏览器里的登录状态；分享页点击下载需要目标网盘客户端已安装。
- 浏览器步骤使用 active 标签页：后台标签页会导致 Quicker 扩展超时或定位不到元素。
- 百度网盘客户端未运行时，网页点击「下载」只会唤起客户端并打开「资源分享」页，需要在客户端里再点一次「下载」才会真正开始；客户端已运行时点网页下载则直接建任务。
- 实测记录：百度网盘链路 2026-11-02 复测通过（分享页提取 → `button.bottom_download_btn` 定位 → 点击）；夸克链路已配置但未用真实分享链接实测页面结构。

## 相关项目

- [无水印视频下载](https://github.com/woaixaingxaing/quicker-no-watermark-video-downloader)：复制分享链接后自动下载无水印视频；抖音直连，其他平台浏览器安全验证兜底（Python + C# 脚本动作）。
