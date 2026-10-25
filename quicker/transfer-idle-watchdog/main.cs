void Main()
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
    return         "aW1wb3J0IHsgdXNlU3RhdGUgfSBmcm9tICdyZWFjdCc7DQppbXBvcnQgUXVpY2tlciBmcm9tICdRdWlja2VyJzsNCg0KY29uc3QgUkVTRVJWRUQgPSBbJ3N5c3RlbScsICdpZGxlJywgJ3JlZ2lzdHJ5JywgJ3Ntc3MnLCAnY3Nyc3MnLCAnd2luaW5pdCcsICd3aW5sb2dvbicsICdzZXJ2aWNlcycsICdsc2FzcycsICdzdmNob3N0JywgJ2R3bScsICdmb250ZHJ2aG9zdCcsICdzaWhvc3QnLCAndGFza2hvc3R3JywgJ2N0Zm1vbicsICdleHBsb3JlcicsICdjb25ob3N0JywgJ3Bvd2Vyc2hlbGwnLCAncHdzaCcsICdjbWQnLCAnd3NjcmlwdCcsICdjc2NyaXB0JywgJ21zaHRhJywgJ3J1bmRsbDMyJywgJ3JlZ3N2cjMyJywgJ3Nwb29sc3YnLCAnYXVkaW9kZycsICdzZWFyY2hpbmRleGVyJywgJ3RydXN0ZWRpbnN0YWxsZXInLCAnc2VhcmNoaG9zdCcsICdzdGFydG1lbnVleHBlcmllbmNlaG9zdCcsICdzaGVsbGV4cGVyaWVuY2Vob3N0JywgJ3RleHRpbnB1dGhvc3QnLCAnYXBwbGljYXRpb25mcmFtZWhvc3QnLCAnbG9ja2FwcCcsICdzbWFydHNjcmVlbicsICdzZWN1cml0eWhlYWx0aHNlcnZpY2UnLCAnbXNtcGVuZycsICduaXNzcnYnLCAnYmFja2dyb3VuZHRhc2tob3N0JywgJ3N5c3RlbXNldHRpbmdzJywgJ3NlYXJjaGFwcCcsICdydW50aW1lYnJva2VyJywgJ2RsbGhvc3QnLCAndGFza2hvc3QnLCAnc2VhcmNocHJvdG9jb2xob3N0JywgJ3NlYXJjaGZpbHRlcmhvc3QnLCAnd2lkZ2V0c2VydmljZSddOw0KDQpmdW5jdGlvbiBzcGxpdE5hbWVzKHZhbHVlKSB7DQogIHJldHVybiBTdHJpbmcodmFsdWUgfHwgJycpLnNwbGl0KC9bO++8myzvvIxcc10rLykubWFwKGZ1bmN0aW9uICh4KSB7IHJldHVybiB4LnRyaW0oKTsgfSkuZmlsdGVyKEJvb2xlYW4pOw0KfQ0KDQpmdW5jdGlvbiBjbGVhbk5hbWUodmFsdWUpIHsNCiAgcmV0dXJuIFN0cmluZyh2YWx1ZSB8fCAnJykudHJpbSgpLnJlcGxhY2UoL1wuZXhlJC9pLCAnJyk7DQp9DQoNCmZ1bmN0aW9uIGZvcm1hdE51bWJlcih2YWx1ZSkgew0KICBjb25zdCBuID0gTnVtYmVyKHZhbHVlKTsNCiAgaWYgKCFpc0Zpbml0ZShuKSkgcmV0dXJuICcwJzsNCiAgcmV0dXJuIFN0cmluZyhNYXRoLnJvdW5kKG4gKiAxMCkgLyAxMCk7DQp9DQoNCmZ1bmN0aW9uIGluaXRpYWxzKG5hbWUpIHsNCiAgY29uc3QgcyA9IFN0cmluZyhuYW1lIHx8ICcnKS50cmltKCk7DQogIGlmICghcykgcmV0dXJuICfmlrAnOw0KICBpZiAoL15bXHU0ZTAwLVx1OWZhNV0vLnRlc3QocykpIHJldHVybiBzLnNsaWNlKDAsIDEpOw0KICBjb25zdCBwYXJ0cyA9IHMuc3BsaXQoL1tcc18tXSsvKS5maWx0ZXIoQm9vbGVhbik7DQogIGlmIChwYXJ0cy5sZW5ndGggPiAxKSByZXR1cm4gKHBhcnRzWzBdLnNsaWNlKDAsIDEpICsgcGFydHNbMV0uc2xpY2UoMCwgMSkpLnRvVXBwZXJDYXNlKCk7DQogIHJldHVybiBzLnNsaWNlKDAsIDIpLnRvVXBwZXJDYXNlKCk7DQp9DQoNCmZ1bmN0aW9uIHRpbnRJbmRleChuYW1lKSB7DQogIGNvbnN0IHMgPSBTdHJpbmcobmFtZSB8fCAnJyk7DQogIGxldCBoID0gMDsNCiAgZm9yIChsZXQgaSA9IDA7IGkgPCBzLmxlbmd0aDsgaSsrKSBoID0gKGggKiAzMSArIHMuY2hhckNvZGVBdChpKSkgJSA5OTc7DQogIHJldHVybiBoICUgNjsNCn0NCg0KZnVuY3Rpb24gYnJpZWYodmFsdWUsIGxpbWl0KSB7DQogIGNvbnN0IHMgPSBTdHJpbmcodmFsdWUgfHwgJycpLnRyaW0oKTsNCiAgcmV0dXJuIHMubGVuZ3RoID4gbGltaXQgPyBzLnNsaWNlKDAsIGxpbWl0KSArICfigKYnIDogczsNCn0NCg0KZnVuY3Rpb24gcHJvY1N1bW1hcnkocHJvY3MpIHsNCiAgaWYgKCFwcm9jcy5sZW5ndGgpIHJldHVybiAn5pyq6K6+572uJzsNCiAgaWYgKHByb2NzLmxlbmd0aCA8PSAyKSByZXR1cm4gcHJvY3Muam9pbign44CBJyk7DQogIHJldHVybiBwcm9jcy5zbGljZSgwLCAyKS5qb2luKCfjgIEnKSArICcg562JICcgKyBwcm9jcy5sZW5ndGggKyAnIOS4qic7DQp9DQoNCmZ1bmN0aW9uIHRyYW5zZmVyU3VtbWFyeShhcHApIHsNCiAgY29uc3QgbGlzdCA9IHNwbGl0TmFtZXMoYXBwLnRyYW5zZmVyUHJvY2Vzc2VzKS5tYXAoY2xlYW5OYW1lKTsNCiAgaWYgKGxpc3QubGVuZ3RoID09PSAwKSByZXR1cm4gJ+WFqOmDqOi/m+eoiyc7DQogIGlmIChsaXN0Lmxlbmd0aCA8PSAyKSByZXR1cm4gbGlzdC5qb2luKCfjgIEnKTsNCiAgcmV0dXJuIGxpc3Quc2xpY2UoMCwgMikuam9pbign44CBJykgKyAnIOetiSAnICsgbGlzdC5sZW5ndGggKyAnIOS4qic7DQp9DQoNCmZ1bmN0aW9uIGlzUmVzZXJ2ZWQobmFtZSkgew0KICBjb25zdCBuID0gY2xlYW5OYW1lKG5hbWUpLnRvTG93ZXJDYXNlKCk7DQogIGlmICghbikgcmV0dXJuIHRydWU7DQogIGlmIChuLmluZGV4T2YoJ3F1aWNrZXInKSA9PT0gMCkgcmV0dXJuIHRydWU7DQogIHJldHVybiBSRVNFUlZFRC5pbmRleE9mKG4pID49IDA7DQp9DQoNCmNvbnN0IGNzcyA9IGANCiogeyBib3gtc2l6aW5nOiBib3JkZXItYm94OyB9DQpodG1sLCBib2R5IHsgbWFyZ2luOiAwOyBwYWRkaW5nOiAwOyB9DQouYXBwIHsNCiAgd2lkdGg6IDEwNDBweDsgaGVpZ2h0OiA3MjBweDsgbWFyZ2luOiAwIGF1dG87IGRpc3BsYXk6IGZsZXg7IGZsZXgtZGlyZWN0aW9uOiBjb2x1bW47DQogIC0tYmc6ICNmM2Y2ZmI7IC0tY2FyZDogI2ZmZmZmZjsgLS1zb2Z0OiAjZjZmOGZjOyAtLXRleHQ6ICMxYjIyMzA7IC0tbXV0ZWQ6ICM2Yjc2ODg7DQogIC0tbGluZTogI2UzZThmMTsgLS1saW5lLXN0cm9uZzogI2QzZGJlODsgLS1hY2NlbnQ6ICMyZjZmZWQ7IC0tYWNjZW50LXdlYWs6ICNlYWYxZmY7DQogIC0tZ3JlZW46ICMxZjlkNjM7IC0tZ3JlZW4td2VhazogI2U1ZjZlZTsgLS1kYW5nZXI6ICNjZjQ0MzY7IC0tc2hhZG93OiAwIDEwcHggMzBweCByZ2JhKDIzLCA0MywgNzcsIDAuMDYpOw0KICBiYWNrZ3JvdW5kOiByYWRpYWwtZ3JhZGllbnQoOTAwcHggMzgwcHggYXQgOTIlIC0xNDBweCwgcmdiYSg0NywgMTExLCAyMzcsIDAuMTApLCByZ2JhKDQ3LCAxMTEs" +
        "IDIzNywgMCkgNzAlKSwgdmFyKC0tYmcpOw0KICBjb2xvcjogdmFyKC0tdGV4dCk7IGZvbnQtc2l6ZTogMTZweDsgbGluZS1oZWlnaHQ6IDEuNTsNCiAgZm9udC1mYW1pbHk6ICdTZWdvZSBVSScsICdNaWNyb3NvZnQgWWFIZWkgVUknLCAnTWljcm9zb2Z0IFlhSGVpJywgc2Fucy1zZXJpZjsNCiAgb3ZlcmZsb3c6IGhpZGRlbjsNCn0NCi5hcHAuZGFyayB7DQogIC0tYmc6ICMxMjE1MWI7IC0tY2FyZDogIzFhMWYyNzsgLS1zb2Z0OiAjMTUxYTIxOyAtLXRleHQ6ICNlZWYyZjg7IC0tbXV0ZWQ6ICM5OGEyYjM7DQogIC0tbGluZTogIzI5MzAzYjsgLS1saW5lLXN0cm9uZzogIzMzM2M0OTsgLS1hY2NlbnQ6ICM3OWEzZmY7IC0tYWNjZW50LXdlYWs6ICMxYjI4NDI7DQogIC0tZ3JlZW46ICM1NGNmOTc7IC0tZ3JlZW4td2VhazogIzE2MzMyODsgLS1kYW5nZXI6ICNmZjgxNzc7IC0tc2hhZG93OiBub25lOw0KICBiYWNrZ3JvdW5kOiByYWRpYWwtZ3JhZGllbnQoOTAwcHggMzgwcHggYXQgOTIlIC0xNDBweCwgcmdiYSgxMDksIDE1NSwgMjU1LCAwLjEyKSwgcmdiYSgxMDksIDE1NSwgMjU1LCAwKSA3MCUpLCB2YXIoLS1iZyk7DQp9DQouaGVhZCB7IGRpc3BsYXk6IGZsZXg7IGFsaWduLWl0ZW1zOiBmbGV4LXN0YXJ0OyBqdXN0aWZ5LWNvbnRlbnQ6IHNwYWNlLWJldHdlZW47IGdhcDogMjBweDsgcGFkZGluZzogMjJweCAyOHB4IDEwcHg7IH0NCmgxIHsgbWFyZ2luOiAwOyBmb250LXNpemU6IDI0cHg7IGxpbmUtaGVpZ2h0OiAxLjM7IGZvbnQtd2VpZ2h0OiA2NTA7IGxldHRlci1zcGFjaW5nOiAwLjJweDsgfQ0KLnN1YiB7IG1hcmdpbjogNXB4IDAgMDsgY29sb3I6IHZhcigtLW11dGVkKTsgZm9udC1zaXplOiAxNC41cHg7IH0NCi5waWxsIHsgZGlzcGxheTogaW5saW5lLWZsZXg7IGFsaWduLWl0ZW1zOiBjZW50ZXI7IGdhcDogOHB4OyBwYWRkaW5nOiA3cHggMTNweDsgYm9yZGVyLXJhZGl1czogOTk5cHg7IGJvcmRlcjogMXB4IHNvbGlkIHZhcigtLWxpbmUpOyBjb2xvcjogdmFyKC0tbXV0ZWQpOyBiYWNrZ3JvdW5kOiB2YXIoLS1jYXJkKTsgZm9udC1zaXplOiAxMy41cHg7IHdoaXRlLXNwYWNlOiBub3dyYXA7IGJveC1zaGFkb3c6IHZhcigtLXNoYWRvdyk7IH0NCi5waWxsLW9uIHsgY29sb3I6IHZhcigtLWdyZWVuKTsgYmFja2dyb3VuZDogdmFyKC0tZ3JlZW4td2Vhayk7IGJvcmRlci1jb2xvcjogdHJhbnNwYXJlbnQ7IGJveC1zaGFkb3c6IG5vbmU7IH0NCi5kb3QgeyB3aWR0aDogOHB4OyBoZWlnaHQ6IDhweDsgYm9yZGVyLXJhZGl1czogNTAlOyBiYWNrZ3JvdW5kOiAjYWFiM2MyOyBkaXNwbGF5OiBpbmxpbmUtYmxvY2s7IGZsZXg6IG5vbmU7IH0NCi5kb3Qtb24geyBiYWNrZ3JvdW5kOiB2YXIoLS1ncmVlbik7IH0NCi50b29sYmFyIHsgZGlzcGxheTogZmxleDsgYWxpZ24taXRlbXM6IGNlbnRlcjsgZ2FwOiAxMnB4OyBwYWRkaW5nOiA2cHggMjhweCAxMnB4OyB9DQouc2VhcmNoIHsgcG9zaXRpb246IHJlbGF0aXZlOyBmbGV4OiAxOyBtYXgtd2lkdGg6IDQ0MHB4OyB9DQouc2VhcmNoLWljb24geyBwb3NpdGlvbjogYWJzb2x1dGU7IGxlZnQ6IDEzcHg7IHRvcDogNTAlOyB3aWR0aDogMTdweDsgaGVpZ2h0OiAxN3B4OyBtYXJnaW4tdG9wOiAtOC41cHg7IGNvbG9yOiB2YXIoLS1tdXRlZCk7IHBvaW50ZXItZXZlbnRzOiBub25lOyBvcGFjaXR5OiAwLjg7IH0NCi5zZWFyY2ggaW5wdXQgeyBwYWRkaW5nLWxlZnQ6IDM4cHg7IH0NCmlucHV0IHsNCiAgd2lkdGg6IDEwMCU7IGJvcmRlcjogMXB4IHNvbGlkIHZhcigtLWxpbmUpOyBib3JkZXItcmFkaXVzOiAxMHB4OyBiYWNrZ3JvdW5kOiB2YXIoLS1jYXJkKTsNCiAgY29sb3I6IHZhcigtLXRleHQpOyBwYWRkaW5nOiA5cHggMTJweDsgZm9udC1zaXplOiAxNXB4OyBvdXRsaW5lOiBub25lOyBmb250LWZhbWlseTogaW5oZXJpdDsNCiAgdHJhbnNpdGlvbjogYm9yZGVyLWNvbG9yIDAuMTVzLCBib3gtc2hhZG93IDAuMTVzOw0KfQ0KaW5wdXQ6OnBsYWNlaG9sZGVyIHsgY29sb3I6IHZhcigtLW11dGVkKTsgb3BhY2l0eTogMC43MjsgfQ0KaW5wdXQ6Zm9jdXMgeyBib3JkZXItY29sb3I6IHZhcigtLWFjY2VudCk7IGJveC1zaGFkb3c6IDAgMCAwIDNweCB2YXIoLS1hY2NlbnQtd2Vhayk7IH0NCmlucHV0W3R5cGU9bnVtYmVyXSB7IGFwcGVhcmFuY2U6IHRleHRmaWVsZDsgLW1vei1hcHBlYXJhbmNlOiB0ZXh0ZmllbGQ7IH0NCi5idG4gew0KICBib3JkZXI6IDFweCBzb2xpZCB2YXIoLS1saW5lKTsgYmFja2dyb3VuZDogdmFyKC0tY2FyZCk7IGNvbG9yOiB2YXIoLS10ZXh0KTsNCiAgYm9yZGVyLXJhZGl1czogMTBweDsgcGFkZGluZzogOXB4IDE1cHg7IGZvbnQtc2l6ZTogMTQuNXB4OyBjdXJzb3I6IHBvaW50ZXI7IHdoaXRlLXNwYWNlOiBub3dyYXA7DQogIGZvbnQtZmFtaWx5OiBpbmhlcml0OyBoZWlnaHQ6IGF1dG87IG1pbi13aWR0aDogMDsgdHJhbnNpdGlvbjogYmFja2dyb3VuZCAwLjE1cywgYm9yZGVyLWNvbG9yIDAuMTVzLCBjb2xvciAwLjE1czsNCn0NCi5idG46aG92ZXIgeyBib3JkZXItY29sb3I6IHZhcigtLWxpbmUtc3Ryb25nKTsgYmFja2dyb3VuZDogdmFyKC0tc29mdCk7IGNvbG9yOiB2YXIoLS10ZXh0KTsgfQ0KLmJ0bi1wcmltYXJ5IHsgYmFja2dyb3VuZDogdmFyKC0tYWNjZW50KTsgYm9yZGVyLWNvbG9yOiB2YXIoLS1hY2NlbnQpOyBjb2xvcjogI2ZmZmZmZjsgZm9udC13ZWlnaHQ6IDYwMDsgcGFkZGluZzogOXB4IDIwcHg7IH0NCi5idG4tcHJpbWFyeTpob3ZlciB7IGJhY2tncm91bmQ6IHZhcigtLWFjY2VudCk7IGJvcmRlci1jb2xvcjogdmFyKC0tYWNjZW50KTsgY29sb3I6ICNmZmZmZmY7IGZpbHRlcjogYnJpZ2h0bmVzcygxLjA3KTsgfQ0KLmJ0bi1hZGQgeyBiYWNrZ3JvdW5kOiB2YXIoLS1hY2NlbnQtd2Vhayk7IGJvcmRlci1jb2xvcjogdHJhbnNwYXJlbnQ7IGNvbG9yOiB2YXIoLS1hY2NlbnQpOyBmb250LXdlaWdodDogNjAwOyBtYXJnaW4tbGVmdDogYXV0bzsgfQ0KLmJ0bi1hZGQ6aG92ZXIgeyBi" +
        "YWNrZ3JvdW5kOiB2YXIoLS1hY2NlbnQtd2Vhayk7IGJvcmRlci1jb2xvcjogdHJhbnNwYXJlbnQ7IGNvbG9yOiB2YXIoLS1hY2NlbnQpOyBmaWx0ZXI6IGJyaWdodG5lc3MoMC45OCk7IH0NCi5wbHVzIHsgZm9udC1zaXplOiAxNnB4OyBmb250LXdlaWdodDogNjAwOyBtYXJnaW4tcmlnaHQ6IDNweDsgfQ0KLmJ0bi1naG9zdCB7IGJvcmRlci1jb2xvcjogdHJhbnNwYXJlbnQ7IGJhY2tncm91bmQ6IHRyYW5zcGFyZW50OyBjb2xvcjogdmFyKC0tbXV0ZWQpOyBwYWRkaW5nOiA1cHggOXB4OyBmb250LXNpemU6IDEzcHg7IGJvcmRlci1yYWRpdXM6IDhweDsgfQ0KLmJ0bi1naG9zdDpob3ZlciB7IGJvcmRlci1jb2xvcjogdHJhbnNwYXJlbnQ7IGNvbG9yOiB2YXIoLS1hY2NlbnQpOyBiYWNrZ3JvdW5kOiB2YXIoLS1hY2NlbnQtd2Vhayk7IH0NCi5idG4tZ2hvc3QuZGFuZ2VyOmhvdmVyIHsgY29sb3I6IHZhcigtLWRhbmdlcik7IGJhY2tncm91bmQ6IHJnYmEoMjA3LCA2OCwgNTQsIDAuMTApOyB9DQoucGlja2VyIHsgZGlzcGxheTogZmxleDsgYWxpZ24taXRlbXM6IGNlbnRlcjsgZ2FwOiAxMnB4OyBwYWRkaW5nOiAwIDI4cHggMTJweDsgbWluLXdpZHRoOiAwOyB9DQoucGlja2VyLWxhYmVsIHsgZGlzcGxheTogaW5saW5lLWZsZXg7IGFsaWduLWl0ZW1zOiBjZW50ZXI7IGdhcDogN3B4OyBmbGV4OiBub25lOyBmb250LXNpemU6IDEzcHg7IGNvbG9yOiB2YXIoLS1tdXRlZCk7IH0NCi5saXZlIHsgd2lkdGg6IDdweDsgaGVpZ2h0OiA3cHg7IGJvcmRlci1yYWRpdXM6IDUwJTsgYmFja2dyb3VuZDogdmFyKC0tZ3JlZW4pOyBib3gtc2hhZG93OiAwIDAgMCAzcHggdmFyKC0tZ3JlZW4td2Vhayk7IH0NCi5jaGlwcyB7IGRpc3BsYXk6IGZsZXg7IGFsaWduLWl0ZW1zOiBjZW50ZXI7IGdhcDogN3B4OyBmbGV4OiAxOyBtaW4td2lkdGg6IDA7IG92ZXJmbG93LXg6IGF1dG87IG92ZXJmbG93LXk6IGhpZGRlbjsgcGFkZGluZzogMnB4IDJweCA0cHg7IH0NCi5jaGlwczo6LXdlYmtpdC1zY3JvbGxiYXIgeyBoZWlnaHQ6IDRweDsgfQ0KLmNoaXBzOjotd2Via2l0LXNjcm9sbGJhci10aHVtYiB7IGJhY2tncm91bmQ6IHZhcigtLWxpbmUtc3Ryb25nKTsgYm9yZGVyLXJhZGl1czogNHB4OyB9DQouY2hpcCB7IGJvcmRlcjogMXB4IHNvbGlkIHZhcigtLWxpbmUpOyBiYWNrZ3JvdW5kOiB2YXIoLS1jYXJkKTsgY29sb3I6IHZhcigtLXRleHQpOyBib3JkZXItcmFkaXVzOiA5OTlweDsgcGFkZGluZzogNXB4IDExcHg7IGZvbnQtc2l6ZTogMTNweDsgY3Vyc29yOiBwb2ludGVyOyBmb250LWZhbWlseTogaW5oZXJpdDsgaGVpZ2h0OiBhdXRvOyBtaW4td2lkdGg6IDA7IHdoaXRlLXNwYWNlOiBub3dyYXA7IH0NCi5jaGlwOmhvdmVyIHsgYm9yZGVyLWNvbG9yOiB0cmFuc3BhcmVudDsgY29sb3I6IHZhcigtLWFjY2VudCk7IGJhY2tncm91bmQ6IHZhcigtLWFjY2VudC13ZWFrKTsgfQ0KLmNoaXAgLmNwbHVzIHsgY29sb3I6IHZhcigtLWFjY2VudCk7IG1hcmdpbi1yaWdodDogNHB4OyBmb250LXdlaWdodDogNjAwOyB9DQoucGlja2VyLWVtcHR5IHsgY29sb3I6IHZhcigtLW11dGVkKTsgZm9udC1zaXplOiAxM3B4OyB3aGl0ZS1zcGFjZTogbm93cmFwOyB9DQoubGlzdC1wYW5lbCB7DQogIGZsZXg6IDE7IG1pbi1oZWlnaHQ6IDA7IG1hcmdpbjogMCAyOHB4OyBkaXNwbGF5OiBmbGV4OyBmbGV4LWRpcmVjdGlvbjogY29sdW1uOw0KICBiYWNrZ3JvdW5kOiB2YXIoLS1jYXJkKTsgYm9yZGVyOiAxcHggc29saWQgdmFyKC0tbGluZSk7IGJvcmRlci1yYWRpdXM6IDE2cHg7DQogIGJveC1zaGFkb3c6IHZhcigtLXNoYWRvdyk7IG92ZXJmbG93OiBoaWRkZW47DQp9DQoubGlzdC1oZWFkIHsgZGlzcGxheTogZmxleDsgYWxpZ24taXRlbXM6IGNlbnRlcjsganVzdGlmeS1jb250ZW50OiBzcGFjZS1iZXR3ZWVuOyBnYXA6IDEycHg7IHBhZGRpbmc6IDEycHggMThweDsgYm9yZGVyLWJvdHRvbTogMXB4IHNvbGlkIHZhcigtLWxpbmUpOyB9DQoubGlzdC10aXRsZSB7IGRpc3BsYXk6IGZsZXg7IGFsaWduLWl0ZW1zOiBjZW50ZXI7IGdhcDogOXB4OyBmb250LXNpemU6IDE0LjVweDsgZm9udC13ZWlnaHQ6IDY1MDsgbGV0dGVyLXNwYWNpbmc6IDAuMnB4OyB9DQouY291bnQgeyBtaW4td2lkdGg6IDIycHg7IGhlaWdodDogMjJweDsgcGFkZGluZzogMCA3cHg7IGJvcmRlci1yYWRpdXM6IDk5OXB4OyBiYWNrZ3JvdW5kOiB2YXIoLS1hY2NlbnQtd2Vhayk7IGNvbG9yOiB2YXIoLS1hY2NlbnQpOyBmb250LXNpemU6IDEycHg7IGZvbnQtd2VpZ2h0OiA2NTA7IGRpc3BsYXk6IGlubGluZS1mbGV4OyBhbGlnbi1pdGVtczogY2VudGVyOyBqdXN0aWZ5LWNvbnRlbnQ6IGNlbnRlcjsgfQ0KLmxpc3QtbWV0YSB7IGZvbnQtc2l6ZTogMTIuNXB4OyBjb2xvcjogdmFyKC0tbXV0ZWQpOyB9DQoubGlzdC1ib2R5IHsgZmxleDogMTsgbWluLWhlaWdodDogMDsgb3ZlcmZsb3cteTogYXV0bzsgfQ0KLmxpc3QtYm9keTo6LXdlYmtpdC1zY3JvbGxiYXIgeyB3aWR0aDogOHB4OyB9DQoubGlzdC1ib2R5Ojotd2Via2l0LXNjcm9sbGJhci10aHVtYiB7IGJhY2tncm91bmQ6IHZhcigtLWxpbmUtc3Ryb25nKTsgYm9yZGVyLXJhZGl1czogOHB4OyB9DQoubGlzdC1ib2R5Ojotd2Via2l0LXNjcm9sbGJhci10cmFjayB7IGJhY2tncm91bmQ6IHRyYW5zcGFyZW50OyB9DQoucm93LXdyYXAgeyBib3JkZXItYm90dG9tOiAxcHggc29saWQgdmFyKC0tbGluZSk7IH0NCi5yb3ctd3JhcDpsYXN0LWNoaWxkIHsgYm9yZGVyLWJvdHRvbTogbm9uZTsgfQ0KLnJvdyB7IGRpc3BsYXk6IGZsZXg7IGFsaWduLWl0ZW1zOiBjZW50ZXI7IGdhcDogMTNweDsgcGFkZGluZzogMTJweCAxOHB4OyB0cmFuc2l0aW9uOiBiYWNrZ3JvdW5kIDAuMTVzOyB9DQoucm93OmhvdmVyLCAucm93LXdyYXAub3BlbiAucm93IHsgYmFja2dyb3VuZDogdmFyKC0tc29mdCk7IH0NCi5hdmF0YXIgeyBwb3NpdGlvbjogcmVsYXRpdmU7IGZsZXg6IG5vbmU7IHdpZHRoOiA0MnB4OyBoZWln" +
        "aHQ6IDQycHg7IGJvcmRlci1yYWRpdXM6IDEycHg7IGRpc3BsYXk6IGZsZXg7IGFsaWduLWl0ZW1zOiBjZW50ZXI7IGp1c3RpZnktY29udGVudDogY2VudGVyOyBmb250LXNpemU6IDE1cHg7IGZvbnQtd2VpZ2h0OiA3MDA7IGxldHRlci1zcGFjaW5nOiAwLjRweDsgfQ0KLmJhZGdlIHsgcG9zaXRpb246IGFic29sdXRlOyByaWdodDogLTNweDsgYm90dG9tOiAtM3B4OyB3aWR0aDogMTJweDsgaGVpZ2h0OiAxMnB4OyBib3JkZXItcmFkaXVzOiA1MCU7IGJvcmRlcjogMnB4IHNvbGlkIHZhcigtLWNhcmQpOyBiYWNrZ3JvdW5kOiAjYjNiY2M5OyB9DQouYmFkZ2UubGl2ZSB7IGJhY2tncm91bmQ6IHZhcigtLWdyZWVuKTsgfQ0KLnRpbnQtMCB7IGJhY2tncm91bmQ6ICNlOGYwZmY7IGNvbG9yOiAjMmY2ZmVkOyB9DQoudGludC0xIHsgYmFja2dyb3VuZDogI2ZkZWVlNzsgY29sb3I6ICNkOTY2M2E7IH0NCi50aW50LTIgeyBiYWNrZ3JvdW5kOiAjZTlmN2VmOyBjb2xvcjogIzFmOWQ2MzsgfQ0KLnRpbnQtMyB7IGJhY2tncm91bmQ6ICNmMmVjZmY7IGNvbG9yOiAjN2E1YWY1OyB9DQoudGludC00IHsgYmFja2dyb3VuZDogI2ZmZjVlMzsgY29sb3I6ICNiNzc5MWY7IH0NCi50aW50LTUgeyBiYWNrZ3JvdW5kOiAjZTZmNmZiOyBjb2xvcjogIzFmN2E5NjsgfQ0KLmFwcC5kYXJrIC50aW50LTAgeyBiYWNrZ3JvdW5kOiAjMWQyYjQ3OyBjb2xvcjogIzhmYjJmZjsgfQ0KLmFwcC5kYXJrIC50aW50LTEgeyBiYWNrZ3JvdW5kOiAjM2EyNTFkOyBjb2xvcjogI2ZmYjA4YTsgfQ0KLmFwcC5kYXJrIC50aW50LTIgeyBiYWNrZ3JvdW5kOiAjMTYzMzI4OyBjb2xvcjogIzZmZGNiMDsgfQ0KLmFwcC5kYXJrIC50aW50LTMgeyBiYWNrZ3JvdW5kOiAjMmEyMzQzOyBjb2xvcjogI2I5YTNmZjsgfQ0KLmFwcC5kYXJrIC50aW50LTQgeyBiYWNrZ3JvdW5kOiAjM2EyZjFiOyBjb2xvcjogI2YwYzY3NDsgfQ0KLmFwcC5kYXJrIC50aW50LTUgeyBiYWNrZ3JvdW5kOiAjMTczMTNhOyBjb2xvcjogIzdkZDNlODsgfQ0KLnJvdy1tYWluIHsgZmxleDogMTsgbWluLXdpZHRoOiAwOyBjdXJzb3I6IHBvaW50ZXI7IH0NCi5yb3ctdGl0bGUgeyBkaXNwbGF5OiBmbGV4OyBhbGlnbi1pdGVtczogY2VudGVyOyBnYXA6IDhweDsgbWluLXdpZHRoOiAwOyB9DQoubmFtZSB7IGZvbnQtc2l6ZTogMTZweDsgZm9udC13ZWlnaHQ6IDY1MDsgb3ZlcmZsb3c6IGhpZGRlbjsgdGV4dC1vdmVyZmxvdzogZWxsaXBzaXM7IHdoaXRlLXNwYWNlOiBub3dyYXA7IH0NCi5zdGF0ZSB7IGZsZXg6IG5vbmU7IGZvbnQtc2l6ZTogMTEuNXB4OyBwYWRkaW5nOiAxcHggN3B4OyBib3JkZXItcmFkaXVzOiA5OTlweDsgY29sb3I6IHZhcigtLW11dGVkKTsgYmFja2dyb3VuZDogdmFyKC0tYmcpOyBib3JkZXI6IDFweCBzb2xpZCB2YXIoLS1saW5lKTsgbGluZS1oZWlnaHQ6IDEuNjsgfQ0KLnN0YXRlLm9uIHsgY29sb3I6IHZhcigtLWdyZWVuKTsgYmFja2dyb3VuZDogdmFyKC0tZ3JlZW4td2Vhayk7IGJvcmRlci1jb2xvcjogdHJhbnNwYXJlbnQ7IH0NCi5zdGF0ZS5vZmYgeyBvcGFjaXR5OiAwLjc1OyB9DQoucm93LXN1YiB7IG1hcmdpbi10b3A6IDNweDsgY29sb3I6IHZhcigtLW11dGVkKTsgZm9udC1zaXplOiAxMi41cHg7IG92ZXJmbG93OiBoaWRkZW47IHRleHQtb3ZlcmZsb3c6IGVsbGlwc2lzOyB3aGl0ZS1zcGFjZTogbm93cmFwOyB9DQoudGFncyB7IG1hcmdpbi10b3A6IDZweDsgZGlzcGxheTogZmxleDsgZmxleC13cmFwOiB3cmFwOyBnYXA6IDVweDsgfQ0KLnRhZyB7IGZvbnQtc2l6ZTogMTEuNXB4OyBsaW5lLWhlaWdodDogMS41OyBjb2xvcjogdmFyKC0tbXV0ZWQpOyBiYWNrZ3JvdW5kOiB2YXIoLS1iZyk7IGJvcmRlcjogMXB4IHNvbGlkIHZhcigtLWxpbmUpOyBib3JkZXItcmFkaXVzOiA2cHg7IHBhZGRpbmc6IDFweCA3cHg7IHdoaXRlLXNwYWNlOiBub3dyYXA7IH0NCi50YWctYWNjZW50IHsgY29sb3I6IHZhcigtLWFjY2VudCk7IGJhY2tncm91bmQ6IHZhcigtLWFjY2VudC13ZWFrKTsgYm9yZGVyLWNvbG9yOiB0cmFuc3BhcmVudDsgZm9udC13ZWlnaHQ6IDYwMDsgfQ0KLnRhZy1kaW0geyBvcGFjaXR5OiAwLjg1OyB9DQoucm93LWFjdGlvbnMgeyBkaXNwbGF5OiBmbGV4OyBhbGlnbi1pdGVtczogY2VudGVyOyBnYXA6IDEwcHg7IGZsZXg6IG5vbmU7IH0NCi5yb3ctYnRucyB7IGRpc3BsYXk6IGZsZXg7IGFsaWduLWl0ZW1zOiBjZW50ZXI7IGdhcDogMDsgfQ0KLnN3aXRjaCB7IHBvc2l0aW9uOiByZWxhdGl2ZTsgd2lkdGg6IDQwcHg7IGhlaWdodDogMjNweDsgZGlzcGxheTogaW5saW5lLWJsb2NrOyBmbGV4OiBub25lOyB9DQouc3dpdGNoIGlucHV0IHsgb3BhY2l0eTogMDsgd2lkdGg6IDA7IGhlaWdodDogMDsgfQ0KLnNsaWRlciB7IHBvc2l0aW9uOiBhYnNvbHV0ZTsgaW5zZXQ6IDA7IGJhY2tncm91bmQ6ICNiOWMxY2U7IGJvcmRlci1yYWRpdXM6IDk5OXB4OyB0cmFuc2l0aW9uOiBiYWNrZ3JvdW5kIDAuMTZzOyBjdXJzb3I6IHBvaW50ZXI7IH0NCi5zbGlkZXI6YmVmb3JlIHsgY29udGVudDogJyc7IHBvc2l0aW9uOiBhYnNvbHV0ZTsgd2lkdGg6IDE3cHg7IGhlaWdodDogMTdweDsgbGVmdDogM3B4OyB0b3A6IDNweDsgYmFja2dyb3VuZDogI2ZmZmZmZjsgYm9yZGVyLXJhZGl1czogNTAlOyB0cmFuc2l0aW9uOiB0cmFuc2Zvcm0gMC4xNnM7IGJveC1zaGFkb3c6IDAgMXB4IDJweCByZ2JhKDAsIDAsIDAsIDAuMik7IH0NCi5zd2l0Y2ggaW5wdXQ6Y2hlY2tlZCArIC5zbGlkZXIgeyBiYWNrZ3JvdW5kOiB2YXIoLS1hY2NlbnQpOyB9DQouc3dpdGNoIGlucHV0OmNoZWNrZWQgKyAuc2xpZGVyOmJlZm9yZSB7IHRyYW5zZm9ybTogdHJhbnNsYXRlWCgxN3B4KTsgfQ0KLmFwcC5kYXJrIC5zbGlkZXIgeyBiYWNrZ3JvdW5kOiAjM2I0NTU0OyB9DQouZWRpdG9yIHsgcGFkZGluZzogMCAxOHB4IDE0cHggNzNweDsgYmFja2dyb3VuZDogdmFyKC0tc29mdCk7IGFuaW1hdGlvbjogcmlzZSAwLjE2cyBlYXNlLW91dDsgfQ0KQGtleWZy" +
        "YW1lcyByaXNlIHsgZnJvbSB7IG9wYWNpdHk6IDA7IHRyYW5zZm9ybTogdHJhbnNsYXRlWSgtM3B4KTsgfSB0byB7IG9wYWNpdHk6IDE7IHRyYW5zZm9ybTogbm9uZTsgfSB9DQouZWRpdG9yLWJveCB7IGJvcmRlcjogMXB4IHNvbGlkIHZhcigtLWxpbmUpOyBiYWNrZ3JvdW5kOiB2YXIoLS1jYXJkKTsgYm9yZGVyLXJhZGl1czogMTJweDsgcGFkZGluZzogMTNweCAxNHB4IDExcHg7IH0NCi5lZGl0b3ItZ3JpZCB7IGRpc3BsYXk6IGdyaWQ7IGdyaWQtdGVtcGxhdGUtY29sdW1uczogcmVwZWF0KDQsIG1pbm1heCgwLCAxZnIpKTsgZ2FwOiAxMXB4IDEzcHg7IH0NCi5maWVsZCBsYWJlbCB7IGRpc3BsYXk6IGJsb2NrOyBmb250LXNpemU6IDEycHg7IGNvbG9yOiB2YXIoLS1tdXRlZCk7IG1hcmdpbi1ib3R0b206IDVweDsgfQ0KLmZpZWxkIGxhYmVsIC5vcHQgeyBvcGFjaXR5OiAwLjc7IGZvbnQtd2VpZ2h0OiA0MDA7IG1hcmdpbi1sZWZ0OiA1cHg7IH0NCi5maWVsZCBpbnB1dCB7IGZvbnQtc2l6ZTogMTRweDsgcGFkZGluZzogN3B4IDEwcHg7IGJvcmRlci1yYWRpdXM6IDhweDsgfQ0KLnNwYW4yIHsgZ3JpZC1jb2x1bW46IHNwYW4gMjsgfQ0KLmVkaXRvci1ub3RlIHsgZ3JpZC1jb2x1bW46IHNwYW4gMjsgZGlzcGxheTogZmxleDsgYWxpZ24taXRlbXM6IGZsZXgtZW5kOyBwYWRkaW5nLWJvdHRvbTogMnB4OyBjb2xvcjogdmFyKC0tbXV0ZWQpOyBmb250LXNpemU6IDEycHg7IGxpbmUtaGVpZ2h0OiAxLjY7IH0NCi5lbXB0eSB7IHBhZGRpbmc6IDU0cHggMjBweCA1NnB4OyB0ZXh0LWFsaWduOiBjZW50ZXI7IH0NCi5lbXB0eS1pY29uIHsgd2lkdGg6IDQ2cHg7IGhlaWdodDogNDZweDsgbWFyZ2luOiAwIGF1dG8gMTBweDsgYm9yZGVyLXJhZGl1czogMTNweDsgYmFja2dyb3VuZDogdmFyKC0tc29mdCk7IGJvcmRlcjogMXB4IHNvbGlkIHZhcigtLWxpbmUpOyBjb2xvcjogdmFyKC0tbXV0ZWQpOyBkaXNwbGF5OiBmbGV4OyBhbGlnbi1pdGVtczogY2VudGVyOyBqdXN0aWZ5LWNvbnRlbnQ6IGNlbnRlcjsgfQ0KLmVtcHR5LXRpdGxlIHsgZm9udC1zaXplOiAxNXB4OyBmb250LXdlaWdodDogNjAwOyB9DQouZW1wdHktc3ViIHsgbWFyZ2luLXRvcDogM3B4OyBmb250LXNpemU6IDEyLjVweDsgY29sb3I6IHZhcigtLW11dGVkKTsgfQ0KLmxpc3QtZm9vdCB7IGRpc3BsYXk6IGZsZXg7IGFsaWduLWl0ZW1zOiBjZW50ZXI7IGp1c3RpZnktY29udGVudDogc3BhY2UtYmV0d2VlbjsgZ2FwOiAxMnB4OyBwYWRkaW5nOiA4cHggMThweDsgYm9yZGVyLXRvcDogMXB4IHNvbGlkIHZhcigtLWxpbmUpOyBjb2xvcjogdmFyKC0tbXV0ZWQpOyBmb250LXNpemU6IDEycHg7IH0NCi5mb290LXJpZ2h0IHsgb3BhY2l0eTogMC45OyB3aGl0ZS1zcGFjZTogbm93cmFwOyB9DQouZm9vdCB7IGRpc3BsYXk6IGZsZXg7IGFsaWduLWl0ZW1zOiBjZW50ZXI7IGp1c3RpZnktY29udGVudDogc3BhY2UtYmV0d2VlbjsgZ2FwOiAxNnB4OyBwYWRkaW5nOiAxM3B4IDI4cHggMTZweDsgfQ0KLmhpbnQgeyBjb2xvcjogdmFyKC0tbXV0ZWQpOyBmb250LXNpemU6IDEzcHg7IH0NCi5lcnJvciB7IGNvbG9yOiB2YXIoLS1kYW5nZXIpOyBmb250LXNpemU6IDEzcHg7IGZvbnQtd2VpZ2h0OiA2MDA7IH0NCi5mb290LWJ0bnMgeyBkaXNwbGF5OiBmbGV4OyBnYXA6IDEwcHg7IGZsZXg6IG5vbmU7IH0NCi5tdXRlZCB7IGNvbG9yOiB2YXIoLS1tdXRlZCk7IH0NCmA7DQoNCmV4cG9ydCBkZWZhdWx0IGZ1bmN0aW9uIEFwcCgpIHsNCiAgY29uc3QgaW5wdXQgPSBRdWlja2VyLmdldElucHV0KCkgfHwge307DQogIGNvbnN0IHRoZW1lID0gKFF1aWNrZXIgYXMgYW55KS50aGVtZTsNCiAgY29uc3QgZGFyayA9IHRoZW1lID09PSAnZGFyaycgfHwgKHRoZW1lICYmIHRoZW1lLm1vZGUgPT09ICdkYXJrJyk7DQogIGNvbnN0IHJ1bm5pbmcgPSBBcnJheS5pc0FycmF5KGlucHV0LnJ1bm5pbmcpID8gaW5wdXQucnVubmluZyA6IFtdOw0KICBjb25zdCBtb25pdG9yID0gaW5wdXQubW9uaXRvciB8fCB7fTsNCiAgY29uc3Qgc291cmNlQXBwcyA9IEFycmF5LmlzQXJyYXkoaW5wdXQuYXBwcykgPyBpbnB1dC5hcHBzIDogW107DQogIGNvbnN0IGluaXRpYWwgPSBzb3VyY2VBcHBzLm1hcChmdW5jdGlvbiAoYSkgew0KICAgIHJldHVybiB7DQogICAgICBuYW1lOiBTdHJpbmcoYS5uYW1lIHx8ICcnKSwNCiAgICAgIGVuYWJsZWQ6IGEuZW5hYmxlZCAhPT0gZmFsc2UsDQogICAgICBwcm9jZXNzZXM6IEFycmF5LmlzQXJyYXkoYS5wcm9jZXNzZXMpID8gYS5wcm9jZXNzZXMuam9pbignOyAnKSA6IFN0cmluZyhhLnByb2Nlc3NlcyB8fCAnJyksDQogICAgICB0cmFuc2ZlclByb2Nlc3NlczogQXJyYXkuaXNBcnJheShhLnRyYW5zZmVyUHJvY2Vzc2VzKSA/IGEudHJhbnNmZXJQcm9jZXNzZXMuam9pbignOyAnKSA6IFN0cmluZyhhLnRyYW5zZmVyUHJvY2Vzc2VzIHx8ICcnKSwNCiAgICAgIHRyYW5zZmVyQ29tbWFuZExpbmU6IFN0cmluZyhhLnRyYW5zZmVyQ29tbWFuZExpbmUgfHwgJycpLA0KICAgICAgaWRsZU1pbnV0ZXM6IE51bWJlcihhLmlkbGVNaW51dGVzKSB8fCA1LA0KICAgICAgdGhyZXNob2xkS0I6IE51bWJlcihhLnRocmVzaG9sZEtCKSB8fCAzMg0KICAgIH07DQogIH0pOw0KICBjb25zdCBbYXBwcywgc2V0QXBwc10gPSB1c2VTdGF0ZShpbml0aWFsKTsNCiAgY29uc3QgW2VkaXRpbmcsIHNldEVkaXRpbmddID0gdXNlU3RhdGUoLTEpOw0KICBjb25zdCBbZmlsdGVyLCBzZXRGaWx0ZXJdID0gdXNlU3RhdGUoJycpOw0KICBjb25zdCBbZXJyb3IsIHNldEVycm9yXSA9IHVzZVN0YXRlKCcnKTsNCiAgY29uc3QgcnVubmluZ05hbWVzID0gcnVubmluZy5tYXAoZnVuY3Rpb24gKHIpIHsgcmV0dXJuIGNsZWFuTmFtZShyLm5hbWUpLnRvTG93ZXJDYXNlKCk7IH0pOw0KDQogIGZ1bmN0aW9uIHBhdGNoKGluZGV4LCBrZXksIHZhbHVlKSB7DQogICAgc2V0QXBwcyhhcHBzLm1hcChmdW5jdGlvbiAoYSwgaSkgew0KICAgICAgaWYgKGkgIT09IGluZGV4" +
        "KSByZXR1cm4gYTsNCiAgICAgIGNvbnN0IGNvcHkgPSBPYmplY3QuYXNzaWduKHt9LCBhKTsNCiAgICAgIGNvcHlba2V5XSA9IHZhbHVlOw0KICAgICAgcmV0dXJuIGNvcHk7DQogICAgfSkpOw0KICB9DQoNCiAgZnVuY3Rpb24gcmVtb3ZlQXBwKGluZGV4KSB7DQogICAgc2V0QXBwcyhhcHBzLmZpbHRlcihmdW5jdGlvbiAoYSwgaSkgeyByZXR1cm4gaSAhPT0gaW5kZXg7IH0pKTsNCiAgICBpZiAoZWRpdGluZyA9PT0gaW5kZXgpIHNldEVkaXRpbmcoLTEpOw0KICAgIGlmIChlZGl0aW5nID4gaW5kZXgpIHNldEVkaXRpbmcoZWRpdGluZyAtIDEpOw0KICAgIHNldEVycm9yKCcnKTsNCiAgfQ0KDQogIGZ1bmN0aW9uIGFkZENhcmQobmFtZSwgcHJvY2Vzc05hbWUpIHsNCiAgICBjb25zdCBuZXh0ID0gYXBwcy5jb25jYXQoW3sNCiAgICAgIG5hbWU6IG5hbWUsDQogICAgICBlbmFibGVkOiB0cnVlLA0KICAgICAgcHJvY2Vzc2VzOiBwcm9jZXNzTmFtZSwNCiAgICAgIHRyYW5zZmVyUHJvY2Vzc2VzOiAnJywNCiAgICAgIHRyYW5zZmVyQ29tbWFuZExpbmU6ICcnLA0KICAgICAgaWRsZU1pbnV0ZXM6IDUsDQogICAgICB0aHJlc2hvbGRLQjogMzINCiAgICB9XSk7DQogICAgc2V0QXBwcyhuZXh0KTsNCiAgICBzZXRFZGl0aW5nKG5leHQubGVuZ3RoIC0gMSk7DQogICAgc2V0RmlsdGVyKCcnKTsNCiAgICBzZXRFcnJvcignJyk7DQogIH0NCg0KICBmdW5jdGlvbiBjb25maWd1cmVkKHByb2Nlc3NOYW1lKSB7DQogICAgY29uc3QgbiA9IGNsZWFuTmFtZShwcm9jZXNzTmFtZSkudG9Mb3dlckNhc2UoKTsNCiAgICByZXR1cm4gYXBwcy5zb21lKGZ1bmN0aW9uIChhKSB7DQogICAgICByZXR1cm4gc3BsaXROYW1lcyhhLnByb2Nlc3Nlcykuc29tZShmdW5jdGlvbiAocCkgeyByZXR1cm4gY2xlYW5OYW1lKHApLnRvTG93ZXJDYXNlKCkgPT09IG47IH0pOw0KICAgIH0pOw0KICB9DQoNCiAgZnVuY3Rpb24gYXBwUnVubmluZyhhcHApIHsNCiAgICByZXR1cm4gc3BsaXROYW1lcyhhcHAucHJvY2Vzc2VzKS5zb21lKGZ1bmN0aW9uIChwKSB7DQogICAgICByZXR1cm4gcnVubmluZ05hbWVzLmluZGV4T2YoY2xlYW5OYW1lKHApLnRvTG93ZXJDYXNlKCkpID49IDA7DQogICAgfSk7DQogIH0NCg0KICBmdW5jdGlvbiBjb2xsZWN0KCkgew0KICAgIGNvbnN0IG91dCA9IFtdOw0KICAgIGNvbnN0IHNlZW4gPSB7fTsNCiAgICBmb3IgKGxldCBpID0gMDsgaSA8IGFwcHMubGVuZ3RoOyBpKyspIHsNCiAgICAgIGNvbnN0IGEgPSBhcHBzW2ldOw0KICAgICAgY29uc3QgZGlzcGxheSA9IFN0cmluZyhhLm5hbWUgfHwgJycpLnRyaW0oKTsNCiAgICAgIGlmICghZGlzcGxheSkgeyBzZXRFcnJvcign56ysICcgKyAoaSArIDEpICsgJyDkuKrova/ku7bov5jmsqHmnInloavlhpnlkI3np7DjgIInKTsgc2V0RWRpdGluZyhpKTsgcmV0dXJuIG51bGw7IH0NCiAgICAgIGNvbnN0IGtleSA9IGRpc3BsYXkudG9Mb3dlckNhc2UoKTsNCiAgICAgIGlmIChzZWVuW2tleV0pIHsgc2V0RXJyb3IoJ+i9r+S7tuWQjeensOmHjeWkje+8micgKyBkaXNwbGF5KTsgc2V0RWRpdGluZyhpKTsgcmV0dXJuIG51bGw7IH0NCiAgICAgIHNlZW5ba2V5XSA9IHRydWU7DQogICAgICBjb25zdCBwcm9jcyA9IHNwbGl0TmFtZXMoYS5wcm9jZXNzZXMpLm1hcChjbGVhbk5hbWUpLmZpbHRlcihCb29sZWFuKTsNCiAgICAgIGlmIChwcm9jcy5sZW5ndGggPT09IDApIHsgc2V0RXJyb3IoZGlzcGxheSArICcg6L+Y5rKh5pyJ5aGr5YaZ55uR5o6n6L+b56iL44CCJyk7IHNldEVkaXRpbmcoaSk7IHJldHVybiBudWxsOyB9DQogICAgICBmb3IgKGxldCBqID0gMDsgaiA8IHByb2NzLmxlbmd0aDsgaisrKSB7DQogICAgICAgIGNvbnN0IHAgPSBwcm9jc1tqXTsNCiAgICAgICAgaWYgKCEvXltccHtMfVxwe059Xy5cLSBdKyQvdS50ZXN0KHApKSB7IHNldEVycm9yKCfov5vnqIvlkI3kuI3lkIjms5XvvJonICsgcCk7IHNldEVkaXRpbmcoaSk7IHJldHVybiBudWxsOyB9DQogICAgICAgIGlmIChpc1Jlc2VydmVkKHApKSB7IHNldEVycm9yKCfkuI3og73nm5Hmjqfns7vnu5/miJblpJblo7Pov5vnqIvvvJonICsgcCk7IHNldEVkaXRpbmcoaSk7IHJldHVybiBudWxsOyB9DQogICAgICB9DQogICAgICBjb25zdCBpZGxlID0gTnVtYmVyKGEuaWRsZU1pbnV0ZXMpOw0KICAgICAgaWYgKCEoaWRsZSA+PSAwLjEgJiYgaWRsZSA8PSAxNDQwKSkgeyBzZXRFcnJvcihkaXNwbGF5ICsgJyDnmoTnqbrpl7LliIbpkp/lupTkuLogMC4xIOWIsCAxNDQw44CCJyk7IHNldEVkaXRpbmcoaSk7IHJldHVybiBudWxsOyB9DQogICAgICBjb25zdCB0aHJlc2hvbGQgPSBOdW1iZXIoYS50aHJlc2hvbGRLQik7DQogICAgICBpZiAoISh0aHJlc2hvbGQgPj0gMSAmJiB0aHJlc2hvbGQgPD0gMTA0ODU3NikpIHsgc2V0RXJyb3IoZGlzcGxheSArICcg55qE6ZiI5YC85bqU5Li6IDEg5YiwIDEwNDg1NzYgS0LjgIInKTsgc2V0RWRpdGluZyhpKTsgcmV0dXJuIG51bGw7IH0NCiAgICAgIG91dC5wdXNoKHsNCiAgICAgICAgbmFtZTogZGlzcGxheSwNCiAgICAgICAgZW5hYmxlZDogYS5lbmFibGVkICE9PSBmYWxzZSwNCiAgICAgICAgcHJvY2Vzc2VzOiBwcm9jcywNCiAgICAgICAgdHJhbnNmZXJQcm9jZXNzZXM6IHNwbGl0TmFtZXMoYS50cmFuc2ZlclByb2Nlc3NlcykubWFwKGNsZWFuTmFtZSkuZmlsdGVyKEJvb2xlYW4pLA0KICAgICAgICB0cmFuc2ZlckNvbW1hbmRMaW5lOiBTdHJpbmcoYS50cmFuc2ZlckNvbW1hbmRMaW5lIHx8ICcnKS50cmltKCksDQogICAgICAgIGlkbGVNaW51dGVzOiBpZGxlLA0KICAgICAgICB0aHJlc2hvbGRLQjogTWF0aC5yb3VuZCh0aHJlc2hvbGQpDQogICAgICB9KTsNCiAgICB9DQogICAgaWYgKG91dC5sZW5ndGggPT09IDApIHsgc2V0RXJyb3IoJ+iHs+Wwkea3u+WKoOS4gOS4qui9r+S7tuOAgicpOyByZXR1cm4gbnVsbDsgfQ0KICAgIHJldHVybiBvdXQ7DQogIH0NCg0KICBmdW5jdGlvbiBzYXZlKCkgew0KICAgIHNldEVycm9yKCcnKTsNCiAg" +
        "ICBjb25zdCByZXN1bHQgPSBjb2xsZWN0KCk7DQogICAgaWYgKCFyZXN1bHQpIHJldHVybjsNCiAgICBRdWlja2VyLmNsb3NlKHsgYXBwczogcmVzdWx0IH0pOw0KICB9DQoNCiAgY29uc3QgcSA9IGZpbHRlci50cmltKCkudG9Mb3dlckNhc2UoKTsNCiAgY29uc3QgdmlzaWJsZSA9IGFwcHMubWFwKGZ1bmN0aW9uIChhLCBpKSB7IHJldHVybiB7IGFwcDogYSwgaW5kZXg6IGkgfTsgfSkuZmlsdGVyKGZ1bmN0aW9uIChpdGVtKSB7DQogICAgaWYgKCFxKSByZXR1cm4gdHJ1ZTsNCiAgICBjb25zdCB0ZXh0ID0gKGl0ZW0uYXBwLm5hbWUgKyAnICcgKyBpdGVtLmFwcC5wcm9jZXNzZXMgKyAnICcgKyBpdGVtLmFwcC50cmFuc2ZlclByb2Nlc3NlcyArICcgJyArIGl0ZW0uYXBwLnRyYW5zZmVyQ29tbWFuZExpbmUpLnRvTG93ZXJDYXNlKCk7DQogICAgcmV0dXJuIHRleHQuaW5kZXhPZihxKSA+PSAwOw0KICB9KTsNCiAgY29uc3QgcGlja2FibGUgPSBydW5uaW5nLmZpbHRlcihmdW5jdGlvbiAocikgew0KICAgIGlmICghciB8fCAhci5uYW1lKSByZXR1cm4gZmFsc2U7DQogICAgaWYgKGlzUmVzZXJ2ZWQoci5uYW1lKSB8fCBjb25maWd1cmVkKHIubmFtZSkpIHJldHVybiBmYWxzZTsNCiAgICBpZiAoIXEpIHJldHVybiB0cnVlOw0KICAgIHJldHVybiAoU3RyaW5nKHIubmFtZSkgKyAnICcgKyBTdHJpbmcoci50aXRsZSB8fCAnJykpLnRvTG93ZXJDYXNlKCkuaW5kZXhPZihxKSA+PSAwOw0KICB9KS5zbGljZSgwLCAzMCk7DQogIGNvbnN0IGVuYWJsZWRDb3VudCA9IGFwcHMuZmlsdGVyKGZ1bmN0aW9uIChhKSB7IHJldHVybiBhLmVuYWJsZWQgIT09IGZhbHNlOyB9KS5sZW5ndGg7DQogIGNvbnN0IHJ1bm5pbmdDb3VudCA9IGFwcHMuZmlsdGVyKGZ1bmN0aW9uIChhKSB7IHJldHVybiBhLmVuYWJsZWQgIT09IGZhbHNlICYmIGFwcFJ1bm5pbmcoYSk7IH0pLmxlbmd0aDsNCg0KICByZXR1cm4gKA0KICAgIDxtYWluIGNsYXNzTmFtZT0ncWstdWktcGFnZSc+DQogICAgICA8ZGl2IGNsYXNzTmFtZT17ZGFyayA/ICdhcHAgZGFyaycgOiAnYXBwJ30+DQogICAgICAgIDxzdHlsZT57Y3NzfTwvc3R5bGU+DQogICAgICAgIDxoZWFkZXIgY2xhc3NOYW1lPSdoZWFkJz4NCiAgICAgICAgICA8ZGl2Pg0KICAgICAgICAgICAgPGgxPuS8oOi+k+epuumXsua4heeQhjwvaDE+DQogICAgICAgICAgICA8cCBjbGFzc05hbWU9J3N1Yic+6L2v5Lu26L+e57ut5rKh5pyJ5LiK5Lyg5oiW5LiL6L295pe277yM6Ieq5Yqo57uT5p2f5ZCO5Y+w6L+b56iL44CC5Y+v5re75Yqg5Lu75oSP6L2v5Lu277yM5bm25Y2V54us6K6+572u6L+b56iL5LiO56m66Zey6KeE5YiZ44CCPC9wPg0KICAgICAgICAgIDwvZGl2Pg0KICAgICAgICAgIDxkaXYgY2xhc3NOYW1lPXttb25pdG9yLnJ1bm5pbmcgPyAncGlsbCBwaWxsLW9uJyA6ICdwaWxsJ30+DQogICAgICAgICAgICA8c3BhbiBjbGFzc05hbWU9e21vbml0b3IucnVubmluZyA/ICdkb3QgZG90LW9uJyA6ICdkb3QnfT48L3NwYW4+DQogICAgICAgICAgICB7bW9uaXRvci5ydW5uaW5nID8gJ+ebkeaOp+i/kOihjOS4rScgOiAn55uR5o6n5pyq6L+Q6KGMJ30NCiAgICAgICAgICA8L2Rpdj4NCiAgICAgICAgPC9oZWFkZXI+DQoNCiAgICAgICAgPGRpdiBjbGFzc05hbWU9J3Rvb2xiYXInPg0KICAgICAgICAgIDxkaXYgY2xhc3NOYW1lPSdzZWFyY2gnPg0KICAgICAgICAgICAgPHN2ZyBjbGFzc05hbWU9J3NlYXJjaC1pY29uJyB2aWV3Qm94PScwIDAgMjAgMjAnIGFyaWEtaGlkZGVuPSd0cnVlJz4NCiAgICAgICAgICAgICAgPGNpcmNsZSBjeD0nOC42JyBjeT0nOC42JyByPSc1LjQnIGZpbGw9J25vbmUnIHN0cm9rZT0nY3VycmVudENvbG9yJyBzdHJva2VXaWR0aD0nMS44JyAvPg0KICAgICAgICAgICAgICA8cGF0aCBkPSdNMTIuNyAxMi43IEwxNi42IDE2LjYnIGZpbGw9J25vbmUnIHN0cm9rZT0nY3VycmVudENvbG9yJyBzdHJva2VXaWR0aD0nMS44JyBzdHJva2VMaW5lY2FwPSdyb3VuZCcgLz4NCiAgICAgICAgICAgIDwvc3ZnPg0KICAgICAgICAgICAgPGlucHV0IHZhbHVlPXtmaWx0ZXJ9IG9uQ2hhbmdlPXtmdW5jdGlvbiAoZSkgeyBzZXRGaWx0ZXIoZS50YXJnZXQudmFsdWUpOyB9fSBwbGFjZWhvbGRlcj0n5pCc57Si6L2v5Lu244CB6L+b56iL5oiW6L+Q6KGM5Lit55qE56iL5bqP4oCmJyAvPg0KICAgICAgICAgIDwvZGl2Pg0KICAgICAgICAgIDxidXR0b24gY2xhc3NOYW1lPSdxay11aS1idG4gYnRuIGJ0bi1hZGQnIG9uQ2xpY2s9e2Z1bmN0aW9uICgpIHsgYWRkQ2FyZCgnJywgJycpOyB9fT48c3BhbiBjbGFzc05hbWU9J3BsdXMnPis8L3NwYW4+5omL5Yqo5re75YqgPC9idXR0b24+DQogICAgICAgIDwvZGl2Pg0KDQogICAgICAgIDxkaXYgY2xhc3NOYW1lPSdwaWNrZXInPg0KICAgICAgICAgIDxkaXYgY2xhc3NOYW1lPSdwaWNrZXItbGFiZWwnPjxzcGFuIGNsYXNzTmFtZT0nbGl2ZSc+PC9zcGFuPuato+WcqOi/kOihjDwvZGl2Pg0KICAgICAgICAgIDxkaXYgY2xhc3NOYW1lPSdjaGlwcyc+DQogICAgICAgICAgICB7cGlja2FibGUubWFwKGZ1bmN0aW9uIChyLCBpKSB7DQogICAgICAgICAgICAgIHJldHVybiAoDQogICAgICAgICAgICAgICAgPGJ1dHRvbiBrZXk9e2l9IGNsYXNzTmFtZT0ncWstdWktYnRuIGNoaXAnIHRpdGxlPXtyLnRpdGxlIHx8IHIubmFtZX0gb25DbGljaz17ZnVuY3Rpb24gKCkgeyBhZGRDYXJkKGNsZWFuTmFtZShyLm5hbWUpLCBjbGVhbk5hbWUoci5uYW1lKSk7IH19Pg0KICAgICAgICAgICAgICAgICAgPHNwYW4gY2xhc3NOYW1lPSdjcGx1cyc+Kzwvc3Bhbj57Y2xlYW5OYW1lKHIubmFtZSl9DQogICAgICAgICAgICAgICAgPC9idXR0b24+DQogICAgICAgICAgICAgICk7DQogICAgICAgICAgICB9KX0NCiAgICAgICAgICAgIHtwaWNrYWJsZS5sZW5ndGggPT09IDAgPyA8c3BhbiBjbGFzc05hbWU9J3BpY2tlci1lbXB0eSc+5rKh5pyJ5Y+v5Yqg5YWl55qE6L+Q6KGM5Lit6L2v5Lu277yM6K+V6K+V5LiK5pa55pCc57Si5oiW5omL" +
        "5Yqo5re75Yqg44CCPC9zcGFuPiA6IG51bGx9DQogICAgICAgICAgPC9kaXY+DQogICAgICAgIDwvZGl2Pg0KDQogICAgICAgIDxkaXYgY2xhc3NOYW1lPSdsaXN0LXBhbmVsJz4NCiAgICAgICAgICA8ZGl2IGNsYXNzTmFtZT0nbGlzdC1oZWFkJz4NCiAgICAgICAgICAgIDxkaXYgY2xhc3NOYW1lPSdsaXN0LXRpdGxlJz7ova/ku7bliJfooaggPHNwYW4gY2xhc3NOYW1lPSdjb3VudCc+e2FwcHMubGVuZ3RofTwvc3Bhbj48L2Rpdj4NCiAgICAgICAgICAgIDxkaXYgY2xhc3NOYW1lPSdsaXN0LW1ldGEnPntxID8gJ+aJvuWIsCAnICsgdmlzaWJsZS5sZW5ndGggKyAnIOS4quWMuemFjemhuScgOiBlbmFibGVkQ291bnQgKyAnIOS4quWQr+eUqCDCtyAnICsgcnVubmluZ0NvdW50ICsgJyDkuKrov5DooYzkuK0nfTwvZGl2Pg0KICAgICAgICAgIDwvZGl2Pg0KICAgICAgICAgIDxkaXYgY2xhc3NOYW1lPSdsaXN0LWJvZHknPg0KICAgICAgICAgICAge3Zpc2libGUubWFwKGZ1bmN0aW9uIChpdGVtKSB7DQogICAgICAgICAgICAgIGNvbnN0IGEgPSBpdGVtLmFwcDsNCiAgICAgICAgICAgICAgY29uc3QgaSA9IGl0ZW0uaW5kZXg7DQogICAgICAgICAgICAgIGNvbnN0IG9uID0gYXBwUnVubmluZyhhKTsNCiAgICAgICAgICAgICAgY29uc3QgZW5hYmxlZCA9IGEuZW5hYmxlZCAhPT0gZmFsc2U7DQogICAgICAgICAgICAgIGNvbnN0IGlzRWRpdGluZyA9IGVkaXRpbmcgPT09IGk7DQogICAgICAgICAgICAgIHJldHVybiAoDQogICAgICAgICAgICAgICAgPGRpdiBjbGFzc05hbWU9e2lzRWRpdGluZyA/ICdyb3ctd3JhcCBvcGVuJyA6ICdyb3ctd3JhcCd9IGtleT17aX0+DQogICAgICAgICAgICAgICAgICA8ZGl2IGNsYXNzTmFtZT0ncm93Jz4NCiAgICAgICAgICAgICAgICAgICAgPGRpdiBjbGFzc05hbWU9eydhdmF0YXIgdGludC0nICsgdGludEluZGV4KGEubmFtZSl9Pg0KICAgICAgICAgICAgICAgICAgICAgIHtpbml0aWFscyhhLm5hbWUpfQ0KICAgICAgICAgICAgICAgICAgICAgIDxzcGFuIGNsYXNzTmFtZT17ZW5hYmxlZCAmJiBvbiA/ICdiYWRnZSBsaXZlJyA6ICdiYWRnZSd9Pjwvc3Bhbj4NCiAgICAgICAgICAgICAgICAgICAgPC9kaXY+DQogICAgICAgICAgICAgICAgICAgIDxkaXYgY2xhc3NOYW1lPSdyb3ctbWFpbicgb25DbGljaz17ZnVuY3Rpb24gKCkgeyBzZXRFZGl0aW5nKGlzRWRpdGluZyA/IC0xIDogaSk7IHNldEVycm9yKCcnKTsgfX0+DQogICAgICAgICAgICAgICAgICAgICAgPGRpdiBjbGFzc05hbWU9J3Jvdy10aXRsZSc+DQogICAgICAgICAgICAgICAgICAgICAgICA8c3BhbiBjbGFzc05hbWU9J25hbWUnPnthLm5hbWUgfHwgJ+acquWRveWQjei9r+S7tid9PC9zcGFuPg0KICAgICAgICAgICAgICAgICAgICAgICAgPHNwYW4gY2xhc3NOYW1lPXshZW5hYmxlZCA/ICdzdGF0ZSBvZmYnIDogKG9uID8gJ3N0YXRlIG9uJyA6ICdzdGF0ZScpfT57IWVuYWJsZWQgPyAn5bey5YGc55SoJyA6IChvbiA/ICfov5DooYzkuK0nIDogJ+acqui/kOihjCcpfTwvc3Bhbj4NCiAgICAgICAgICAgICAgICAgICAgICA8L2Rpdj4NCiAgICAgICAgICAgICAgICAgICAgICA8ZGl2IGNsYXNzTmFtZT0ncm93LXN1Yic+55uR5o6n6L+b56iLIMK3IHtwcm9jU3VtbWFyeShzcGxpdE5hbWVzKGEucHJvY2Vzc2VzKS5tYXAoY2xlYW5OYW1lKSl9PC9kaXY+DQogICAgICAgICAgICAgICAgICAgICAgPGRpdiBjbGFzc05hbWU9J3RhZ3MnPg0KICAgICAgICAgICAgICAgICAgICAgICAgPHNwYW4gY2xhc3NOYW1lPSd0YWcgdGFnLWFjY2VudCc+56m66ZeyIHtmb3JtYXROdW1iZXIoYS5pZGxlTWludXRlcyl9IOWIhumSnzwvc3Bhbj4NCiAgICAgICAgICAgICAgICAgICAgICAgIDxzcGFuIGNsYXNzTmFtZT0ndGFnJz7kvKDovpMge3RyYW5zZmVyU3VtbWFyeShhKX08L3NwYW4+DQogICAgICAgICAgICAgICAgICAgICAgICB7U3RyaW5nKGEudHJhbnNmZXJDb21tYW5kTGluZSB8fCAnJykudHJpbSgpID8gPHNwYW4gY2xhc3NOYW1lPSd0YWcnPuetm+mAiSB7YnJpZWYoYS50cmFuc2ZlckNvbW1hbmRMaW5lLCAxOCl9PC9zcGFuPiA6IG51bGx9DQogICAgICAgICAgICAgICAgICAgICAgICA8c3BhbiBjbGFzc05hbWU9J3RhZyB0YWctZGltJz7pmIjlgLwge2Zvcm1hdE51bWJlcihhLnRocmVzaG9sZEtCKX0gS0I8L3NwYW4+DQogICAgICAgICAgICAgICAgICAgICAgPC9kaXY+DQogICAgICAgICAgICAgICAgICAgIDwvZGl2Pg0KICAgICAgICAgICAgICAgICAgICA8ZGl2IGNsYXNzTmFtZT0ncm93LWFjdGlvbnMnPg0KICAgICAgICAgICAgICAgICAgICAgIDxsYWJlbCBjbGFzc05hbWU9J3N3aXRjaCcgdGl0bGU9e2VuYWJsZWQgPyAn5bey5ZCv55SoJyA6ICflt7LlgZznlKgnfT4NCiAgICAgICAgICAgICAgICAgICAgICAgIDxpbnB1dCB0eXBlPSdjaGVja2JveCcgY2hlY2tlZD17ZW5hYmxlZH0gb25DaGFuZ2U9e2Z1bmN0aW9uIChlKSB7IHBhdGNoKGksICdlbmFibGVkJywgZS50YXJnZXQuY2hlY2tlZCk7IH19IC8+DQogICAgICAgICAgICAgICAgICAgICAgICA8c3BhbiBjbGFzc05hbWU9J3NsaWRlcic+PC9zcGFuPg0KICAgICAgICAgICAgICAgICAgICAgIDwvbGFiZWw+DQogICAgICAgICAgICAgICAgICAgICAgPGRpdiBjbGFzc05hbWU9J3Jvdy1idG5zJz4NCiAgICAgICAgICAgICAgICAgICAgICAgIDxidXR0b24gY2xhc3NOYW1lPSdxay11aS1idG4gYnRuIGJ0bi1naG9zdCcgb25DbGljaz17ZnVuY3Rpb24gKCkgeyBzZXRFZGl0aW5nKGlzRWRpdGluZyA/IC0xIDogaSk7IHNldEVycm9yKCcnKTsgfX0+e2lzRWRpdGluZyA/ICfmlLbotbcnIDogJ+e8lui+kSd9PC9idXR0b24+DQogICAgICAgICAgICAgICAgICAgICAgICA8YnV0dG9uIGNsYXNzTmFtZT0ncWstdWktYnRuIGJ0biBidG4tZ2hvc3QgZGFuZ2VyJyBvbkNsaWNrPXtmdW5jdGlvbiAoKSB7IHJlbW92ZUFwcChpKTsgfX0+5Yig6ZmkPC9idXR0b24+DQogICAgICAgICAgICAgICAgICAgICAgPC9kaXY+DQogICAgICAg" +
        "ICAgICAgICAgICAgIDwvZGl2Pg0KICAgICAgICAgICAgICAgICAgPC9kaXY+DQogICAgICAgICAgICAgICAgICB7aXNFZGl0aW5nID8gKA0KICAgICAgICAgICAgICAgICAgICA8ZGl2IGNsYXNzTmFtZT0nZWRpdG9yJz4NCiAgICAgICAgICAgICAgICAgICAgICA8ZGl2IGNsYXNzTmFtZT0nZWRpdG9yLWJveCc+DQogICAgICAgICAgICAgICAgICAgICAgICA8ZGl2IGNsYXNzTmFtZT0nZWRpdG9yLWdyaWQnPg0KICAgICAgICAgICAgICAgICAgICAgICAgICA8ZGl2IGNsYXNzTmFtZT0nZmllbGQgc3BhbjInPg0KICAgICAgICAgICAgICAgICAgICAgICAgICAgIDxsYWJlbD7ova/ku7blkI3np7A8L2xhYmVsPg0KICAgICAgICAgICAgICAgICAgICAgICAgICAgIDxpbnB1dCB2YWx1ZT17YS5uYW1lfSBvbkNoYW5nZT17ZnVuY3Rpb24gKGUpIHsgcGF0Y2goaSwgJ25hbWUnLCBlLnRhcmdldC52YWx1ZSk7IH19IHBsYWNlaG9sZGVyPSfkvovlpoLvvJrnmb7luqbnvZHnm5gnIC8+DQogICAgICAgICAgICAgICAgICAgICAgICAgIDwvZGl2Pg0KICAgICAgICAgICAgICAgICAgICAgICAgICA8ZGl2IGNsYXNzTmFtZT0nZmllbGQnPg0KICAgICAgICAgICAgICAgICAgICAgICAgICAgIDxsYWJlbD7nqbrpl7LliIbpkp88L2xhYmVsPg0KICAgICAgICAgICAgICAgICAgICAgICAgICAgIDxpbnB1dCB0eXBlPSdudW1iZXInIG1pbj0nMC4xJyBtYXg9JzE0NDAnIHN0ZXA9JzAuNScgdmFsdWU9e2EuaWRsZU1pbnV0ZXN9IG9uQ2hhbmdlPXtmdW5jdGlvbiAoZSkgeyBwYXRjaChpLCAnaWRsZU1pbnV0ZXMnLCBlLnRhcmdldC52YWx1ZSk7IH19IC8+DQogICAgICAgICAgICAgICAgICAgICAgICAgIDwvZGl2Pg0KICAgICAgICAgICAgICAgICAgICAgICAgICA8ZGl2IGNsYXNzTmFtZT0nZmllbGQnPg0KICAgICAgICAgICAgICAgICAgICAgICAgICAgIDxsYWJlbD7ph4fmoLfpmIjlgLwgS0I8L2xhYmVsPg0KICAgICAgICAgICAgICAgICAgICAgICAgICAgIDxpbnB1dCB0eXBlPSdudW1iZXInIG1pbj0nMScgbWF4PScxMDQ4NTc2JyBzdGVwPScxJyB2YWx1ZT17YS50aHJlc2hvbGRLQn0gb25DaGFuZ2U9e2Z1bmN0aW9uIChlKSB7IHBhdGNoKGksICd0aHJlc2hvbGRLQicsIGUudGFyZ2V0LnZhbHVlKTsgfX0gLz4NCiAgICAgICAgICAgICAgICAgICAgICAgICAgPC9kaXY+DQogICAgICAgICAgICAgICAgICAgICAgICAgIDxkaXYgY2xhc3NOYW1lPSdmaWVsZCBzcGFuMic+DQogICAgICAgICAgICAgICAgICAgICAgICAgICAgPGxhYmVsPuebkeaOp+i/m+eoizxzcGFuIGNsYXNzTmFtZT0nb3B0Jz7liIblj7fliIbpmpQgwrcg5Yik5pat5piv5ZCm5omT5byA5bm257uT5p2f5ZCO5Y+wPC9zcGFuPjwvbGFiZWw+DQogICAgICAgICAgICAgICAgICAgICAgICAgICAgPGlucHV0IHZhbHVlPXthLnByb2Nlc3Nlc30gb25DaGFuZ2U9e2Z1bmN0aW9uIChlKSB7IHBhdGNoKGksICdwcm9jZXNzZXMnLCBlLnRhcmdldC52YWx1ZSk7IH19IHBsYWNlaG9sZGVyPSfkvovlpoLvvJpCYWlkdU5ldGRpc2s7IGJhaWR1bmV0ZGlza2hvc3QnIC8+DQogICAgICAgICAgICAgICAgICAgICAgICAgIDwvZGl2Pg0KICAgICAgICAgICAgICAgICAgICAgICAgICA8ZGl2IGNsYXNzTmFtZT0nZmllbGQgc3BhbjInPg0KICAgICAgICAgICAgICAgICAgICAgICAgICAgIDxsYWJlbD7kvKDovpPov5vnqIs8c3BhbiBjbGFzc05hbWU9J29wdCc+5YiG5Y+35YiG6ZqUIMK3IOeVmeepuuihqOekuuWFqOmDqDwvc3Bhbj48L2xhYmVsPg0KICAgICAgICAgICAgICAgICAgICAgICAgICAgIDxpbnB1dCB2YWx1ZT17YS50cmFuc2ZlclByb2Nlc3Nlc30gb25DaGFuZ2U9e2Z1bmN0aW9uIChlKSB7IHBhdGNoKGksICd0cmFuc2ZlclByb2Nlc3NlcycsIGUudGFyZ2V0LnZhbHVlKTsgfX0gcGxhY2Vob2xkZXI9J+eVmeepuuWNs+WPr++8m+eZvuW6pue9keebmOWhqyBiYWlkdW5ldGRpc2tob3N0JyAvPg0KICAgICAgICAgICAgICAgICAgICAgICAgICA8L2Rpdj4NCiAgICAgICAgICAgICAgICAgICAgICAgICAgPGRpdiBjbGFzc05hbWU9J2ZpZWxkIHNwYW4yJz4NCiAgICAgICAgICAgICAgICAgICAgICAgICAgICA8bGFiZWw+5ZG95Luk6KGM562b6YCJPHNwYW4gY2xhc3NOYW1lPSdvcHQnPuWPr+mAiTwvc3Bhbj48L2xhYmVsPg0KICAgICAgICAgICAgICAgICAgICAgICAgICAgIDxpbnB1dCB2YWx1ZT17YS50cmFuc2ZlckNvbW1hbmRMaW5lfSBvbkNoYW5nZT17ZnVuY3Rpb24gKGUpIHsgcGF0Y2goaSwgJ3RyYW5zZmVyQ29tbWFuZExpbmUnLCBlLnRhcmdldC52YWx1ZSk7IH19IHBsYWNlaG9sZGVyPSflj6rnu5/orqHlkb3ku6TooYzljIXlkKvmraTmloflrZfnmoTkvKDovpPov5vnqIsnIC8+DQogICAgICAgICAgICAgICAgICAgICAgICAgIDwvZGl2Pg0KICAgICAgICAgICAgICAgICAgICAgICAgICA8ZGl2IGNsYXNzTmFtZT0nZWRpdG9yLW5vdGUnPuW9k+WJjei/m+eoi3tvbiA/ICfmraPlnKjov5DooYwnIDogJ+acqui/kOihjCd944CC57uT5p2f5pe25Lya6L+e5ZCM5a2Q6L+b56iL5LiA5bm25YWz6Zet77yM6K+35YWI5L+d5a2Y5omL5aS05bel5L2c44CCPC9kaXY+DQogICAgICAgICAgICAgICAgICAgICAgICA8L2Rpdj4NCiAgICAgICAgICAgICAgICAgICAgICA8L2Rpdj4NCiAgICAgICAgICAgICAgICAgICAgPC9kaXY+DQogICAgICAgICAgICAgICAgICApIDogbnVsbH0NCiAgICAgICAgICAgICAgICA8L2Rpdj4NCiAgICAgICAgICAgICAgKTsNCiAgICAgICAgICAgIH0pfQ0KICAgICAgICAgICAge3Zpc2libGUubGVuZ3RoID09PSAwID8gKA0KICAgICAgICAgICAgICA8ZGl2IGNsYXNzTmFtZT0nZW1wdHknPg0KICAgICAgICAgICAgICAgIDxkaXYgY2xhc3NOYW1lPSdlbXB0eS1pY29uJz4NCiAgICAgICAgICAgICAgICAgIDxzdmcgdmlld0JveD0nMCAwIDI0IDI0JyB3aWR0aD0nMjInIGhlaWdodD0nMjInIGZpbGw9J25vbmUnIHN0cm9rZT0nY3VycmVudENvbG9yJyBzdHJva2VXaWR0aD0nMS43JyBzdHJva2VMaW5l" +
        "Y2FwPSdyb3VuZCcgc3Ryb2tlTGluZWpvaW49J3JvdW5kJz4NCiAgICAgICAgICAgICAgICAgICAgPHJlY3QgeD0nNCcgeT0nNScgd2lkdGg9JzE2JyBoZWlnaHQ9JzE0JyByeD0nMyc+PC9yZWN0Pg0KICAgICAgICAgICAgICAgICAgICA8cGF0aCBkPSdNOCAxMGg4TTggMTRoNSc+PC9wYXRoPg0KICAgICAgICAgICAgICAgICAgPC9zdmc+DQogICAgICAgICAgICAgICAgPC9kaXY+DQogICAgICAgICAgICAgICAgPGRpdiBjbGFzc05hbWU9J2VtcHR5LXRpdGxlJz57cSA/ICfmsqHmnInljLnphY3nmoTova/ku7YnIDogJ+i/mOayoeaciea3u+WKoOi9r+S7tid9PC9kaXY+DQogICAgICAgICAgICAgICAgPGRpdiBjbGFzc05hbWU9J2VtcHR5LXN1Yic+e3EgPyAn5o2i5Liq5YWz6ZSu6K+N77yM5oiW5riF56m65pCc57Si5p+l55yL5YWo6YOo44CCJyA6ICfku47kuIrmlrnov5DooYzkuK3nmoTnqIvluo/ngrnpgInvvIzmiJbmiYvliqjmt7vliqDjgIInfTwvZGl2Pg0KICAgICAgICAgICAgICA8L2Rpdj4NCiAgICAgICAgICAgICkgOiBudWxsfQ0KICAgICAgICAgIDwvZGl2Pg0KICAgICAgICAgIDxkaXYgY2xhc3NOYW1lPSdsaXN0LWZvb3QnPg0KICAgICAgICAgICAgPHNwYW4+57uT5p2f5pe25Lya5YWz6Zet5omA6YCJ6L2v5Lu255qE5ZCO5Y+w6L+b56iL5LiO5a2Q6L+b56iLPC9zcGFuPg0KICAgICAgICAgICAgPHNwYW4gY2xhc3NOYW1lPSdmb290LXJpZ2h0Jz7kuIrkvKDkuI7kuIvovb3pg73kvJrpobrlu7bnqbrpl7Lml7bpl7Q8L3NwYW4+DQogICAgICAgICAgPC9kaXY+DQogICAgICAgIDwvZGl2Pg0KDQogICAgICAgIDxmb290ZXIgY2xhc3NOYW1lPSdmb290Jz4NCiAgICAgICAgICA8ZGl2IGNsYXNzTmFtZT17ZXJyb3IgPyAnZXJyb3InIDogJ2hpbnQnfT57ZXJyb3IgfHwgKG1vbml0b3IucnVubmluZyA/ICfkv53lrZjlkI7nq4vljbPnlJ/mlYjvvIzml6DpnIDph43lkK/jgIInIDogJ+S/neWtmOWQjuS8muWwneivleWQr+WKqOWQjuWPsOebkeaOp+OAgicpfTwvZGl2Pg0KICAgICAgICAgIDxkaXYgY2xhc3NOYW1lPSdmb290LWJ0bnMnPg0KICAgICAgICAgICAgPGJ1dHRvbiBjbGFzc05hbWU9J3FrLXVpLWJ0biBidG4nIG9uQ2xpY2s9e2Z1bmN0aW9uICgpIHsgUXVpY2tlci5jYW5jZWwoKTsgfX0+5Y+W5raIPC9idXR0b24+DQogICAgICAgICAgICA8YnV0dG9uIGNsYXNzTmFtZT0ncWstdWktYnRuIGJ0biBidG4tcHJpbWFyeScgb25DbGljaz17c2F2ZX0+5L+d5a2Y5bm25YWz6ZetPC9idXR0b24+DQogICAgICAgICAgPC9kaXY+DQogICAgICAgIDwvZm9vdGVyPg0KICAgICAgPC9kaXY+DQogICAgPC9tYWluPg0KICApOw0KfQ0K";
}

