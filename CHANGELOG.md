# 更新记录

## 2026-11-04 — 定位改走元素链 + 浏览器静默

- `quicker/netdisk-download/` 不再内联浏览器步骤，改为调用公共子程序「网页打开并点击」；动作只剩「读剪贴板 → 调子程序 → 日志/提示」。
- 删除写死的 `button.bottom_download_btn`：分享页改版或按钮语义变化就会失效。
- 公共子程序 `quicker/browser-open-click/` 新增两个参数：`active`（默认 false，后台标签页，不抢焦点）、`autoFallback`（默认 true）；新增输出 `matched` / `tabId`。
- 回退链：主定位（role/text/css）失败后依次尝试 文本精确 → 按钮角色 → 链接角色 → 模糊文本。
- 修正 2026-11-02 的结论：后台标签页 `{"active": false}` 可以正常点击（实测 3.1 秒）；但后台标签页下文本精确定位常报 `No descriptor locator resolved to an actionable element`，角色定位更稳，因此需要回退链。
- 触发器文本匹配由 `pan\.baidu\.com/s/` 放宽为 `.*pan\.baidu\.com/s/.*`，匹配「链接: … 提取码: …」这类整段剪贴板文本。
- 更新两个动作与公开子程序的 README。

## 2026-11-02 — 网盘下载：定位与打开方式修复

- `quicker/netdisk-download/` 改为内联浏览器步骤，不再依赖公共子程序。
- 修复后台标签页导致 Quicker 扩展无响应：`windowInfo` 改为 `{"active": true}`。
- 修复 `[title="下载"]` 同时匹配顶部工具栏和右下角两个按钮导致点击失败：改用唯一选择器 `button.bottom_download_btn`。
- `OpenUrl` 改为 `waitComplete: false`，`Wait` 超时放宽到 30 秒，给分享页提取码流程留时间。
- 更新动作 README 的流程、变量和关键实现说明。

## 2026-11-01 — Quicker 动作合集整理

- 新增 quicker/netdisk-download/（网盘下载）：剪贴板出现百度网盘分享链接时，用已登录的 Chrome 打开分享页并自动点击「下载」，由客户端接管。2026-11-01 全流程实测通过。
- 新增 quicker/quark-download/（夸克下载）：同一结构处理 pan.quark.cn/s/ 与 drive.quark.cn/s/ 链接；触发已配好，夸克页面结构待实测。
- 新增 quicker/browser-open-click/（公共子程序）：通用「打开网址 + 按 role/text/css 定位点击」，上面两个动作共用。
- 新增 quicker/README.md 作为全部 Quicker 动作的统一索引。
- 更新 quicker/触发器配置.md：合并网盘、夸克与米家插座的全部触发器说明，并注明免费版 2 条事件触发配额。
- 根 README 补充动作合集入口与目录说明。

## 2026-10-26 — transfer-idle-watchdog v1.0.4 多进程空闲判定修复

- 空闲判定改为按 PID 单独比对 I/O 增量：新出现的辅助进程只记基线，子进程起落既不算活动也不重置倒计时，避免夸克网盘这类 Electron 客户端整夜不被清理。
- 修复新进程的历史累计 I/O 被误算为一次大传输的问题。
- 动作触发器补充「夸克网盘启动时确保后台监控运行」，与百度网盘一致。
- 新增多进程抖动回归测试记录（每 5 秒派生同名子进程，6 秒空闲阈值下 13 秒内正确清理）。

## 2026-10-25 — transfer-idle-watchdog v1.0.3 桌面提示

- `quicker/transfer-idle-watchdog/`：空闲清理真正结束（或有残留）进程后，屏幕右下角弹出桌面提示卡片；卡片为 WPF 渲染（抗锯齿圆角 + 阴影），8 秒自动消失，点卡片或 Esc 可提前关闭。
- 提示由独立小进程（新增 `notify.ps1`）显示，不再阻塞监控采样循环；原 `Show-Balloon` 气泡方案废弃。
- 手动触发时先打开设置窗口、关闭后再确保监控，避免监控启动等待拖慢弹窗。
- `build_action.py` 现在同时嵌入 `watchdog.ps1`、`notify.ps1`、`settings.tsx`；动作按 `watchdog.version` 标记自动更新数据目录脚本并重启监控。

## 2026-10-25 — 新增独立动作

- 新增 `quicker/transfer-idle-watchdog/`：通用「传输空闲自动清后台」Quicker 动作。监控任意软件的上传/下载，连续空闲达到设定时间后结束后台进程；默认百度网盘 5 分钟。
- 支持多软件列表、搜索、运行中软件快捷添加、行内编辑、配置热更新，以及独立后台监控和状态/日志菜单。
- 附带 `main.cs`、`settings.tsx`、`watchdog.ps1`、`build_action.py`、动作元数据与去敏配置示例。
- 根 README 补充新动作入口与目录说明。

## v1.0 — 2026-10-24

首个版本。

- `Win + L` 锁屏后自动关闭米家插座，解锁后自动恢复供电。
- 在已解锁桌面双击右 `Ctrl`，随时手动开电。
- `plug.py` 支持 `on` / `off` / `toggle` / `status`，云端写入后回读校验，失败最多重试 3 次。
- 附带 Quicker 开启/关闭动作 JSON 和三条触发器配置说明。
- 附带去敏后的 `config.example.json`，不包含账号凭据与设备 DID。
- 提供工作链路示意图、测试记录与排错文档。
