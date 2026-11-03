# 网页打开并点击（公共子程序）

通用浏览器操作子程序，被「网盘下载」「夸克下载」共用。任何 Quicker 动作都可以直接调用，不必重复搭建 `chromecontrol` 步骤链。

## 参数

| 参数 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `url` | text | 空 | 要打开的网址；留空则不打开，直接操作当前活动标签页 |
| `locator` | text | `role` | 主定位方式：`role` / `text` / `css` |
| `value` | text | 空 | 定位值：可访问名称、可见文本或 CSS 选择器 |
| `role` | text | `button` | 仅 `locator=role` 时使用，例如 `button`、`link`、`textbox` |
| `pageUrlPattern` | text | 空 | 页面地址约束，如 `https://example.com/orders/*`；留空不限 |
| `waitMs` | integer | 2500 | 打开网址后等待的毫秒数 |
| `timeoutMs` | integer | 10000 | 主定位的超时毫秒数 |
| `active` | boolean | false | 是否把新标签页切到前台；false = 后台标签页，不抢焦点 |
| `autoFallback` | boolean | true | 主定位失败后是否自动走回退定位链 |

## 输出

| 输出 | 类型 | 说明 |
| --- | --- | --- |
| `ok` | boolean | 是否点击成功 |
| `matched` | text | 命中方式：`role`/`text`/`css`（主定位）、`text:exact`、`role:button`、`role:link`、`text:fuzzy`、`notfound` |
| `tabId` | integer | 实际操作的标签页 ID（`url` 为空时为 0） |

## 行为

1. `url` 非空 → `chromecontrol OpenUrl`，`windowInfo` 按 `active` 取 `{"active":true}` 或 `{"active":false}`，`waitComplete: true`，失败不中断。
2. 等待 `waitMs`。
3. 主定位：
   - `css` → 先 `Wait`（`elementClickable`），再 `PageAction` + CSS
   - `text` → `PageAction` + 文本精确
   - 其他 → `PageAction` + 角色（默认 `button`）
4. 主定位失败且 `autoFallback=true` → 依次回退：文本精确 → 按钮角色 → 链接角色 → 模糊文本（每次 4 秒）。
5. 结果写入 `ok` / `matched` / `tabId`，并写一行动作日志。失败只返回 `false`，不抛错。

## 调用示例

```yaml
- subProgram: "%%f89423ad-7e45-4105-b5be-935167a18570"
  inputs:
    url:
      var: shareUrl
    locator: text
    value: 下载
    active: false
    autoFallback: true
    waitMs: 1500
    timeoutMs: 8000
  outputs:
    ok: okClick
    matched: matchedBy
    tabId: tabId
```

## 实测与坑

- **后台标签页可用**（2026-11-04 复测）：`active: false` 时 3.1 秒完成「打开 → 定位 → 点击」，Chrome 不抢焦点、不切标签页。2026-11-02 记的「必须用 active 标签页」结论已修正。
- **文本精确定位在后台标签页偏弱**：同一页面、后台标签页时，`locator=text` 常返回
  `No descriptor locator resolved to an actionable element`，而角色定位（`role=button` + 名称「下载」）能命中。`autoFallback` 就是为这种情况准备的。
- `PageAction` 可能输出 `LOW_CONFIDENCE` 警告；只要 `isSuccess=true` 即表示点击已执行。
- 若页面把按钮做成不可交互的容器（无 button/a 语义），三种定位都会失败，此时用 `locator=css` 给唯一选择器最稳。

## 依赖

- Quicker 的 `chromecontrol` 模块（`sys:chromecontrol`）
- 浏览器侧安装并启用「Quicker 连接器」扩展