string WatchdogBase64()
{
    return         "77u/IyB0cmFuc2Zlci1pZGxlLXdhdGNoZG9nIHYxLjAuMQpwYXJhbSgKICAgIFtQYXJhbWV0ZXIoTWFuZGF0b3J5ID0gJHRydWUpXVtzdHJpbmddJERhdGFEaXIsCiAgICBbUGFyYW1ldGVyKE1hbmRhdG9yeSA9ICR0cnVlKV1bc3RyaW5nXSRUb2tlbiwKICAgIFtQYXJhbWV0ZXIoTWFuZGF0b3J5ID0gJHRydWUpXVtzdHJpbmddJFZlcnNpb24KKQoKJEVycm9yQWN0aW9uUHJlZmVyZW5jZSA9ICdTdG9wJwokc2NyaXB0OlN0YXJ0ZWRBdCA9IChHZXQtRGF0ZSkuVG9TdHJpbmcoJ28nKQokY29uZmlnUGF0aCA9IEpvaW4tUGF0aCAkRGF0YURpciAnYXBwcy5qc29uJwokc3RhdHVzUGF0aCA9IEpvaW4tUGF0aCAkRGF0YURpciAnc3RhdHVzLmpzb24nCiRsb2dQYXRoID0gSm9pbi1QYXRoICREYXRhRGlyICd3YXRjaGRvZy5sb2cnCiRzdG9wUGF0aCA9IEpvaW4tUGF0aCAkRGF0YURpciAnc3RvcC5mbGFnJwokc2NyaXB0Okxhc3RFdmVudCA9ICflt7LlkK/liqgnCgokUHJvdGVjdGVkTmFtZXMgPSBbU3lzdGVtLkNvbGxlY3Rpb25zLkdlbmVyaWMuSGFzaFNldFtzdHJpbmddXTo6bmV3KFtTeXN0ZW0uU3RyaW5nQ29tcGFyZXJdOjpPcmRpbmFsSWdub3JlQ2FzZSkKZm9yZWFjaCAoJG5hbWUgaW4gQCgKICAgICAgICAnc3lzdGVtJywgJ2lkbGUnLCAncmVnaXN0cnknLCAnbWVtb3J5IGNvbXByZXNzaW9uJywgJ3NlY3VyZSBzeXN0ZW0nLAogICAgICAgICdzbXNzJywgJ2NzcnNzJywgJ3dpbmluaXQnLCAnd2lubG9nb24nLCAnc2VydmljZXMnLCAnbHNhc3MnLCAnc3ZjaG9zdCcsCiAgICAgICAgJ2R3bScsICdmb250ZHJ2aG9zdCcsICdzaWhvc3QnLCAndGFza2hvc3R3JywgJ2N0Zm1vbicsICdleHBsb3JlcicsCiAgICAgICAgJ2Nvbmhvc3QnLCAncG93ZXJzaGVsbCcsICdwd3NoJywgJ2NtZCcsICd3c2NyaXB0JywgJ2NzY3JpcHQnLCAnbXNodGEnLAogICAgICAgICdydW5kbGwzMicsICdyZWdzdnIzMicsICdzcG9vbHN2JywgJ2F1ZGlvZGcnLCAnc2VhcmNoaW5kZXhlcicsCiAgICAgICAgJ3d1YXVjbHQnLCAndHJ1c3RlZGluc3RhbGxlcicsICdzZWFyY2hob3N0JywgJ3N0YXJ0bWVudWV4cGVyaWVuY2Vob3N0JywKICAgICAgICAnc2hlbGxleHBlcmllbmNlaG9zdCcsICd0ZXh0aW5wdXRob3N0JywgJ2FwcGxpY2F0aW9uZnJhbWVob3N0JywgJ2xvY2thcHAnLAogICAgICAgICdzbWFydHNjcmVlbicsICdzZWN1cml0eWhlYWx0aHNlcnZpY2UnLCAnbXNtcGVuZycsICduaXNzcnYnLAogICAgICAgICdiYWNrZ3JvdW5kdGFza2hvc3QnLCAnc3lzdGVtc2V0dGluZ3MnLCAnc2VhcmNoYXBwJywgJ3J1bnRpbWVicm9rZXInLAogICAgICAgICdkbGxob3N0JywgJ3Rhc2tob3N0JywgJ3NlYXJjaHByb3RvY29saG9zdCcsICdzZWFyY2hmaWx0ZXJob3N0JywKICAgICAgICAnd2lkZ2V0c2VydmljZScKICAgICkpIHsKICAgIFt2b2lkXSRQcm90ZWN0ZWROYW1lcy5BZGQoJG5hbWUpCn0KCmZ1bmN0aW9uIFdyaXRlLUxvZyhbc3RyaW5nXSRtZXNzYWdlKSB7CiAgICB0cnkgewogICAgICAgICRsaW5lID0gKEdldC1EYXRlKS5Ub1N0cmluZygneXl5eS1NTS1kZCBISDptbTpzcycpICsgJyAgJyArICRtZXNzYWdlCiAgICAgICAgQWRkLUNvbnRlbnQgLUxpdGVyYWxQYXRoICRsb2dQYXRoIC1WYWx1ZSAkbGluZSAtRW5jb2RpbmcgVVRGOAogICAgfSBjYXRjaCB7CiAgICB9Cn0KCmZ1bmN0aW9uIFdyaXRlLVN0YXR1cygkYXBwcywgW2ludF0kc2FtcGxlU2Vjb25kcykgewogICAgdHJ5IHsKICAgICAgICAkb2JqID0gW3BzY3VzdG9tb2JqZWN0XUB7CiAgICAgICAgICAgIHZlcnNpb24gICAgICAgPSAkVmVyc2lvbgogICAgICAgICAgICBwaWQgICAgICAgICAgID0gJFBJRAogICAgICAgICAgICB0b2tlbiAgICAgICAgID0gJFRva2VuCiAgICAgICAgICAgIHN0YXJ0ZWRBdCAgICAgPSAkc2NyaXB0OlN0YXJ0ZWRBdAogICAgICAgICAgICB1cGRhdGVkQXQgICAgID0gKEdldC1EYXRlKS5Ub1N0cmluZygnbycpCiAgICAgICAgICAgIHNhbXBsZVNlY29uZHMgPSAkc2FtcGxlU2Vjb25kcwogICAgICAgICAgICBhcHBzICAgICAgICAgID0gQCgkYXBwcykKICAgICAgICAgICAgbGFzdEV2ZW50ICAgICA9ICRzY3JpcHQ6TGFzdEV2ZW50CiAgICAgICAgfQogICAgICAgICRqc29uID0gJG9iaiB8IENvbnZlcnRUby1Kc29uIC1EZXB0aCA2CiAgICAgICAgU2V0LUNvbnRlbnQgLUxpdGVyYWxQYXRoICRzdGF0dXNQYXRoIC1WYWx1ZSAkanNvbiAtRW5jb2RpbmcgVVRGOAogICAgfSBjYXRjaCB7CiAgICAgICAgV3JpdGUtTG9nICgn5YaZ5YWl54q25oCB5aSx6LSlOiAnICsgJF8uRXhjZXB0aW9uLk1lc3NhZ2UpCiAgICB9Cn0KCmZ1bmN0aW9uIFNob3ctQmFsbG9vbihbc3RyaW5nXSR0aXRsZSwgW3N0cmluZ10kdGV4dCkgewogICAgdHJ5IHsKICAgICAgICBBZGQtVHlwZSAtQXNzZW1ibHlOYW1lIFN5c3RlbS5XaW5kb3dzLkZvcm1zCiAgICAgICAgQWRkLVR5cGUgLUFzc2VtYmx5TmFtZSBTeXN0ZW0uRHJhd2luZwogICAgICAgICRpY29uID0gTmV3LU9iamVjdCBTeXN0ZW0uV2luZG93cy5Gb3Jtcy5Ob3RpZnlJY29uCiAgICAgICAgJGljb24uSWNvbiA9IFtTeXN0ZW0uRHJhd2luZy5TeXN0ZW1JY29uc106OkluZm9ybWF0aW9uCiAgICAgICAgJGljb24uVmlzaWJsZSA9ICR0cnVlCiAgICAgICAgJGljb24uQmFsbG9vblRpcFRpdGxlID0gJHRpdGxlCiAgICAgICAgJGljb24uQmFsbG9vblRpcFRleHQgPSAkdGV4dAogICAgICAgICRpY29uLkJhbGxvb25UaXBJY29uID0gW1N5c3RlbS5XaW5kb3dzLkZvcm1zLlRvb2xUaXBJY29uXTo6SW5mbwogICAgICAgICRpY29uLlNob3dCYWxsb29uVGlwKDYwMDApCiAgICAgICAgU3RhcnQtU2xlZXAgLVNlY29uZHMgNwogICAgICAgICRpY29uLkRpc3Bvc2UoKQogICAgfSBjYXRjaCB7CiAgICAgICAgV3JpdGUtTG9nICgn6YCa55+l5aSx6LSlOiAnICsgJF8uRXhjZXB0aW9uLk1lc3NhZ2UpCiAgICB9Cn0KCmZ1bmN0aW9uIENvbnZlcnRUby1Qcm9jTmFtZShbc3RyaW5nXSRuYW1l" +
        "KSB7CiAgICBpZiAoW3N0cmluZ106OklzTnVsbE9yV2hpdGVTcGFjZSgkbmFtZSkpIHsgcmV0dXJuICcnIH0KICAgICRuYW1lID0gJG5hbWUuVHJpbSgpCiAgICBpZiAoJG5hbWUuRW5kc1dpdGgoJy5leGUnLCBbU3lzdGVtLlN0cmluZ0NvbXBhcmlzb25dOjpPcmRpbmFsSWdub3JlQ2FzZSkpIHsKICAgICAgICAkbmFtZSA9ICRuYW1lLlN1YnN0cmluZygwLCAkbmFtZS5MZW5ndGggLSA0KQogICAgfQogICAgcmV0dXJuICRuYW1lCn0KCmZ1bmN0aW9uIFJlYWQtV2F0Y2hDb25maWcgewogICAgJHJhdyA9IEdldC1Db250ZW50IC1MaXRlcmFsUGF0aCAkY29uZmlnUGF0aCAtUmF3IC1FbmNvZGluZyBVVEY4CiAgICAkY2ZnID0gJHJhdyB8IENvbnZlcnRGcm9tLUpzb24KICAgICRub3RpZmljYXRpb25zID0gJHRydWUKICAgIGlmICgkbnVsbCAtbmUgJGNmZy5ub3RpZmljYXRpb25zKSB7ICRub3RpZmljYXRpb25zID0gW2Jvb2xdJGNmZy5ub3RpZmljYXRpb25zIH0KICAgICRzYW1wbGVTZWNvbmRzID0gMTAKICAgIHRyeSB7CiAgICAgICAgaWYgKCRudWxsIC1uZSAkY2ZnLnNhbXBsZVNlY29uZHMpIHsgJHNhbXBsZVNlY29uZHMgPSBbaW50XSRjZmcuc2FtcGxlU2Vjb25kcyB9CiAgICB9IGNhdGNoIHsKICAgICAgICAkc2FtcGxlU2Vjb25kcyA9IDEwCiAgICB9CiAgICBpZiAoJHNhbXBsZVNlY29uZHMgLWx0IDUpIHsgJHNhbXBsZVNlY29uZHMgPSA1IH0KICAgIGlmICgkc2FtcGxlU2Vjb25kcyAtZ3QgMTIwKSB7ICRzYW1wbGVTZWNvbmRzID0gMTIwIH0KCiAgICAkYXBwcyA9IEAoKQogICAgZm9yZWFjaCAoJGEgaW4gQCgkY2ZnLmFwcHMpKSB7CiAgICAgICAgaWYgKCRudWxsIC1lcSAkYSkgeyBjb250aW51ZSB9CiAgICAgICAgaWYgKCRhLmVuYWJsZWQgLWVxICRmYWxzZSkgeyBjb250aW51ZSB9CgogICAgICAgICRwcm9jTmFtZXMgPSBAKCkKICAgICAgICBmb3JlYWNoICgkbiBpbiBAKCRhLnByb2Nlc3NlcykpIHsKICAgICAgICAgICAgJG4gPSBDb252ZXJ0VG8tUHJvY05hbWUgKFtzdHJpbmddJG4pCiAgICAgICAgICAgIGlmICgkbikgeyAkcHJvY05hbWVzICs9ICRuIH0KICAgICAgICB9CiAgICAgICAgaWYgKCRwcm9jTmFtZXMuQ291bnQgLWVxIDApIHsgY29udGludWUgfQoKICAgICAgICAkdHJhbnNmZXJOYW1lcyA9IEAoKQogICAgICAgIGZvcmVhY2ggKCRuIGluIEAoJGEudHJhbnNmZXJQcm9jZXNzZXMpKSB7CiAgICAgICAgICAgICRuID0gQ29udmVydFRvLVByb2NOYW1lIChbc3RyaW5nXSRuKQogICAgICAgICAgICBpZiAoJG4pIHsgJHRyYW5zZmVyTmFtZXMgKz0gJG4gfQogICAgICAgIH0KICAgICAgICBpZiAoJHRyYW5zZmVyTmFtZXMuQ291bnQgLWVxIDApIHsgJHRyYW5zZmVyTmFtZXMgPSAkcHJvY05hbWVzIH0KCiAgICAgICAgJGlkbGVNaW51dGVzID0gNS4wCiAgICAgICAgdHJ5IHsKICAgICAgICAgICAgaWYgKCRudWxsIC1uZSAkYS5pZGxlTWludXRlcykgeyAkaWRsZU1pbnV0ZXMgPSBbZG91YmxlXSRhLmlkbGVNaW51dGVzIH0KICAgICAgICB9IGNhdGNoIHsKICAgICAgICAgICAgJGlkbGVNaW51dGVzID0gNS4wCiAgICAgICAgfQogICAgICAgIGlmICgkaWRsZU1pbnV0ZXMgLWx0IDAuMSkgeyAkaWRsZU1pbnV0ZXMgPSAwLjEgfQogICAgICAgIGlmICgkaWRsZU1pbnV0ZXMgLWd0IDE0NDApIHsgJGlkbGVNaW51dGVzID0gMTQ0MCB9CgogICAgICAgICR0aHJlc2hvbGRLQiA9IDMyCiAgICAgICAgdHJ5IHsKICAgICAgICAgICAgaWYgKCRudWxsIC1uZSAkYS50aHJlc2hvbGRLQikgeyAkdGhyZXNob2xkS0IgPSBbaW50XSRhLnRocmVzaG9sZEtCIH0KICAgICAgICB9IGNhdGNoIHsKICAgICAgICAgICAgJHRocmVzaG9sZEtCID0gMzIKICAgICAgICB9CiAgICAgICAgaWYgKCR0aHJlc2hvbGRLQiAtbHQgMSkgeyAkdGhyZXNob2xkS0IgPSAxIH0KICAgICAgICBpZiAoJHRocmVzaG9sZEtCIC1ndCAxMDQ4NTc2KSB7ICR0aHJlc2hvbGRLQiA9IDEwNDg1NzYgfQoKICAgICAgICAkYXBwcyArPSBbcHNjdXN0b21vYmplY3RdQHsKICAgICAgICAgICAgbmFtZSAgICAgICAgICAgICAgICA9IFtzdHJpbmddJGEubmFtZQogICAgICAgICAgICBwcm9jZXNzZXMgICAgICAgICAgID0gQCgkcHJvY05hbWVzKQogICAgICAgICAgICB0cmFuc2ZlclByb2Nlc3NlcyAgID0gQCgkdHJhbnNmZXJOYW1lcykKICAgICAgICAgICAgdHJhbnNmZXJDb21tYW5kTGluZSA9IFtzdHJpbmddJGEudHJhbnNmZXJDb21tYW5kTGluZQogICAgICAgICAgICBpZGxlU2Vjb25kcyAgICAgICAgID0gW2ludF1bbWF0aF06OlJvdW5kKCRpZGxlTWludXRlcyAqIDYwKQogICAgICAgICAgICBpZGxlTWludXRlcyAgICAgICAgID0gJGlkbGVNaW51dGVzCiAgICAgICAgICAgIHRocmVzaG9sZEJ5dGVzICAgICAgPSBbbG9uZ10kdGhyZXNob2xkS0IgKiAxMDI0CiAgICAgICAgfQogICAgfQoKICAgIHJldHVybiBbcHNjdXN0b21vYmplY3RdQHsKICAgICAgICBzYW1wbGVTZWNvbmRzID0gJHNhbXBsZVNlY29uZHMKICAgICAgICBub3RpZmljYXRpb25zID0gJG5vdGlmaWNhdGlvbnMKICAgICAgICBhcHBzICAgICAgICAgID0gQCgkYXBwcykKICAgIH0KfQoKZnVuY3Rpb24gR2V0LU5hbWVNYXAoJGFsbCkgewogICAgJG1hcCA9IEB7fQogICAgZm9yZWFjaCAoJHAgaW4gJGFsbCkgewogICAgICAgICRuID0gQ29udmVydFRvLVByb2NOYW1lIChbc3RyaW5nXSRwLk5hbWUpCiAgICAgICAgaWYgKC1ub3QgJG4pIHsgY29udGludWUgfQogICAgICAgIGlmICgtbm90ICRtYXAuQ29udGFpbnNLZXkoJG4pKSB7CiAgICAgICAgICAgICRtYXBbJG5dID0gTmV3LU9iamVjdCBTeXN0ZW0uQ29sbGVjdGlvbnMuQXJyYXlMaXN0CiAgICAgICAgfQogICAgICAgIFt2b2lkXSRtYXBbJG5dLkFkZCgkcCkKICAgIH0KICAgIHJldHVybiAkbWFwCn0KCmZ1bmN0aW9uIEdldC1BcHBQcm9jZXNzZXMoJGFwcCwgJG5hbWVNYXApIHsKICAgICRzZXQgPSBAe30KICAgIGZvcmVhY2ggKCRuIGluIEAoJGFwcC5wcm9jZXNzZXMpKSB7" +
        "CiAgICAgICAgaWYgKC1ub3QgJG5hbWVNYXAuQ29udGFpbnNLZXkoJG4pKSB7IGNvbnRpbnVlIH0KICAgICAgICBmb3JlYWNoICgkcCBpbiBAKCRuYW1lTWFwWyRuXSkpIHsKICAgICAgICAgICAgJHNldFtbaW50XSRwLlByb2Nlc3NJZF0gPSAkcAogICAgICAgIH0KICAgIH0KICAgIHJldHVybiBAKCRzZXQuVmFsdWVzKQp9CgpmdW5jdGlvbiBHZXQtVHJhbnNmZXJCeXRlcygkYXBwLCAkbmFtZU1hcCkgewogICAgJHNldCA9IEB7fQogICAgZm9yZWFjaCAoJG4gaW4gQCgkYXBwLnRyYW5zZmVyUHJvY2Vzc2VzKSkgewogICAgICAgIGlmICgtbm90ICRuYW1lTWFwLkNvbnRhaW5zS2V5KCRuKSkgeyBjb250aW51ZSB9CiAgICAgICAgZm9yZWFjaCAoJHAgaW4gQCgkbmFtZU1hcFskbl0pKSB7CiAgICAgICAgICAgICRjbWQgPSBbc3RyaW5nXSRwLkNvbW1hbmRMaW5lCiAgICAgICAgICAgIGlmICgkYXBwLnRyYW5zZmVyQ29tbWFuZExpbmUgLWFuZCAkY21kLkluZGV4T2YoW3N0cmluZ10kYXBwLnRyYW5zZmVyQ29tbWFuZExpbmUsIFtTeXN0ZW0uU3RyaW5nQ29tcGFyaXNvbl06Ok9yZGluYWxJZ25vcmVDYXNlKSAtbHQgMCkgewogICAgICAgICAgICAgICAgY29udGludWUKICAgICAgICAgICAgfQogICAgICAgICAgICAkc2V0W1tpbnRdJHAuUHJvY2Vzc0lkXSA9ICRwCiAgICAgICAgfQogICAgfQogICAgW2xvbmddJHN1bSA9IDAKICAgIGZvcmVhY2ggKCRwIGluICRzZXQuVmFsdWVzKSB7CiAgICAgICAgJHN1bSArPSBbbG9uZ10kcC5SZWFkVHJhbnNmZXJDb3VudAogICAgICAgICRzdW0gKz0gW2xvbmddJHAuV3JpdGVUcmFuc2ZlckNvdW50CiAgICB9CiAgICByZXR1cm4gJHN1bQp9CgpmdW5jdGlvbiBUZXN0LUtpbGxBbGxvd2VkKCRwcm9jLCBbaW50XSRzZWxmSWQsIFtzdHJpbmddJHdpbmRvd3NQcmVmaXgpIHsKICAgICRwcm9jSWQgPSBbaW50XSRwcm9jLlByb2Nlc3NJZAogICAgaWYgKCRwcm9jSWQgLWxlIDQgLW9yICRwcm9jSWQgLWVxICRzZWxmSWQpIHsgcmV0dXJuICRmYWxzZSB9CiAgICAkbmFtZSA9IENvbnZlcnRUby1Qcm9jTmFtZSAoW3N0cmluZ10kcHJvYy5OYW1lKQogICAgaWYgKC1ub3QgJG5hbWUpIHsgcmV0dXJuICRmYWxzZSB9CiAgICBpZiAoJFByb3RlY3RlZE5hbWVzLkNvbnRhaW5zKCRuYW1lKSkgeyByZXR1cm4gJGZhbHNlIH0KICAgIGlmICgkbmFtZS5TdGFydHNXaXRoKCdxdWlja2VyJywgW1N5c3RlbS5TdHJpbmdDb21wYXJpc29uXTo6T3JkaW5hbElnbm9yZUNhc2UpKSB7IHJldHVybiAkZmFsc2UgfQogICAgJHBhdGggPSBbc3RyaW5nXSRwcm9jLkV4ZWN1dGFibGVQYXRoCiAgICBpZiAoLW5vdCBbc3RyaW5nXTo6SXNOdWxsT3JXaGl0ZVNwYWNlKCRwYXRoKSkgewogICAgICAgIHRyeSB7CiAgICAgICAgICAgICRmdWxsID0gW1N5c3RlbS5JTy5QYXRoXTo6R2V0RnVsbFBhdGgoJHBhdGgpCiAgICAgICAgICAgIGlmICgkZnVsbC5TdGFydHNXaXRoKCR3aW5kb3dzUHJlZml4LCBbU3lzdGVtLlN0cmluZ0NvbXBhcmlzb25dOjpPcmRpbmFsSWdub3JlQ2FzZSkpIHsgcmV0dXJuICRmYWxzZSB9CiAgICAgICAgfSBjYXRjaCB7CiAgICAgICAgICAgIHJldHVybiAkZmFsc2UKICAgICAgICB9CiAgICB9CiAgICByZXR1cm4gJHRydWUKfQoKZnVuY3Rpb24gS2lsbC1BcHBQcm9jZXNzZXMoJGFwcCwgW2ludF0kc2VsZklkLCBbc3RyaW5nXSR3aW5kb3dzUHJlZml4LCAkYWxsKSB7CiAgICAkbmFtZVNldCA9IFtTeXN0ZW0uQ29sbGVjdGlvbnMuR2VuZXJpYy5IYXNoU2V0W3N0cmluZ11dOjpuZXcoW1N5c3RlbS5TdHJpbmdDb21wYXJlcl06Ok9yZGluYWxJZ25vcmVDYXNlKQogICAgZm9yZWFjaCAoJG4gaW4gQCgkYXBwLnByb2Nlc3NlcykpIHsgW3ZvaWRdJG5hbWVTZXQuQWRkKChDb252ZXJ0VG8tUHJvY05hbWUgJG4pKSB9CgogICAgJHRhcmdldCA9IEB7fQogICAgZm9yZWFjaCAoJHAgaW4gJGFsbCkgewogICAgICAgICRuID0gQ29udmVydFRvLVByb2NOYW1lIChbc3RyaW5nXSRwLk5hbWUpCiAgICAgICAgaWYgKCRuYW1lU2V0LkNvbnRhaW5zKCRuKSkgeyAkdGFyZ2V0W1tpbnRdJHAuUHJvY2Vzc0lkXSA9ICR0cnVlIH0KICAgIH0KICAgIGlmICgkdGFyZ2V0LkNvdW50IC1lcSAwKSB7IHJldHVybiBbcHNjdXN0b21vYmplY3RdQHsgS2lsbGVkID0gMDsgUmVtYWluaW5nID0gMDsgTmFtZXMgPSBAKCkgfSB9CgogICAgJGNoYW5nZWQgPSAkdHJ1ZQogICAgJGd1YXJkID0gMAogICAgd2hpbGUgKCRjaGFuZ2VkIC1hbmQgJGd1YXJkIC1sdCAxMikgewogICAgICAgICRndWFyZCsrCiAgICAgICAgJGNoYW5nZWQgPSAkZmFsc2UKICAgICAgICBmb3JlYWNoICgkcCBpbiAkYWxsKSB7CiAgICAgICAgICAgICRwYXJlbnQgPSBbaW50XSRwLlBhcmVudFByb2Nlc3NJZAogICAgICAgICAgICAkcHJvY0lkID0gW2ludF0kcC5Qcm9jZXNzSWQKICAgICAgICAgICAgaWYgKCR0YXJnZXQuQ29udGFpbnNLZXkoJHBhcmVudCkgLWFuZCAtbm90ICR0YXJnZXQuQ29udGFpbnNLZXkoJHByb2NJZCkpIHsKICAgICAgICAgICAgICAgICR0YXJnZXRbJHByb2NJZF0gPSAkdHJ1ZQogICAgICAgICAgICAgICAgJGNoYW5nZWQgPSAkdHJ1ZQogICAgICAgICAgICB9CiAgICAgICAgfQogICAgfQoKICAgICRieUlkID0gQHt9CiAgICBmb3JlYWNoICgkcCBpbiAkYWxsKSB7ICRieUlkW1tpbnRdJHAuUHJvY2Vzc0lkXSA9ICRwIH0KCiAgICAkcm9vdHMgPSBAKCkKICAgICRjaGlsZHJlbiA9IEAoKQogICAgJHNraXBwZWQgPSBAKCkKICAgIGZvcmVhY2ggKCRwcm9jSWQgaW4gQCgkdGFyZ2V0LktleXMpKSB7CiAgICAgICAgaWYgKC1ub3QgJGJ5SWQuQ29udGFpbnNLZXkoJHByb2NJZCkpIHsgY29udGludWUgfQogICAgICAgICRwID0gJGJ5SWRbJHByb2NJZF0KICAgICAgICBpZiAoLW5vdCAoVGVzdC1LaWxsQWxsb3dlZCAkcCAkc2VsZklkICR3aW5kb3dzUHJlZml4KSkgewogICAgICAgICAgICAkc2tpcHBlZCArPSAoQ29udmVydFRvLVByb2NOYW1lIChbc3RyaW5nXSRwLk5hbWUpKQogICAgICAg" +
        "ICAgICBjb250aW51ZQogICAgICAgIH0KICAgICAgICBpZiAoJHRhcmdldC5Db250YWluc0tleShbaW50XSRwLlBhcmVudFByb2Nlc3NJZCkpIHsgJGNoaWxkcmVuICs9ICRwIH0gZWxzZSB7ICRyb290cyArPSAkcCB9CiAgICB9CgogICAgJGtpbGxlZCA9IDAKICAgIGZvcmVhY2ggKCRwIGluIEAoJHJvb3RzICsgJGNoaWxkcmVuKSkgewogICAgICAgIHRyeSB7CiAgICAgICAgICAgIFN0b3AtUHJvY2VzcyAtSWQgKFtpbnRdJHAuUHJvY2Vzc0lkKSAtRm9yY2UgLUVycm9yQWN0aW9uIFN0b3AKICAgICAgICAgICAgJGtpbGxlZCsrCiAgICAgICAgfSBjYXRjaCB7CiAgICAgICAgICAgIFdyaXRlLUxvZyAoJ+e7k+adn+i/m+eoi+Wksei0pSAnICsgJHAuTmFtZSArICcgKCcgKyAkcC5Qcm9jZXNzSWQgKyAnKTogJyArICRfLkV4Y2VwdGlvbi5NZXNzYWdlKQogICAgICAgIH0KICAgIH0KCiAgICBTdGFydC1TbGVlcCAtTWlsbGlzZWNvbmRzIDEyMDAKCiAgICB0cnkgewogICAgICAgICRhZnRlciA9IEAoR2V0LUNpbUluc3RhbmNlIC1DbGFzc05hbWUgV2luMzJfUHJvY2VzcyAtRXJyb3JBY3Rpb24gU3RvcCkKICAgIH0gY2F0Y2ggewogICAgICAgICRhZnRlciA9IEAoKQogICAgfQogICAgJHJlbWFpbmluZ1RhcmdldHMgPSBAe30KICAgIGZvcmVhY2ggKCRwIGluICRhZnRlcikgewogICAgICAgICRuID0gQ29udmVydFRvLVByb2NOYW1lIChbc3RyaW5nXSRwLk5hbWUpCiAgICAgICAgaWYgKCRuYW1lU2V0LkNvbnRhaW5zKCRuKSkgeyAkcmVtYWluaW5nVGFyZ2V0c1tbaW50XSRwLlByb2Nlc3NJZF0gPSAkdHJ1ZSB9CiAgICB9CiAgICAkY2hhbmdlZCA9ICR0cnVlCiAgICAkZ3VhcmQgPSAwCiAgICB3aGlsZSAoJGNoYW5nZWQgLWFuZCAkZ3VhcmQgLWx0IDEyKSB7CiAgICAgICAgJGd1YXJkKysKICAgICAgICAkY2hhbmdlZCA9ICRmYWxzZQogICAgICAgIGZvcmVhY2ggKCRwIGluICRhZnRlcikgewogICAgICAgICAgICAkcGFyZW50ID0gW2ludF0kcC5QYXJlbnRQcm9jZXNzSWQKICAgICAgICAgICAgJHByb2NJZCA9IFtpbnRdJHAuUHJvY2Vzc0lkCiAgICAgICAgICAgIGlmICgkcmVtYWluaW5nVGFyZ2V0cy5Db250YWluc0tleSgkcGFyZW50KSAtYW5kIC1ub3QgJHJlbWFpbmluZ1RhcmdldHMuQ29udGFpbnNLZXkoJHByb2NJZCkpIHsKICAgICAgICAgICAgICAgICRyZW1haW5pbmdUYXJnZXRzWyRwcm9jSWRdID0gJHRydWUKICAgICAgICAgICAgICAgICRjaGFuZ2VkID0gJHRydWUKICAgICAgICAgICAgfQogICAgICAgIH0KICAgIH0KCiAgICAkcmVtYWluaW5nID0gMAogICAgZm9yZWFjaCAoJHAgaW4gJGFmdGVyKSB7CiAgICAgICAgaWYgKC1ub3QgJHJlbWFpbmluZ1RhcmdldHMuQ29udGFpbnNLZXkoW2ludF0kcC5Qcm9jZXNzSWQpKSB7IGNvbnRpbnVlIH0KICAgICAgICBpZiAoLW5vdCAoVGVzdC1LaWxsQWxsb3dlZCAkcCAkc2VsZklkICR3aW5kb3dzUHJlZml4KSkgeyBjb250aW51ZSB9CiAgICAgICAgJHJlbWFpbmluZysrCiAgICAgICAgdHJ5IHsKICAgICAgICAgICAgU3RvcC1Qcm9jZXNzIC1JZCAoW2ludF0kcC5Qcm9jZXNzSWQpIC1Gb3JjZSAtRXJyb3JBY3Rpb24gU3RvcAogICAgICAgICAgICAka2lsbGVkKysKICAgICAgICB9IGNhdGNoIHsKICAgICAgICB9CiAgICB9CgogICAgU3RhcnQtU2xlZXAgLU1pbGxpc2Vjb25kcyA1MDAKICAgIHRyeSB7CiAgICAgICAgJGZpbmFsID0gQChHZXQtQ2ltSW5zdGFuY2UgLUNsYXNzTmFtZSBXaW4zMl9Qcm9jZXNzIC1FcnJvckFjdGlvbiBTdG9wKQogICAgfSBjYXRjaCB7CiAgICAgICAgJGZpbmFsID0gQCgpCiAgICB9CiAgICAkbGVmdCA9IDAKICAgICRsZWZ0TmFtZXMgPSBAKCkKICAgIGZvcmVhY2ggKCRwIGluICRmaW5hbCkgewogICAgICAgICRuID0gQ29udmVydFRvLVByb2NOYW1lIChbc3RyaW5nXSRwLk5hbWUpCiAgICAgICAgaWYgKCRuYW1lU2V0LkNvbnRhaW5zKCRuKSkgewogICAgICAgICAgICAkbGVmdCsrCiAgICAgICAgICAgICRsZWZ0TmFtZXMgKz0gJG4KICAgICAgICB9CiAgICB9CgogICAgcmV0dXJuIFtwc2N1c3RvbW9iamVjdF1AewogICAgICAgIEtpbGxlZCAgICA9ICRraWxsZWQKICAgICAgICBSZW1haW5pbmcgPSAkbGVmdAogICAgICAgIE5hbWVzICAgICA9IEAoJGxlZnROYW1lcyB8IFNvcnQtT2JqZWN0IC1VbmlxdWUpCiAgICAgICAgU2tpcHBlZCAgID0gQCgkc2tpcHBlZCB8IFNvcnQtT2JqZWN0IC1VbmlxdWUpCiAgICB9Cn0KCmlmIChUZXN0LVBhdGggLUxpdGVyYWxQYXRoICRsb2dQYXRoKSB7CiAgICB0cnkgewogICAgICAgIGlmICgoR2V0LUl0ZW0gLUxpdGVyYWxQYXRoICRsb2dQYXRoKS5MZW5ndGggLWd0IDEwNDg1NzYpIHsKICAgICAgICAgICAgTW92ZS1JdGVtIC1MaXRlcmFsUGF0aCAkbG9nUGF0aCAtRGVzdGluYXRpb24gKCRsb2dQYXRoICsgJy4xJykgLUZvcmNlCiAgICAgICAgfQogICAgfSBjYXRjaCB7CiAgICB9Cn0KCiR3aW5kb3dzUHJlZml4ID0gW1N5c3RlbS5JTy5QYXRoXTo6R2V0RnVsbFBhdGgoJGVudjpXSU5ESVIpLlRyaW1FbmQoJ1wnKSArICdcJwpXcml0ZS1Mb2cgKCfnm5HmjqflkK/liqggcGlkPScgKyAkUElEICsgJyB2ZXJzaW9uPScgKyAkVmVyc2lvbikKCiMg5Y+q5L+d55WZ5LiA5Liq55uR5o6n5a6e5L6L77ya5ZCM5pe25ZCv5Yqo5pe25L+d55WZIFBJRCDovoPlsI/nmoTpgqPkuKrjgIIKU3RhcnQtU2xlZXAgLVNlY29uZHMgMQp0cnkgewogICAgJHNlbGZQYXRoID0gW3N0cmluZ10kTXlJbnZvY2F0aW9uLk15Q29tbWFuZC5QYXRoCiAgICBpZiAoJHNlbGZQYXRoKSB7CiAgICAgICAgJG90aGVycyA9IEAoR2V0LUNpbUluc3RhbmNlIC1DbGFzc05hbWUgV2luMzJfUHJvY2VzcyAtRmlsdGVyICJOYW1lPSdwb3dlcnNoZWxsLmV4ZSciIC1FcnJvckFjdGlvbiBTdG9wIHwKICAgICAgICAgICAgV2hlcmUtT2JqZWN0IHsKICAgICAgICAgICAgICAgICRfLlByb2Nlc3NJZCAtbmUgJFBJRCAtYW5kICRfLkNvbW1hbmRMaW5lIC1hbmQKICAgICAgICAgICAgICAgICRf" +
        "LkNvbW1hbmRMaW5lLkluZGV4T2YoJHNlbGZQYXRoLCBbU3lzdGVtLlN0cmluZ0NvbXBhcmlzb25dOjpPcmRpbmFsSWdub3JlQ2FzZSkgLWdlIDAKICAgICAgICAgICAgfSB8CiAgICAgICAgICAgIEZvckVhY2gtT2JqZWN0IHsgW2ludF0kXy5Qcm9jZXNzSWQgfSkKICAgICAgICBpZiAoJG90aGVycy5Db3VudCAtZ3QgMCkgewogICAgICAgICAgICAkbG93ZXN0ID0gKCRvdGhlcnMgfCBNZWFzdXJlLU9iamVjdCAtTWluaW11bSkuTWluaW11bQogICAgICAgICAgICBpZiAoJGxvd2VzdCAtbHQgJFBJRCkgewogICAgICAgICAgICAgICAgV3JpdGUtTG9nICgn5bey5pyJ55uR5o6n5a6e5L6LICcgKyAkbG93ZXN0ICsgJ++8jOacrOWunuS+i+mAgOWHuicpCiAgICAgICAgICAgICAgICBleGl0CiAgICAgICAgICAgIH0KICAgICAgICB9CiAgICB9Cn0gY2F0Y2ggewogICAgV3JpdGUtTG9nICgn5qOA5p+l6YeN5aSN5a6e5L6L5aSx6LSlOiAnICsgJF8uRXhjZXB0aW9uLk1lc3NhZ2UpCn0KClJlbW92ZS1JdGVtIC1MaXRlcmFsUGF0aCAkc3RvcFBhdGggLUZvcmNlIC1FcnJvckFjdGlvbiBTaWxlbnRseUNvbnRpbnVlCgokc3RhdGUgPSBAe30KJGxhc3RHb29kQ29uZmlnID0gJG51bGwKJGNvbmZpZ0Vycm9yTG9nZ2VkID0gJycKJGN1cnJlbnRTYW1wbGVTZWNvbmRzID0gMTAKJGxhc3RTYW1wbGVBdCA9IChHZXQtRGF0ZSkuQWRkU2Vjb25kcygtMTApCiRsYXN0UXVpY2tlckNoZWNrID0gR2V0LURhdGUKJHF1aWNrZXJNaXNzZXMgPSAwCgp3aGlsZSAoJHRydWUpIHsKICAgIGlmIChUZXN0LVBhdGggLUxpdGVyYWxQYXRoICRzdG9wUGF0aCkgewogICAgICAgICRzY3JpcHQ6TGFzdEV2ZW50ID0gJ+aUtuWIsOWBnOatouivt+axgicKICAgICAgICBXcml0ZS1Mb2cgJ+aUtuWIsOWBnOatouivt+axgu+8jOmAgOWHuuebkeaOpycKICAgICAgICBicmVhawogICAgfQoKICAgICRub3cgPSBHZXQtRGF0ZQogICAgaWYgKCgkbm93IC0gJGxhc3RRdWlja2VyQ2hlY2spLlRvdGFsU2Vjb25kcyAtZ2UgNSkgewogICAgICAgICRsYXN0UXVpY2tlckNoZWNrID0gJG5vdwogICAgICAgICRxdWlja2VyID0gQChHZXQtUHJvY2VzcyAtTmFtZSBRdWlja2VyIC1FcnJvckFjdGlvbiBTaWxlbnRseUNvbnRpbnVlKQogICAgICAgIGlmICgkcXVpY2tlci5Db3VudCAtZ3QgMCkgewogICAgICAgICAgICAkcXVpY2tlck1pc3NlcyA9IDAKICAgICAgICB9IGVsc2UgewogICAgICAgICAgICAkcXVpY2tlck1pc3NlcysrCiAgICAgICAgICAgIGlmICgkcXVpY2tlck1pc3NlcyAtZ2UgMikgewogICAgICAgICAgICAgICAgJHNjcmlwdDpMYXN0RXZlbnQgPSAnUXVpY2tlciDlt7LpgIDlh7onCiAgICAgICAgICAgICAgICBXcml0ZS1Mb2cgJ1F1aWNrZXIg5bey6YCA5Ye677yM57uT5p2f55uR5o6nJwogICAgICAgICAgICAgICAgYnJlYWsKICAgICAgICAgICAgfQogICAgICAgIH0KICAgIH0KCiAgICBpZiAoKCRub3cgLSAkbGFzdFNhbXBsZUF0KS5Ub3RhbFNlY29uZHMgLWx0ICRjdXJyZW50U2FtcGxlU2Vjb25kcykgewogICAgICAgIFN0YXJ0LVNsZWVwIC1NaWxsaXNlY29uZHMgMzAwCiAgICAgICAgY29udGludWUKICAgIH0KCiAgICAkY29uZmlnID0gJG51bGwKICAgIHRyeSB7CiAgICAgICAgJGNvbmZpZyA9IFJlYWQtV2F0Y2hDb25maWcKICAgICAgICAkbGFzdEdvb2RDb25maWcgPSAkY29uZmlnCiAgICAgICAgJGNvbmZpZ0Vycm9yTG9nZ2VkID0gJycKICAgIH0gY2F0Y2ggewogICAgICAgICRtc2cgPSAkXy5FeGNlcHRpb24uTWVzc2FnZQogICAgICAgIGlmICgkbXNnIC1uZSAkY29uZmlnRXJyb3JMb2dnZWQpIHsKICAgICAgICAgICAgV3JpdGUtTG9nICgn6K+75Y+W6YWN572u5aSx6LSl77yM5rK/55So5LiK5qyh6YWN572uOiAnICsgJG1zZykKICAgICAgICAgICAgJGNvbmZpZ0Vycm9yTG9nZ2VkID0gJG1zZwogICAgICAgIH0KICAgICAgICAkY29uZmlnID0gJGxhc3RHb29kQ29uZmlnCiAgICB9CgogICAgaWYgKCRudWxsIC1lcSAkY29uZmlnKSB7CiAgICAgICAgU3RhcnQtU2xlZXAgLVNlY29uZHMgMgogICAgICAgIGNvbnRpbnVlCiAgICB9CgogICAgJGdhcCA9ICgkbm93IC0gJGxhc3RTYW1wbGVBdCkuVG90YWxTZWNvbmRzCiAgICAkbGFzdFNhbXBsZUF0ID0gJG5vdwogICAgJGN1cnJlbnRTYW1wbGVTZWNvbmRzID0gJGNvbmZpZy5zYW1wbGVTZWNvbmRzCiAgICBpZiAoJGdhcCAtZ3QgKFttYXRoXTo6TWF4KDMwLCAkY29uZmlnLnNhbXBsZVNlY29uZHMgKiAzKSkpIHsKICAgICAgICBXcml0ZS1Mb2cgKCfmo4DmtYvliLDns7vnu5/kvJHnnKAv5Y2h6aG/ICcgKyBbaW50XSRnYXAgKyAnIOenku+8jOmHjee9ruepuumXsuiuoeaXticpCiAgICAgICAgZm9yZWFjaCAoJGtleSBpbiBAKCRzdGF0ZS5LZXlzKSkgewogICAgICAgICAgICAkc3RhdGVbJGtleV0ubGFzdEFjdGl2ZSA9ICRudWxsCiAgICAgICAgfQogICAgICAgIFN0YXJ0LVNsZWVwIC1TZWNvbmRzIDEKICAgICAgICBjb250aW51ZQogICAgfQoKICAgICRhbGwgPSBAKCkKICAgIHRyeSB7CiAgICAgICAgJGFsbCA9IEAoR2V0LUNpbUluc3RhbmNlIC1DbGFzc05hbWUgV2luMzJfUHJvY2VzcyAtRXJyb3JBY3Rpb24gU3RvcCkKICAgIH0gY2F0Y2ggewogICAgICAgIFdyaXRlLUxvZyAoJ+ivu+WPlui/m+eoi+Wksei0pTogJyArICRfLkV4Y2VwdGlvbi5NZXNzYWdlKQogICAgICAgICRzY3JpcHQ6TGFzdEV2ZW50ID0gJ+ivu+WPlui/m+eoi+Wksei0pScKICAgICAgICBTdGFydC1TbGVlcCAtU2Vjb25kcyAyCiAgICAgICAgY29udGludWUKICAgIH0KICAgICRuYW1lTWFwID0gR2V0LU5hbWVNYXAgJGFsbAoKICAgICRhcHBTdGF0dXNlcyA9IEAoKQogICAgZm9yZWFjaCAoJGFwcCBpbiBAKCRjb25maWcuYXBwcykpIHsKICAgICAgICBpZiAoJG51bGwgLWVxICRhcHApIHsgY29udGludWUgfQogICAgICAgIGlmICgtbm90ICRzdGF0ZS5Db250YWluc0tleSgkYXBwLm5hbWUpKSB7CiAgICAgICAgICAgICRzdGF0ZVskYXBwLm5hbWVdID0gW3BzY3Vz" +
        "dG9tb2JqZWN0XUB7CiAgICAgICAgICAgICAgICBsYXN0QWN0aXZlID0gJG51bGwKICAgICAgICAgICAgICAgIHByZXZCeXRlcyAgPSBbbG9uZ10wCiAgICAgICAgICAgICAgICBwcmltYXJ5U2lnID0gJycKICAgICAgICAgICAgICAgIHdhc1J1bm5pbmcgPSAkZmFsc2UKICAgICAgICAgICAgICAgIGxhc3RLaWxsICAgPSBbZGF0ZXRpbWVdOjpNaW5WYWx1ZQogICAgICAgICAgICAgICAga2lsbHMgICAgICA9IDAKICAgICAgICAgICAgfQogICAgICAgIH0KICAgICAgICAkc3QgPSAkc3RhdGVbJGFwcC5uYW1lXQogICAgICAgICRhcHBQcm9jcyA9IEAoR2V0LUFwcFByb2Nlc3NlcyAkYXBwICRuYW1lTWFwKQogICAgICAgICRydW5uaW5nID0gKCRhcHBQcm9jcy5Db3VudCAtZ3QgMCkKICAgICAgICAkYnl0ZXMgPSBbbG9uZ10wCiAgICAgICAgaWYgKCRydW5uaW5nKSB7ICRieXRlcyA9IEdldC1UcmFuc2ZlckJ5dGVzICRhcHAgJG5hbWVNYXAgfQoKICAgICAgICAkaWRsZVNlY29uZHMgPSAwCiAgICAgICAgaWYgKC1ub3QgJHJ1bm5pbmcpIHsKICAgICAgICAgICAgJHN0Lmxhc3RBY3RpdmUgPSAkbnVsbAogICAgICAgICAgICAkc3QucHJldkJ5dGVzID0gW2xvbmddMAogICAgICAgICAgICAkc3QucHJpbWFyeVNpZyA9ICcnCiAgICAgICAgICAgICRzdC53YXNSdW5uaW5nID0gJGZhbHNlCiAgICAgICAgfSBlbHNlIHsKICAgICAgICAgICAgJHByaW1hcnlOYW1lID0gQ29udmVydFRvLVByb2NOYW1lIChbc3RyaW5nXUAoJGFwcC5wcm9jZXNzZXMpWzBdKQogICAgICAgICAgICAkcHJpbWFyeUlkcyA9IEAoJGFwcFByb2NzIHwgV2hlcmUtT2JqZWN0IHsgKENvbnZlcnRUby1Qcm9jTmFtZSAoW3N0cmluZ10kXy5OYW1lKSkgLWVxICRwcmltYXJ5TmFtZSB9IHwgRm9yRWFjaC1PYmplY3QgeyBbc3RyaW5nXSRfLlByb2Nlc3NJZCB9IHwgU29ydC1PYmplY3QpCiAgICAgICAgICAgICRwcmltYXJ5U2lnID0gKCRwcmltYXJ5SWRzIC1qb2luICcsJykKICAgICAgICAgICAgJHdhc1J1bm5pbmcgPSBbYm9vbF0kc3Qud2FzUnVubmluZwogICAgICAgICAgICAkc3Qud2FzUnVubmluZyA9ICR0cnVlCiAgICAgICAgICAgIGlmICgkbnVsbCAtZXEgJHN0Lmxhc3RBY3RpdmUgLW9yIC1ub3QgJHdhc1J1bm5pbmcgLW9yICRwcmltYXJ5U2lnIC1uZSAkc3QucHJpbWFyeVNpZykgewogICAgICAgICAgICAgICAgJHN0Lmxhc3RBY3RpdmUgPSAkbm93CiAgICAgICAgICAgICAgICAkc3QucHJldkJ5dGVzID0gJGJ5dGVzCiAgICAgICAgICAgICAgICAkc3QucHJpbWFyeVNpZyA9ICRwcmltYXJ5U2lnCiAgICAgICAgICAgIH0gZWxzZSB7CiAgICAgICAgICAgICAgICAkZGVsdGEgPSAkYnl0ZXMgLSAkc3QucHJldkJ5dGVzCiAgICAgICAgICAgICAgICBpZiAoJGJ5dGVzIC1sdCAkc3QucHJldkJ5dGVzKSB7CiAgICAgICAgICAgICAgICAgICAgJHN0Lmxhc3RBY3RpdmUgPSAkbm93CiAgICAgICAgICAgICAgICB9IGVsc2VpZiAoJGRlbHRhIC1nZSAkYXBwLnRocmVzaG9sZEJ5dGVzKSB7CiAgICAgICAgICAgICAgICAgICAgJHN0Lmxhc3RBY3RpdmUgPSAkbm93CiAgICAgICAgICAgICAgICB9CiAgICAgICAgICAgICAgICAkc3QucHJldkJ5dGVzID0gJGJ5dGVzCiAgICAgICAgICAgICAgICAkaWRsZVNlY29uZHMgPSBbaW50XSgkbm93IC0gJHN0Lmxhc3RBY3RpdmUpLlRvdGFsU2Vjb25kcwogICAgICAgICAgICB9CgogICAgICAgICAgICBpZiAoJGlkbGVTZWNvbmRzIC1nZSAkYXBwLmlkbGVTZWNvbmRzKSB7CiAgICAgICAgICAgICAgICAkc2NyaXB0Okxhc3RFdmVudCA9ICgkYXBwLm5hbWUgKyAnIOepuumXsua4heeQhicpCiAgICAgICAgICAgICAgICBXcml0ZS1Mb2cgKCRhcHAubmFtZSArICcg5bey6L+e57utICcgKyAkaWRsZVNlY29uZHMgKyAnIOenkuaXoOS8oOi+k++8iOmYiOWAvCAnICsgJGFwcC5pZGxlU2Vjb25kcyArICcg56eS77yJ77yM5byA5aeL57uT5p2f5ZCO5Y+wJykKICAgICAgICAgICAgICAgICRyZXN1bHQgPSBLaWxsLUFwcFByb2Nlc3NlcyAkYXBwICRQSUQgJHdpbmRvd3NQcmVmaXggJGFsbAogICAgICAgICAgICAgICAgaWYgKFtpbnRdJHJlc3VsdC5LaWxsZWQgLWd0IDApIHsKICAgICAgICAgICAgICAgICAgICAkc3Qua2lsbHMgPSBbaW50XSRzdC5raWxscyArIDEKICAgICAgICAgICAgICAgIH0KICAgICAgICAgICAgICAgICRzdC5sYXN0S2lsbCA9ICRub3cKICAgICAgICAgICAgICAgICRzdC5sYXN0QWN0aXZlID0gJG5vdwogICAgICAgICAgICAgICAgJHN0LnByZXZCeXRlcyA9IFtsb25nXTAKICAgICAgICAgICAgICAgICRzdC5wcmltYXJ5U2lnID0gJycKICAgICAgICAgICAgICAgICRzdC53YXNSdW5uaW5nID0gJGZhbHNlCiAgICAgICAgICAgICAgICBpZiAoJHJlc3VsdC5Ta2lwcGVkLkNvdW50IC1ndCAwKSB7CiAgICAgICAgICAgICAgICAgICAgV3JpdGUtTG9nICgn5Y+X5L+d5oqkL+aXoOazleivu+WPluiAjOi3s+i/hzogJyArICgkcmVzdWx0LlNraXBwZWQgLWpvaW4gJywgJykpCiAgICAgICAgICAgICAgICB9CiAgICAgICAgICAgICAgICBXcml0ZS1Mb2cgKCRhcHAubmFtZSArICcg5riF55CG5a6M5oiQ77ya57uT5p2fICcgKyAkcmVzdWx0LktpbGxlZCArICcg5Liq6L+b56iL77yM5Ymp5L2ZICcgKyAkcmVzdWx0LlJlbWFpbmluZykKICAgICAgICAgICAgICAgIGlmICgkY29uZmlnLm5vdGlmaWNhdGlvbnMgLW5lICRmYWxzZSkgewogICAgICAgICAgICAgICAgICAgICR0ZXh0ID0gJGFwcC5uYW1lICsgJyDlt7Lov57nu60gJyArIFttYXRoXTo6Um91bmQoJGFwcC5pZGxlTWludXRlcywgMSkgKyAnIOWIhumSn+aXoOS4iuS8oC/kuIvovb3vvIznu5PmnZ/lkI7lj7AgJyArICRyZXN1bHQuS2lsbGVkICsgJyDkuKrov5vnqIvjgIInCiAgICAgICAgICAgICAgICAgICAgaWYgKCRyZXN1bHQuUmVtYWluaW5nIC1ndCAwKSB7CiAgICAgICAgICAgICAgICAgICAgICAgICR0ZXh0ID0gJHRleHQgKyAnIOWPpuaciSAnICsgJHJlc3VsdC5SZW1haW5pbmcgKyAnIOS4qui/" +
        "m+eoi+acquiDvee7k+adn+OAgicKICAgICAgICAgICAgICAgICAgICB9CiAgICAgICAgICAgICAgICAgICAgU2hvdy1CYWxsb29uICfkvKDovpPnqbrpl7LmuIXnkIYnICR0ZXh0CiAgICAgICAgICAgICAgICB9CiAgICAgICAgICAgICAgICB0cnkgewogICAgICAgICAgICAgICAgICAgICRhbGwgPSBAKEdldC1DaW1JbnN0YW5jZSAtQ2xhc3NOYW1lIFdpbjMyX1Byb2Nlc3MgLUVycm9yQWN0aW9uIFN0b3ApCiAgICAgICAgICAgICAgICAgICAgJG5hbWVNYXAgPSBHZXQtTmFtZU1hcCAkYWxsCiAgICAgICAgICAgICAgICB9IGNhdGNoIHsKICAgICAgICAgICAgICAgICAgICAkYWxsID0gQCgpCiAgICAgICAgICAgICAgICAgICAgJG5hbWVNYXAgPSBAe30KICAgICAgICAgICAgICAgIH0KICAgICAgICAgICAgICAgICRhcHBQcm9jcyA9IEAoR2V0LUFwcFByb2Nlc3NlcyAkYXBwICRuYW1lTWFwKQogICAgICAgICAgICAgICAgJHJ1bm5pbmcgPSAoJGFwcFByb2NzLkNvdW50IC1ndCAwKQogICAgICAgICAgICAgICAgJGJ5dGVzID0gW2xvbmddMAogICAgICAgICAgICAgICAgJGlkbGVTZWNvbmRzID0gMAogICAgICAgICAgICB9CiAgICAgICAgfQoKICAgICAgICAkbGFzdEFjdGl2ZVRleHQgPSAkbnVsbAogICAgICAgIGlmICgkbnVsbCAtbmUgJHN0Lmxhc3RBY3RpdmUpIHsgJGxhc3RBY3RpdmVUZXh0ID0gKFtkYXRldGltZV0kc3QubGFzdEFjdGl2ZSkuVG9TdHJpbmcoJ28nKSB9CiAgICAgICAgJGxhc3RLaWxsVGV4dCA9ICRudWxsCiAgICAgICAgaWYgKCRzdC5sYXN0S2lsbCAtbmUgW2RhdGV0aW1lXTo6TWluVmFsdWUpIHsgJGxhc3RLaWxsVGV4dCA9IChbZGF0ZXRpbWVdJHN0Lmxhc3RLaWxsKS5Ub1N0cmluZygnbycpIH0KCiAgICAgICAgJGFwcFN0YXR1c2VzICs9IFtwc2N1c3RvbW9iamVjdF1AewogICAgICAgICAgICBuYW1lICAgICAgICAgICAgICA9IFtzdHJpbmddJGFwcC5uYW1lCiAgICAgICAgICAgIHJ1bm5pbmcgICAgICAgICAgID0gJHJ1bm5pbmcKICAgICAgICAgICAgcHJvY2Vzc0NvdW50ICAgICAgPSAkYXBwUHJvY3MuQ291bnQKICAgICAgICAgICAgdHJhbnNmZXJQcm9jZXNzZXMgPSAoQCgkYXBwLnRyYW5zZmVyUHJvY2Vzc2VzKSAtam9pbiAnOycpCiAgICAgICAgICAgIHRyYW5zZmVyQnl0ZXMgICAgID0gJGJ5dGVzCiAgICAgICAgICAgIGlkbGVTZWNvbmRzICAgICAgID0gJGlkbGVTZWNvbmRzCiAgICAgICAgICAgIGlkbGVMaW1pdFNlY29uZHMgID0gJGFwcC5pZGxlU2Vjb25kcwogICAgICAgICAgICBsYXN0QWN0aXZlICAgICAgICA9ICRsYXN0QWN0aXZlVGV4dAogICAgICAgICAgICBraWxscyAgICAgICAgICAgICA9ICRzdC5raWxscwogICAgICAgICAgICBsYXN0S2lsbCAgICAgICAgICA9ICRsYXN0S2lsbFRleHQKICAgICAgICB9CiAgICB9CgogICAgaWYgKCRhcHBTdGF0dXNlcy5Db3VudCAtZXEgMCkgeyAkc2NyaXB0Okxhc3RFdmVudCA9ICfmsqHmnInlkK/nlKjnmoTova/ku7YnIH0KICAgIFdyaXRlLVN0YXR1cyAkYXBwU3RhdHVzZXMgJGNvbmZpZy5zYW1wbGVTZWNvbmRzCiAgICBTdGFydC1TbGVlcCAtTWlsbGlzZWNvbmRzIDUwMAp9Cgp0cnkgewogICAgUmVtb3ZlLUl0ZW0gLUxpdGVyYWxQYXRoICRzdGF0dXNQYXRoIC1Gb3JjZSAtRXJyb3JBY3Rpb24gU2lsZW50bHlDb250aW51ZQp9IGNhdGNoIHsKfQpXcml0ZS1Mb2cgJ+ebkeaOp+mAgOWHuicK";
}
