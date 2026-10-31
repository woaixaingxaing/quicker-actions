# 网页打开并点击（公共子程序）

通用浏览器操作子程序，被「网盘下载」「夸克下载」共用。以后任何 Quicker 动作都可以直接调用，不必重复搭建 `chromecontrol` 步骤链。

## 参数

| 参数 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `url` | text | 空 | 要打开的网址；留空则不打开，直接操作当前活动标签页 |
| `locator` | text | `role` | 定位方式：`role` / `text` / `css` |
| `value` | text | 空 | 定位值：角色的可访问名称、可见文本或选择器 |
| `role` | text | `button` | 仅 `locator=role` 时使用，例如 `button`、`link`、`textbox` |
| `pageUrlPattern` | text | 空 | 页面地址约束，如 `https://example.com/orders/*`；留空不限 |
| `waitMs` | integer | 2500 | 打开网址后等待的毫秒数 |
| `timeoutMs` | integer | 10000 | 定位元素的超时毫秒数 |
| `ok` | boolean | — | 输出：是否点击成功 |

## 行为

1. `url` 非空 → `chromecontrol OpenUrl`（当前浏览器窗口，等待加载完成，失败不中断）。
2. 等待 `waitMs`。
3. 按 `locator` 分支：
   - `css` → `PageAction` + CSS 选择器
   - `text` → `PageAction` + 文本精确匹配
   - 其他 → `PageAction` + 角色定位（默认 `button`）
4. 结果写入 `ok`；点击失败只返回 `false`，不抛错。

## 调用示例

```yaml
- subProgram: "%%<子程序 GUID>"
  inputs:
    url:
      var: shareUrl
    locator: role
    value: 下载
    role: button
    pageUrlPattern: https://pan.baidu.com/s/*
    waitMs: 4000
    timeoutMs: 10000
  outputs:
    ok: okClick
```

## 依赖

- Quicker 的 `chromecontrol` 模块（`sys:chromecontrol`）
- 浏览器侧安装并启用「Quicker 连接器」扩展
