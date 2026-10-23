#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""列出小米账号下的设备，帮助找到插座 DID。

不会输出 token 等凭据字段。
"""

import sys

try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass


def main() -> int:
    try:
        from mijiaAPI import mijiaAPI
    except Exception:
        print("无法导入 mijiaAPI，请先运行: python -m pip install mijiaAPI")
        return 3

    try:
        api = mijiaAPI()
        devices = api.get_devices_list()
    except Exception as exc:
        print(f"读取设备列表失败: {exc}")
        return 1

    rows = []
    for dev in devices:
        rows.append(
            (
                str(dev.get("name") or ""),
                str(dev.get("did") or ""),
                str(dev.get("model") or ""),
                "在线" if dev.get("isOnline") else "离线",
            )
        )

    rows.sort(key=lambda r: (r[2], r[0]))
    print(f"{'名称':<28} {'DID':<16} {'型号':<32} 状态")
    print("-" * 90)
    for name, did, model, online in rows:
        print(f"{name:<28} {did:<16} {model:<32} {online}")

    print("\n把插座的 DID 填进 config.json 的 did 字段。")
    print("米家智能插座 3 的型号通常是 cuco.plug.v3。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
