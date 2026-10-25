# -*- coding: utf-8 -*-
"""Generate main.cs with the watchdog script and React settings embedded as base64."""
import base64
import pathlib

HERE = pathlib.Path(__file__).resolve().parent
WATCHDOG = HERE / "watchdog.ps1"
SETTINGS = HERE / "settings.tsx"
OUTPUT = HERE / "main.cs"


def chunks(value: str, width: int = 4000) -> str:
    parts = [value[i:i + width] for i in range(0, len(value), width)]
    if len(parts) == 1:
        return '        "' + parts[0] + '"'
    lines = []
    for idx, part in enumerate(parts):
        suffix = '" +' if idx < len(parts) - 1 else '"'
        prefix = '        "' if idx else '        "'
        lines.append(prefix + part + suffix)
    # first line also needs indentation and no leading extra spaces inside string
    return "\n".join(lines)


watchdog_bytes = WATCHDOG.read_bytes()
if not watchdog_bytes.startswith(b"\xef\xbb\xbf"):
    watchdog_bytes = b"\xef\xbb\xbf" + watchdog_bytes
watchdog_b64 = base64.b64encode(watchdog_bytes).decode("ascii")
settings_b64 = base64.b64encode(SETTINGS.read_bytes()).decode("ascii")

TEMPLATE = r'''void Main()
{
    var dir = PrepareData();
    var trigger = qk.Context.Trigger ?? "";
    if (trigger == "autoRun" || trigger == "event")
    {
        EnsureMonitor(dir, false);
        return;
    }
    EnsureMonitor(dir, false);
    OpenSettings(dir);
    EnsureMonitor(dir, true);
}

[ActionMenu("软件与规则…", Description = "添加或修改需要监控的软件", Icon = "fa:Light_Cog")]
void OpenSettingsMenu()
{
    var dir = PrepareData();
    EnsureMonitor(dir, false);
    OpenSettings(dir);
    EnsureMonitor(dir, true);
}

[ActionMenu("运行状态…", Description = "查看每个软件的空闲计时、传输量和清理记录")]
void ShowStatusMenu()
{
    ShowStatus(PrepareData());
}

[ActionMenu("重启监控", Description = "重新启动后台监控进程")]
void RestartMonitorMenu()
{
    var dir = PrepareData();
    StopMonitor(dir, false);
    var ok = StartMonitor(dir);
    qk.Ui.Notify(ok ? "后台监控已重启。" : "后台监控重启失败，请查看日志。",
        ok ? "success" : "warn", "传输空闲清理", 5000);
}

[ActionMenu("停止监控", Description = "停止后台监控，不再自动结束后台")]
void StopMonitorMenu()
{
    StopMonitor(PrepareData(), true);
}

[ActionMenu("查看日志…", Description = "查看后台监控最近的运行日志")]
void ShowLogMenu()
{
    ShowLog(PrepareData());
}

[ActionMenu("打开数据文件夹", Description = "打开配置、状态和日志所在的文件夹")]
void OpenDataFolderMenu()
{
    qk.Process.Open(PrepareData());
}

string PrepareData()
{
    var dir = qk.Files.GetActionDataDirectory();
    qk.Files.CreateDirectory(dir);
    EnsureConfig(dir);
    var updated = EnsureWatchdog(dir);
    if (updated && IsMonitorAlive(dir)) StopMonitor(dir, false);
    return dir;
}

void EnsureConfig(string dir)
{
    var path = Path.Combine(dir, "apps.json");
    if (qk.Files.Exists(path)) return;
    var node = JsonNode.Parse(DefaultConfigJson());
    if (node != null) WriteJson(path, node);
}

bool EnsureWatchdog(string dir)
{
    var path = Path.Combine(dir, "watchdog.ps1");
    var marker = Path.Combine(dir, "watchdog.version");
    var version = WatchdogVersion();
    var need = true;
    try
    {
        if (qk.Files.Exists(path) && qk.Files.Exists(marker))
            need = qk.Files.ReadText(marker).Trim() != version;
    }
    catch
    {
        need = true;
    }
    if (!need) return false;
    qk.Files.WriteBytes(path, Convert.FromBase64String(WatchdogBase64()), overwrite: true);
    qk.Files.WriteText(marker, version, overwrite: true);
    qk.Log("已更新后台监控脚本 " + version);
    return true;
}

string WatchdogVersion()
{
    return "1.0.1";
}

void EnsureMonitor(string dir, bool notify)
{
    if (IsMonitorFresh(dir)) return;
    var ok = EnsureMonitorCore(dir);
    if (!notify) return;
    if (ok) qk.Ui.Notify("后台监控已启动。", "success", "传输空闲清理", 4000);
    else qk.Ui.Notify("后台监控启动失败，请用右键菜单“查看日志…”排查。", "warn", "传输空闲清理", 7000);
}

bool EnsureMonitorCore(string dir)
{
    if (IsMonitorFresh(dir)) return true;
    return StartMonitor(dir);
}

bool StartMonitor(string dir)
{
    try
    {
        var stopPath = Path.Combine(dir, "stop.flag");
        if (qk.Files.Exists(stopPath)) qk.Files.Delete(stopPath);
        var ps1 = Path.Combine(dir, "watchdog.ps1");
        if (!qk.Files.Exists(ps1)) EnsureWatchdog(dir);
        var token = Guid.NewGuid().ToString("N");
        var inner = new string[]
        {
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
            "-File", ps1, "-DataDir", dir, "-Token", token, "-Version", WatchdogVersion()
        };
        var launcher = BuildHiddenLauncher(inner);
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(launcher));
        var run = qk.Process.Run("powershell.exe", new string[]
        {
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded
        }, encoding: "gbk", timeoutMs: 30000);
        if (run.ExitCode != 0)
        {
            qk.Log("启动后台监控失败 exit=" + run.ExitCode + " stderr=" + run.StandardError, "error");
            return false;
        }
        var until = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < until)
        {
            qk.Wait(500);
            if (IsMonitorFresh(dir)) return true;
        }
        qk.Log("后台监控启动后没有写出有效状态。stdout=" + run.StandardOutput + " stderr=" + run.StandardError, "warn");
        return false;
    }
    catch (Exception e)
    {
        qk.Log("启动后台监控异常：" + e.Message, "error");
        return false;
    }
}

string BuildHiddenLauncher(string[] args)
{
    var parts = new List<string>();
    foreach (var a in args) parts.Add(PsQuote(a));
    var sb = new StringBuilder();
    sb.AppendLine("$ErrorActionPreference = 'Stop'");
    sb.AppendLine("$argList = @(" + string.Join(", ", parts) + ")");
    sb.AppendLine("$p = Start-Process -FilePath 'powershell.exe' -ArgumentList $argList -WindowStyle Hidden -PassThru");
    sb.AppendLine("if ($null -eq $p) { exit 2 }");
    sb.AppendLine("[string]$p.Id");
    return sb.ToString();
}

string PsQuote(string value)
{
    var needQuotes = value.IndexOf(' ') >= 0 || value.IndexOf('\t') >= 0;
    var inner = value.Replace("'", "''");
    if (needQuotes) inner = "\"" + inner + "\"";
    return "'" + inner + "'";
}

JsonObject ReadStatus(string dir)
{
    var path = Path.Combine(dir, "status.json");
    try
    {
        if (!qk.Files.Exists(path)) return null;
        var text = qk.Files.ReadText(path);
        if (string.IsNullOrWhiteSpace(text)) return null;
        return JsonNode.Parse(text) as JsonObject;
    }
    catch
    {
        return null;
    }
}

bool IsMonitorAlive(string dir)
{
    var st = ReadStatus(dir);
    if (st == null) return false;
    var updatedText = JsonText(st, "updatedAt");
    DateTimeOffset updated;
    if (updatedText.Length == 0) return false;
    if (!DateTimeOffset.TryParse(updatedText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out updated)) return false;
    var sample = JsonInt(st, "sampleSeconds", 10);
    var limit = Math.Max(30, sample * 3);
    if ((DateTimeOffset.UtcNow - updated.ToUniversalTime()).TotalSeconds > limit) return false;
    var pid = JsonInt(st, "pid", 0);
    if (pid <= 0) return false;
    foreach (var p in qk.Process.List())
    {
        if (p.Pid == pid) return true;
    }
    return false;
}

bool IsMonitorFresh(string dir)
{
    if (!IsMonitorAlive(dir)) return false;
    var st = ReadStatus(dir);
    if (st == null) return false;
    return JsonText(st, "version") == WatchdogVersion();
}

void StopMonitor(string dir, bool notify)
{
    var stopPath = Path.Combine(dir, "stop.flag");
    var wasAlive = IsMonitorAlive(dir);
    try
    {
        qk.Files.WriteText(stopPath, "stop", overwrite: true);
    }
    catch
    {
    }
    var until = DateTimeOffset.UtcNow.AddSeconds(5);
    while (DateTimeOffset.UtcNow < until)
    {
        if (!IsMonitorAlive(dir)) break;
        qk.Wait(250);
    }
    var pids = FindMonitorPids(dir);
    var killed = 0;
    foreach (var pid in pids)
    {
        try
        {
            killed += qk.Process.Kill(pid);
        }
        catch (ActionApiException e)
        {
            qk.Log("结束旧监控进程失败 " + pid + "：" + e.Code.Value, "warn");
        }
    }
    try
    {
        if (qk.Files.Exists(stopPath)) qk.Files.Delete(stopPath);
    }
    catch
    {
    }
    try
    {
        var statusPath = Path.Combine(dir, "status.json");
        if (qk.Files.Exists(statusPath) && !IsMonitorAlive(dir)) qk.Files.Delete(statusPath);
    }
    catch
    {
    }
    qk.Log("已停止后台监控。killed=" + killed);
    if (notify)
    {
        if (wasAlive || pids.Count > 0) qk.Ui.Notify("后台监控已停止。", "success", "传输空闲清理", 4000);
        else qk.Ui.Notify("后台监控本来就没有运行。", "info", "传输空闲清理", 4000);
    }
}

List<int> FindMonitorPids(string dir)
{
    var result = new List<int>();
    try
    {
        var ps1 = Path.Combine(dir, "watchdog.ps1");
        var query = "$path = " + PsQuote(ps1) + "\n" +
            "try { Get-CimInstance Win32_Process -Filter \"Name='powershell.exe'\" -ErrorAction Stop | Where-Object { $_.CommandLine -and $_.CommandLine.IndexOf($path, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 } | ForEach-Object { [string]$_.ProcessId } } catch { }\n";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(query));
        var run = qk.Process.Run("powershell.exe", new string[]
        {
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded
        }, encoding: "gbk", timeoutMs: 15000);
        foreach (var line in run.StandardOutput.Replace("\r", "").Split('\n'))
        {
            int pid;
            if (int.TryParse(line.Trim(), out pid) && pid > 0 && !result.Contains(pid)) result.Add(pid);
        }
    }
    catch (Exception e)
    {
        qk.Log("查找监控进程失败：" + e.Message, "warn");
    }
    return result;
}

JsonObject LoadConfig(string dir)
{
    var path = Path.Combine(dir, "apps.json");
    try
    {
        if (qk.Files.Exists(path))
        {
            var text = qk.Files.ReadText(path);
            if (!string.IsNullOrWhiteSpace(text))
            {
                var node = JsonNode.Parse(text) as JsonObject;
                if (node != null && node["apps"] is JsonArray) return node;
            }
        }
    }
    catch (Exception e)
    {
        qk.Log("读取配置失败：" + e.Message, "warn");
    }
    var fallback = JsonNode.Parse(DefaultConfigJson()) as JsonObject;
    if (fallback != null) return fallback;
    return new JsonObject();
}

string DefaultConfigJson()
{
    return @"{
  ""version"": 1,
  ""sampleSeconds"": 10,
  ""notifications"": true,
  ""apps"": [
    {
      ""name"": ""百度网盘"",
      ""enabled"": true,
      ""processes"": [""BaiduNetdisk"", ""BaiduNetdiskUnite"", ""baidunetdiskhost"", ""BaiduNetdiskRender"", ""ServiceAssistans""],
      ""transferProcesses"": [""baidunetdiskhost""],
      ""transferCommandLine"": ""kernel.dll"",
      ""idleMinutes"": 5,
      ""thresholdKB"": 32
    }
  ]
}";
}

void WriteJson(string path, JsonNode node)
{
    var options = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    qk.Files.WriteText(path, node.ToJsonString(options), overwrite: true);
}

string JsonText(JsonObject o, string key)
{
    try
    {
        var v = o[key];
        if (v != null) return v.GetValue<string>() ?? "";
    }
    catch
    {
    }
    return "";
}

int JsonInt(JsonObject o, string key, int def)
{
    try
    {
        var v = o[key];
        if (v != null) return v.GetValue<int>();
    }
    catch
    {
    }
    return def;
}

long JsonLong(JsonObject o, string key, long def)
{
    try
    {
        var v = o[key];
        if (v != null) return v.GetValue<long>();
    }
    catch
    {
    }
    return def;
}

double JsonDouble(JsonObject o, string key, double def)
{
    try
    {
        var v = o[key];
        if (v != null) return v.GetValue<double>();
    }
    catch
    {
    }
    return def;
}

bool JsonBool(JsonObject o, string key, bool def)
{
    try
    {
        var v = o[key];
        if (v != null) return v.GetValue<bool>();
    }
    catch
    {
    }
    return def;
}

void OpenSettings(string dir)
{
    var cfg = LoadConfig(dir);
    var input = new Dictionary<string, object?>();
    input["apps"] = UiApps(cfg);
    input["running"] = RunningWindows();
    input["monitor"] = MonitorSummary(dir);
    object? result = null;
    try
    {
        result = qk.Ui.React(SettingsSource(), input, width: 1080, height: 780,
            options: new UiOptions(Title: "传输空闲清理 · 软件与规则"));
    }
    catch (ActionApiException e)
    {
        qk.Log("设置窗口打开失败：" + e.Code.Value + " " + (e.Detail ?? ""), "error");
        qk.Ui.Notify("设置窗口打开失败：" + e.Code.Value + " " + (e.Detail ?? ""), "error", "传输空闲清理", 7000);
        return;
    }
    if (result == null) return;
    JsonNode node;
    try
    {
        node = JsonSerializer.SerializeToNode(result);
    }
    catch (Exception e)
    {
        qk.Log("读取设置结果失败：" + e.Message, "error");
        return;
    }
    var apps = node?["apps"] as JsonArray;
    if (apps == null) return;
    var error = SaveApps(dir, cfg, apps);
    if (error.Length > 0)
    {
        qk.Ui.Notify("设置未保存：" + error, "error", "传输空闲清理", 7000);
        return;
    }
    qk.Ui.Notify("规则已保存，后台监控会在几秒内按新规则运行。", "success", "传输空闲清理", 4000);
}

List<Dictionary<string, object?>> UiApps(JsonObject cfg)
{
    var list = new List<Dictionary<string, object?>>();
    if (!(cfg["apps"] is JsonArray apps)) return list;
    foreach (var item in apps)
    {
        var a = item as JsonObject;
        if (a == null) continue;
        var procs = new List<string>();
        foreach (var s in JsonTextArray(a, "processes")) procs.Add(s);
        var transfer = new List<string>();
        foreach (var s in JsonTextArray(a, "transferProcesses")) transfer.Add(s);
        list.Add(new Dictionary<string, object?>
        {
            ["name"] = JsonText(a, "name"),
            ["enabled"] = JsonBool(a, "enabled", true),
            ["processes"] = procs,
            ["transferProcesses"] = transfer,
            ["transferCommandLine"] = JsonText(a, "transferCommandLine"),
            ["idleMinutes"] = JsonDouble(a, "idleMinutes", 5.0),
            ["thresholdKB"] = JsonInt(a, "thresholdKB", 32)
        });
    }
    return list;
}

List<string> JsonTextArray(JsonObject o, string key)
{
    var list = new List<string>();
    if (!(o[key] is JsonArray arr)) return list;
    foreach (var item in arr)
    {
        try
        {
            var s = item?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
        }
        catch
        {
        }
    }
    return list;
}

List<Dictionary<string, object?>> RunningWindows()
{
    var list = new List<Dictionary<string, object?>>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var reserved = ReservedNames();
    try
    {
        foreach (var w in qk.Window.FindAllInfo(limit: 64))
        {
            var name = NormalizeName(w.Process ?? "");
            if (name.Length == 0 || seen.Contains(name)) continue;
            if (reserved.Contains(name)) continue;
            if (name.StartsWith("quicker", StringComparison.OrdinalIgnoreCase)) continue;
            seen.Add(name);
            list.Add(new Dictionary<string, object?>
            {
                ["name"] = name,
                ["title"] = w.Title ?? "",
                ["path"] = w.Path ?? ""
            });
        }
    }
    catch (Exception e)
    {
        qk.Log("枚举运行中窗口失败：" + e.Message, "warn");
    }
    list.Sort((a, b) => string.Compare((string?)a["name"], (string?)b["name"], StringComparison.OrdinalIgnoreCase));
    return list;
}

Dictionary<string, object?> MonitorSummary(string dir)
{
    var st = ReadStatus(dir);
    var alive = IsMonitorAlive(dir);
    var fresh = alive && IsMonitorFresh(dir);
    var pid = 0;
    if (st != null) pid = JsonInt(st, "pid", 0);
    return new Dictionary<string, object?>
    {
        ["running"] = fresh,
        ["alive"] = alive,
        ["pid"] = pid,
        ["updatedAt"] = st != null ? JsonText(st, "updatedAt") : "",
        ["lastEvent"] = st != null ? JsonText(st, "lastEvent") : ""
    };
}

string SaveApps(string dir, JsonObject cfg, JsonArray apps)
{
    JsonArray cleaned;
    var error = CleanApps(apps, out cleaned);
    if (error.Length > 0) return error;
    cfg["apps"] = cleaned;
    cfg["version"] = JsonValue.Create(1);
    WriteJson(Path.Combine(dir, "apps.json"), cfg);
    qk.Log("已保存 " + cleaned.Count + " 个软件的监控规则。");
    return "";
}

string CleanApps(JsonArray apps, out JsonArray cleaned)
{
    cleaned = new JsonArray();
    if (apps.Count == 0) return "至少保留一个软件。";
    if (apps.Count > 50) return "最多只能添加 50 个软件。";
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var reserved = ReservedNames();
    foreach (var item in apps)
    {
        var a = item as JsonObject;
        if (a == null) continue;
        var name = JsonText(a, "name").Trim();
        if (name.Length == 0) return "有软件还没有填写名称。";
        if (name.Length > 40) return "软件名称太长：" + name;
        if (!seen.Add(name)) return "软件名称重复：" + name;
        var procs = new List<string>();
        foreach (var raw in JsonTextArray(a, "processes"))
        {
            var p = NormalizeName(raw);
            if (p.Length == 0) continue;
            if (!IsSafeProcessName(p)) return name + " 的进程名不合法：" + p;
            if (reserved.Contains(p)) return name + " 不能监控系统进程：" + p;
            if (!procs.Any(x => x.Equals(p, StringComparison.OrdinalIgnoreCase))) procs.Add(p);
        }
        if (procs.Count == 0) return name + " 还没有填写监控进程。";
        if (procs.Count > 12) return name + " 的监控进程最多 12 个。";
        var transfer = new List<string>();
        foreach (var raw in JsonTextArray(a, "transferProcesses"))
        {
            var p = NormalizeName(raw);
            if (p.Length == 0) continue;
            if (!IsSafeProcessName(p)) return name + " 的传输进程名不合法：" + p;
            if (reserved.Contains(p)) return name + " 不能监控系统进程：" + p;
            if (!transfer.Any(x => x.Equals(p, StringComparison.OrdinalIgnoreCase))) transfer.Add(p);
        }
        if (transfer.Count > 12) return name + " 的传输进程最多 12 个。";
        var cmd = JsonText(a, "transferCommandLine").Trim();
        if (cmd.Length > 200) return name + " 的命令行筛选太长。";
        var idle = JsonDouble(a, "idleMinutes", 5.0);
        if (idle < 0.1) idle = 0.1;
        if (idle > 1440) idle = 1440;
        var threshold = JsonInt(a, "thresholdKB", 32);
        if (threshold < 1) threshold = 1;
        if (threshold > 1048576) threshold = 1048576;
        var app = new JsonObject();
        app["name"] = JsonValue.Create(name);
        app["enabled"] = JsonValue.Create(JsonBool(a, "enabled", true));
        var pa = new JsonArray();
        foreach (var p in procs) pa.Add(JsonValue.Create(p));
        app["processes"] = pa;
        var ta = new JsonArray();
        foreach (var p in transfer) ta.Add(JsonValue.Create(p));
        app["transferProcesses"] = ta;
        app["transferCommandLine"] = JsonValue.Create(cmd);
        app["idleMinutes"] = JsonValue.Create(idle);
        app["thresholdKB"] = JsonValue.Create(threshold);
        cleaned.Add(app);
    }
    if (cleaned.Count == 0) return "至少保留一个软件。";
    return "";
}

bool IsSafeProcessName(string name)
{
    return Regex.IsMatch(name, @"^[\p{L}\p{N}_.\- ]+$");
}

string NormalizeName(string name)
{
    name = (name ?? "").Trim();
    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        name = name.Substring(0, name.Length - 4);
    return name;
}

HashSet<string> ReservedNames()
{
    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var n in new string[]
    {
        "system", "idle", "registry", "memory compression", "secure system",
        "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost",
        "dwm", "fontdrvhost", "sihost", "taskhostw", "ctfmon", "explorer",
        "conhost", "powershell", "pwsh", "cmd", "wscript", "cscript", "mshta",
        "rundll32", "regsvr32", "spoolsv", "audiodg", "searchindexer",
        "wuauclt", "trustedinstaller", "searchhost", "startmenuexperiencehost",
        "shellexperiencehost", "textinputhost", "applicationframehost", "lockapp",
        "smartscreen", "securityhealthservice", "msmpeng", "nissrv",
        "backgroundtaskhost", "systemsettings", "searchapp", "runtimebroker",
        "dllhost", "taskhost", "searchprotocolhost", "searchfilterhost",
        "widgetservice"
    })
    {
        set.Add(n);
    }
    return set;
}

void ShowStatus(string dir)
{
    var cfg = LoadConfig(dir);
    var st = ReadStatus(dir);
    var alive = IsMonitorAlive(dir);
    var fresh = alive && IsMonitorFresh(dir);
    var statusMap = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
    if (st != null && st["apps"] is JsonArray statusApps)
    {
        foreach (var item in statusApps)
        {
            var o = item as JsonObject;
            if (o == null) continue;
            var n = JsonText(o, "name");
            if (n.Length > 0) statusMap[n] = o;
        }
    }
    var rows = new List<Dictionary<string, object?>>();
    if (cfg["apps"] is JsonArray apps)
    {
        foreach (var item in apps)
        {
            var a = item as JsonObject;
            if (a == null) continue;
            var name = JsonText(a, "name");
            var enabled = JsonBool(a, "enabled", true);
            JsonObject status = null;
            statusMap.TryGetValue(name, out status);
            var running = false;
            var count = 0;
            long bytes = 0;
            var idle = 0;
            var limit = 0;
            var kills = 0;
            var lastActive = "";
            if (status != null)
            {
                running = JsonBool(status, "running", false);
                count = JsonInt(status, "processCount", 0);
                bytes = JsonLong(status, "transferBytes", 0);
                idle = JsonInt(status, "idleSeconds", 0);
                limit = JsonInt(status, "idleLimitSeconds", 0);
                kills = JsonInt(status, "kills", 0);
                lastActive = JsonText(status, "lastActive");
            }
            string state;
            if (!enabled) state = "已停用";
            else if (!fresh) state = "监控未启动";
            else if (running) state = "运行中 · " + count + " 个进程";
            else state = "未运行";
            var row = new Dictionary<string, object?>();
            row["软件"] = name;
            row["状态"] = state;
            row["进程数"] = count.ToString();
            row["空闲秒数"] = idle.ToString();
            row["上限秒数"] = limit > 0 ? limit.ToString() : "—";
            row["本次传输"] = FormatBytes(bytes);
            row["已清理"] = kills + " 次";
            row["最近传输"] = lastActive.Length > 0 ? FormatTime(lastActive) : "—";
            rows.Add(row);
        }
    }
    if (rows.Count == 0)
    {
        rows.Add(new Dictionary<string, object?>
        {
            ["软件"] = "（未配置）", ["状态"] = "—", ["进程数"] = "0",
            ["空闲秒数"] = "0", ["上限秒数"] = "—", ["本次传输"] = "0 B",
            ["已清理"] = "0 次", ["最近传输"] = "—"
        });
    }
    var pid = 0;
    var updated = "";
    var lastEvent = "";
    if (st != null)
    {
        pid = JsonInt(st, "pid", 0);
        updated = JsonText(st, "updatedAt");
        lastEvent = JsonText(st, "lastEvent");
    }
    var help = fresh
        ? "后台监控运行中（PID " + pid + "） · 更新于 " + FormatTime(updated) + (lastEvent.Length > 0 ? " · " + lastEvent : "")
        : "后台监控未运行。右键动作选择“重启监控”即可恢复。";
    qk.Ui.EditTable(rows,
        new string[] { "软件", "状态", "进程数", "空闲秒数", "上限秒数", "本次传输", "已清理", "最近传输" },
        readOnly: true, width: 1020, height: 540,
        options: new UiOptions(Title: "传输空闲清理 · 运行状态", Help: help));
}

string FormatBytes(long bytes)
{
    if (bytes < 1024) return bytes.ToString() + " B";
    if (bytes < 1048576) return (bytes / 1024.0).ToString("0.0") + " KB";
    if (bytes < 1073741824) return (bytes / 1048576.0).ToString("0.0") + " MB";
    return (bytes / 1073741824.0).ToString("0.0") + " GB";
}

string FormatTime(string iso)
{
    if (string.IsNullOrWhiteSpace(iso)) return "—";
    DateTimeOffset t;
    if (DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t))
        return t.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    return iso;
}

void ShowLog(string dir)
{
    var path = Path.Combine(dir, "watchdog.log");
    if (!qk.Files.Exists(path))
    {
        qk.Ui.Alert("还没有日志。后台监控尚未产生记录。", "info");
        return;
    }
    var text = qk.Files.ReadText(path);
    var lines = text.Replace("\r", "").Split('\n');
    var start = Math.Max(0, lines.Length - 160);
    var tail = string.Join("\n", lines.Skip(start));
    qk.Ui.ShowText("传输空闲清理 · 后台监控日志（最后 " + (lines.Length - start) + " 行）\n\n" + tail,
        wrap: true, options: new UiOptions(Title: "监控日志"));
}

string SettingsSource()
{
    return Encoding.UTF8.GetString(Convert.FromBase64String(SettingsBase64()));
}

string SettingsBase64()
{
    return __SETTINGS_B64__;
}

string WatchdogBase64()
{
    return __WATCHDOG_B64__;
}
'''

body = TEMPLATE.replace("__SETTINGS_B64__", chunks(settings_b64))
body = body.replace("__WATCHDOG_B64__", chunks(watchdog_b64))
OUTPUT.write_text(body, encoding="utf-8", newline="\n")
print("wrote", OUTPUT, len(body), "chars")
print("watchdog b64", len(watchdog_b64), "settings b64", len(settings_b64))
