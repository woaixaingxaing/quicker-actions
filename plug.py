#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""米家智能插座 3 开关控制（供 Quicker 调用）

用法:
    python plug.py on        打开
    python plug.py off       关闭
    python plug.py toggle    取反
    python plug.py status    查询（输出 True/False）

配置:
    复制 config.example.json 为 config.json，填入自己的设备 DID。
    也可以用环境变量 MIJIA_DID / MIJIA_PROP / MIJIA_RETRIES 覆盖。
"""

import json
import os
import sys
import time
import traceback
from pathlib import Path

BASE_DIR = Path(__file__).resolve().parent
CONFIG_FILE = BASE_DIR / "config.json"
EXAMPLE_FILE = BASE_DIR / "config.example.json"
LOG_FILE = BASE_DIR / "plug.log"


class _NullStream:
    """pythonw.exe 下 stdout/stderr 为 None，mijiaAPI 的 logger 会调用 isatty()。"""

    def write(self, *_args):
        return 0

    def flush(self):
        pass

    def isatty(self):
        return False


if sys.stdout is None:
    sys.stdout = _NullStream()
if sys.stderr is None:
    sys.stderr = _NullStream()


def log(msg: str) -> None:
    action = sys.argv[1] if len(sys.argv) > 1 else "status"
    line = f"{time.strftime('%Y-%m-%d %H:%M:%S')} [{action}] {msg}"
    try:
        with open(LOG_FILE, "a", encoding="utf-8") as f:
            f.write(line + "\n")
    except Exception:
        pass


def load_config():
    """读取配置：config.example.json 提供默认值，config.json 覆盖，环境变量优先级最高。"""
    cfg = {"prop": "on-2", "retries": 3}
    for path in (EXAMPLE_FILE, CONFIG_FILE):
        if not path.exists():
            continue
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
            if isinstance(data, dict):
                cfg.update(data)
        except Exception as exc:
            log(f"WARN 无法读取 {path.name}: {exc}")

    did = str(os.environ.get("MIJIA_DID") or cfg.get("did") or "").strip()
    prop = str(os.environ.get("MIJIA_PROP") or cfg.get("prop") or "on-2").strip()
    try:
        retries = int(os.environ.get("MIJIA_RETRIES") or cfg.get("retries") or 3)
    except (TypeError, ValueError):
        retries = 3
    return did, prop, max(1, retries)


def main() -> int:
    action = (sys.argv[1] if len(sys.argv) > 1 else "status").lower()
    log(f"START argv={sys.argv} ppid={os.getppid()}")

    if action not in ("on", "off", "toggle", "status"):
        log(f"ERROR 未知参数 {action}")
        return 2

    did, prop, retries = load_config()
    if not did or did.startswith("YOUR_"):
        log("ERROR 未配置设备 DID：复制 config.example.json 为 config.json 并填写 did")
        return 2

    try:
        from mijiaAPI import mijiaAPI, mijiaDevice
    except Exception:
        log("ERROR 无法导入 mijiaAPI\n" + traceback.format_exc())
        return 3

    last_err = None
    for attempt in range(1, retries + 1):
        try:
            api = mijiaAPI()
            dev = mijiaDevice(api, did=did)

            if action == "status":
                state = bool(dev.get(prop))
                log(f"status={state} (attempt {attempt})")
                print(state)
                return 0

            if action == "toggle":
                target = not bool(dev.get(prop))
            else:
                target = action == "on"

            dev.set(prop, target)
            time.sleep(1.0)
            actual = bool(dev.get(prop))
            if actual == target:
                log(f"OK set {target} -> verified {actual} (attempt {attempt})")
                return 0
            last_err = f"写入后状态不一致: 期望 {target}, 实际 {actual}"
            log(f"WARN {last_err} (attempt {attempt})")
        except Exception:
            last_err = traceback.format_exc()
            log(f"ERROR attempt {attempt}\n{last_err}")
        time.sleep(1.5)

    log(f"FAILED 重试 {retries} 次仍失败: {last_err}")
    return 1


if __name__ == "__main__":
    sys.exit(main())
