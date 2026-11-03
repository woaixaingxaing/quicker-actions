// 鼠标离开 OpenChamber 窗口时，自动打开右上角「工作状态」面板，
// 让 TPS / 用量 / 系统状态随时可见。
// 触发方式：「定时重复」事件，每秒一次。刚离开窗口立即处理，离开期间每 10 秒复核一次。
// 只用 UI Automation 读取按钮状态并按下，不移动鼠标、不抢焦点；已经开着时什么都不做。

void Main()
{
    try
    {
        EnsurePanel();
    }
    catch
    {
        // 后台轮询，失败时保持安静
    }
}

void EnsurePanel()
{
    WinInfo target = null;
    var wins = qk.Window.FindAllInfo(process: "OpenChamber");
    foreach (var w in wins)
    {
        if (!w.Visible || w.State == "minimized") continue;
        if (target == null || w.Bounds.Width * w.Bounds.Height > target.Bounds.Width * target.Bounds.Height)
        {
            target = w;
        }
    }
    if (target == null) return;

    var b = target.Bounds;
    var p = qk.Mouse.GetPosition();
    bool inside = p.X >= b.X && p.X <= b.X + b.Width && p.Y >= b.Y && p.Y <= b.Y + b.Height;

    bool wasInside = qk.State.Get<bool>("wasInside", false);
    if (inside)
    {
        if (!wasInside) qk.State.Set("wasInside", true);
        return;
    }

    bool justLeft = wasInside;
    long lastTicks = qk.State.Get<long>("lastCheckTicks", 0L);
    double age = (DateTime.UtcNow.Ticks - lastTicks) / 10000000.0;
    if (!justLeft && age >= 0 && age < 10) return;

    qk.State.Set("lastCheckTicks", DateTime.UtcNow.Ticks);
    if (justLeft) qk.State.Set("wasInside", false);

    var toggle = qk.Uia.Find(target.Window, name: "切换工作状态面板");
    if (toggle == null) toggle = qk.Uia.Find(target.Window, name: "Toggle work status panel");
    if (toggle == null) return;

    if (qk.Uia.Info(toggle).Toggled == true) return;
    qk.Uia.Act(toggle, "check");
}
