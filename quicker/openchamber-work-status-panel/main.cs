void Main(string quicker_in_param = "")
{
    // 触发在右键按下瞬间；稍等，让点击/选区状态稳定。
    System.Threading.Thread.Sleep(120);

    // 读取选区会动剪贴板；先备份，粘贴时再放回去（内置恢复不包含图片格式）。
    Img img = null;
    string clipText = "";
    string[] clipFiles = new string[0];
    try { img = qk.Clipboard.GetImage(); } catch { }
    try { clipText = qk.Clipboard.GetText() ?? ""; } catch { }
    try { clipFiles = qk.Clipboard.GetFiles(); } catch { }

    string selected = "";
    try
    {
        selected = qk.Selection.GetText(400) ?? "";
    }
    catch (ActionApiException ex) when (ex.Code.Value == "SELECTION_FAILED")
    {
        selected = "";
    }

    var key = string.IsNullOrEmpty(selected) ? "Ctrl+V" : "Ctrl+C";
    var fg = qk.Window.GetForeground();
    qk.Log("[智能复制粘贴] selLen=" + selected.Length + " key=" + key + " img=" + (img != null) + " fg=" + (fg != null ? fg.Title : "?"));

    if (key == "Ctrl+V")
    {
        if (img != null) qk.Clipboard.SetImage(img);
        else if (clipFiles.Length > 0) qk.Clipboard.SetFiles(clipFiles);
        else if (clipText.Length > 0) qk.Clipboard.SetText(clipText);
    }

    // 鼠标还按着时 qk 会拒绝模拟输入，松开后重试。
    var deadline = System.Environment.TickCount64 + 2500;
    while (System.Environment.TickCount64 < deadline)
    {
        try
        {
            qk.Keyboard.Press(key);
            qk.Ui.Notify(key == "Ctrl+C" ? "已复制" : "已粘贴", "info", null, 1200, "bottomCenter");
            return;
        }
        catch (ActionApiException ex) when (ex.Code.Value == "INPUT_USER_ACTIVE")
        {
        }
        System.Threading.Thread.Sleep(30);
    }

    qk.Keyboard.Press(key);
    qk.Ui.Notify(key == "Ctrl+C" ? "已复制" : "已粘贴", "info", null, 1200, "bottomCenter");
}