# 抖音 AI 总结静默落盘（总结落盘）

抖音网页版生成「AI 总结」后，自动把总结追加进一份 Markdown 归档，全程不需要点按钮。

## 组成

| 文件 | 说明 |
|---|---|
| `program.source.yaml` | 组合动作主程序：s1 读取 → s2 落盘 → s3 日志 |
| `files/chromecontrol1.js` | s1 的 `RunScript` 脚本（浏览器扩展新版写法） |
| `files/assign1.txt` | s2 的 `$=` C#，落盘核心（235 行） |
| `info.yaml` | 动作标题与图标 |

网页侧脚本不在本仓库：它在油猴脚本 `douyin-ad-skip.user.js` 里负责写 `localStorage` 并 ping Quicker，关键片段见下一节。

## 触发链路

1. 油猴脚本把总结写进页面 `localStorage` 的 `dy-ad-skip:summaries:v1`（数组，上限 300 条）。
2. 2.5 秒后把当前 URL 的 hash 改成 `#dy-summary-<Date.now()>`，1.5 秒后还原成原地址。
3. Quicker 的「浏览器网址变更」事件命中正则，延迟 200ms 触发本动作。
4. 动作先读 localStorage，再按 `awemeId` 锚点幂等合并进 Markdown。

```js
// 每次 ping 都带时间戳，保证 hash 一定变化，重复总结也能再次触发
hist.replaceState(hist.state, '', base + '#dy-summary-' + Date.now());
```

## 触发器

| 项 | 值 |
|---|---|
| 事件类型 | 浏览器网址变更 `BrowserUrlChanged` |
| 匹配网址 | `regex:^https://(?:www\|m)\.douyin\.com/.*#dy-summary` |
| 忽略非活动标签页 | 是 |
| 节流阀 | 1000 ms |
| 延迟执行 | 200 ms |
| 动作参数 | 空 |

## 安装

1. 新建组合动作，把 `program.source.yaml` 写入 YAML 源，保存并移出「暂存区」。
2. s1 用浏览器扩展的「对标签页运行脚本」，脚本内容取 `files/chromecontrol1.js`。
3. s2 是赋值步骤，指向上面的 `files/assign1.txt`（内容以 `$=` 开头的 C# 表达式）。
4. 把 `assign1.txt` 里的 `const string path` 改成你自己的 Markdown 路径。
5. 按上表加事件触发；浏览器需装好「Quicker 连接器」，并允许运行用户脚本。

## 关键实现点

- `RunScript` 取的是脚本最后表达式的值 / 返回的 Promise，**顶层 `return` 无效**，要写成 `async function f(){…} f();`。
- 幂等：以 `dy-<awemeId>` 为锚点，有则整段替换、无则追加；生成文本与现有文件完全一致时直接不写盘（避免 OneDrive 反复同步）。
- 并发：`Local\QuickerDouyinSummaryDump` 互斥量，多个抖音标签页同时 ping 时串行化读-改-写。
- 写盘失败（OneDrive 同步 / 编辑器占用）重试 3 次，间隔 250ms。
- 目录按文件里实际存在的锚点重建，不在本批数据里的老条目不会从目录里消失；单条出错只记进 `errors`，不影响其余条目。
- 读取为空时日志写「扩展没连上，或活动标签页不是抖音」，不再和「不是 JSON」混为一谈。

## 注意事项

- 落盘目标是单个 Markdown 文件，路径写死在 C# 的 `const string path`。
- Quicker 检查器会对「文件形式的多行 `$=`」报一条「字符串内有原始换行」的告警；该 C# 实际编译运行正常，属误报。
- 本动作占用一条自定义事件触发配额（免费版共 2 条）。
- 实测记录：2026-11-06 运行验证通过（读取 → 解析 → 幂等不写盘），日志「抖音总结落盘：0（内容无变化，未写入）」。
