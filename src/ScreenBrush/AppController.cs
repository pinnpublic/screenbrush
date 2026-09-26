using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace ScreenBrush;

internal sealed class AppController : IDisposable
{
    internal Settings Settings { get; private set; }
    internal bool Drawing { get; private set; }
    internal bool Disposing { get; private set; }
    private bool holding, settingsOpen, excluded, savingScreen;
    private readonly List<OverlayWindow> overlays = new();
    private ToolbarWindow? toolbar;
    private Hotkeys? hotkeys;
    private WheelZoomHook? wheelZoom;
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private string? startupWarning;
    private readonly bool saveOnExit;
    private double nextColorHue;
    private System.Threading.Tasks.Task<string>? pendingScreenSave;
    internal IReadOnlyList<OverlayWindow> Overlays => overlays;
    internal ToolbarWindow Toolbar => toolbar!;
    private bool EffectiveDrawing => Drawing && !holding && !settingsOpen;
    internal AppController(Settings? initialSettings = null, bool saveOnExit = true)
    {
        this.saveOnExit = saveOnExit;
        Settings = initialSettings ?? Settings.Load(out startupWarning);
        Settings.InitializeDrawingSelection();
        Settings.InitializeToolColors();
        Settings.RestoreToolColor();
    }
    internal void Start()
    {
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var overlay = new OverlayWindow(screen);
            overlay.Surface.ColorPhaseForNewMark = () => nextColorHue;
            overlay.Surface.MarkCompleted += mark => { if (mark.FlowColors) nextColorHue = FlowColorRendering.EndHue(mark); };
            overlay.WheelForward += ForwardWheel;
            overlay.ViewPresented += KeepControlsAboveCanvas;
            overlay.CaptureFailed += error => { ResetZoom(); toolbar?.Refresh(error); };
            overlays.Add(overlay); overlay.Show();
        }
        toolbar = new ToolbarWindow(this);
        new WindowInteropHelper(toolbar).EnsureHandle();
        Application.Current.MainWindow = toolbar;
        hotkeys = new Hotkeys(Settings.HoldKey);
        hotkeys.SuppressHold = () => Settings.MatchesWheelZoom(WheelZoomHook.Modifiers);
        wheelZoom = new WheelZoomHook();
        wheelZoom.Wheel = (delta, point) =>
        {
            if (Disposing || settingsOpen || savingScreen || !Settings.MatchesWheelZoom(WheelZoomHook.Modifiers)) return false;
            var target = overlays.FirstOrDefault(o => o.Screen.Bounds.Contains(point.X, point.Y));
            if (target == null) return false;
            double change = delta / 120.0 * Settings.ZoomStepPercent / 100.0;
            // Consume the wheel now; screen capture and drawing stay outside the hook.
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (Disposing || settingsOpen || savingScreen) return;
                hotkeys.ResetHold();
                Zoom(change, target);
            });
            return true;
        };
        hotkeys.Triggered += ExecuteShortcut;
        hotkeys.DrawingDismissed += EnterScreenMode;
        hotkeys.HoldChanged += value => { holding = value; UpdateMode(); };
        string? error = hotkeys.Apply(Settings.ActiveShortcuts());
        trayIcon = AppIcon.CreateTrayIcon();
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "ScreenBrush", Visible = true, ContextMenuStrip = new Forms.ContextMenuStrip() };
        tray.ContextMenuStrip.Items.Add("도구 모음 열기", null, (_, _) => ShowToolbar()).Tag = ActionId.ToggleToolbar;
        tray.ContextMenuStrip.Items.Add("설정 · 단축키", null, (_, _) => OpenSettings());
        tray.ContextMenuStrip.Items.Add("종료", null, (_, _) => Quit()).Tag = ActionId.Quit;
        tray.DoubleClick += (_, _) => ShowToolbar();
        Drawing = false;
        Settings.RememberToolColor();
        Settings.WhiteboardEnabled = false;
        Settings.RestoreToolColor();
        ApplyBoard(); UpdateTools(); UpdateMode();
        if (Settings.ShowToolbarOnStartup) ShowToolbar();
        if ((error ?? startupWarning) is string warning)
        {
            toolbar.Refresh(warning);
            tray.ShowBalloonTip(5000, "ScreenBrush 설정 확인", warning, Forms.ToolTipIcon.Warning);
        }
    }
    internal void ShowToolbar() { if (Disposing) return; toolbar?.Show(); toolbar?.Activate(); }
    internal void ToggleToolbar()
    {
        if (Disposing || settingsOpen || savingScreen || toolbar == null) return;
        if (toolbar.IsVisible) toolbar.Hide();
        else ShowToolbar();
    }
    internal void EnterScreenMode()
    {
        if (Disposing || settingsOpen || savingScreen) return;
        foreach (var overlay in overlays) overlay.Surface.Finish();
        Drawing = false;
        UpdateMode();
        // Board activation is explicit, never part of Esc session restoration.
        Settings.RememberToolColor();
        Settings.WhiteboardEnabled = false;
        Settings.RestoreToolColor();
        if (!Settings.PreserveSessionOnEscape)
        {
            foreach (var overlay in overlays)
            {
                overlay.Surface.DiscardSession();
                overlay.ChangeZoom(1);
            }
            nextColorHue = 0;
        }
        ApplyBoard(); UpdateTools();
    }
    internal void SelectTool(Tool tool)
    {
        if (!Enum.IsDefined(tool)) return;
        Settings.RememberToolColor();
        if (tool is Tool.Marker or Tool.Ballpoint or Tool.Pencil) Settings.BrushTool = tool;
        else if (tool != Tool.Eraser) Settings.DrawingMode = Enum.Parse<DrawingMode>(tool.ToString());
        Settings.Tool = tool; Settings.RestoreToolColor();
        Drawing = true; UpdateTools(); UpdateMode();
    }
    internal void SelectFreehand()
    {
        Settings.RememberToolColor();
        Settings.DrawingMode = DrawingMode.Freehand;
        Settings.Tool = Settings.BrushTool; Settings.RestoreToolColor();
        Drawing = true; UpdateTools(); UpdateMode();
    }
    internal void ChangeColor(string color)
    {
        Settings.Color = color; Settings.CycleColors = false;
        Settings.RememberToolColor(); UpdateTools();
    }
    internal void ToggleCycleColor()
    {
        foreach (var overlay in overlays) overlay.Surface.Finish();
        Settings.CycleColors = !Settings.CycleColors;
        Settings.RememberToolColor();
        if (Settings.CycleColors) nextColorHue = 0;
        UpdateTools();
    }
    internal void ChangeWidth(double width) { Settings.Width = width; UpdateTools(); }
    internal void ChangeOpacity(double opacity) { Settings.InkOpacity = opacity; UpdateTools(); }
    private sealed record WindowOwner(IntPtr Handle) : Forms.IWin32Window;
    internal void ChooseColor(bool boardColor = false)
    {
        if (Disposing || settingsOpen || savingScreen || toolbar == null || hotkeys == null) return;
        settingsOpen = true; hotkeys.Suspended = true; hotkeys.ResetHold(); holding = false; UpdateMode();
        hotkeys.Apply(new Dictionary<ActionId, Shortcut>());
        try
        {
            using var picker = new Forms.ColorDialog { FullOpen = true, Color = System.Drawing.ColorTranslator.FromHtml(boardColor ? Settings.BoardColor : Settings.Color) };
            if (picker.ShowDialog(new WindowOwner(new WindowInteropHelper(toolbar).Handle)) == Forms.DialogResult.OK)
            {
                string color = $"#FF{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}";
                if (boardColor) { Settings.BoardColor = color; SelectBoard(BoardBackground.CustomColor); }
                else ChangeColor(color);
            }
        }
        finally
        {
            settingsOpen = false;
            if (!Disposing)
            {
                string? error = hotkeys.Apply(Settings.ActiveShortcuts());
                hotkeys.Suspended = false;
                UpdateMode();
                if (error != null) toolbar.Refresh(error);
            }
        }
    }
    private Brush boardBrush = Brushes.White;
    private (BoardBackground, string, string)? boardBrushKey;
    private void ApplyBoard()
    {
        var key = (Settings.BoardBackground, Settings.BoardColor, Settings.BoardImagePath);
        string? error = null;
        if (Settings.WhiteboardEnabled && boardBrushKey != key)
        {
            if (Whiteboard.TryLoad(Settings, out boardBrush, out error)) boardBrushKey = key;
        }
        foreach (var overlay in overlays) overlay.SetBoard(Settings.WhiteboardEnabled, boardBrush);
        if (error != null) toolbar?.Refresh(error);
    }
    internal void ToggleBoard()
    {
        if (!Settings.WhiteboardEnabled && !Whiteboard.TryLoad(Settings, out boardBrush, out var error))
        { toolbar?.Refresh(error); return; }
        Settings.RememberToolColor();
        Settings.WhiteboardEnabled = !Settings.WhiteboardEnabled;
        Settings.RestoreToolColor();
        boardBrushKey = (Settings.BoardBackground, Settings.BoardColor, Settings.BoardImagePath);
        if (Settings.WhiteboardEnabled) Drawing = true;
        ApplyBoard(); UpdateTools(); UpdateMode();
    }
    internal void SelectBoard(BoardBackground background)
    {
        var candidate = Settings.Clone(); candidate.BoardBackground = background;
        if (!Whiteboard.TryLoad(candidate, out var brush, out var error))
        { toolbar?.Refresh(error); return; }
        Settings.RememberToolColor();
        Settings.BoardBackground = background; Settings.WhiteboardEnabled = true; Drawing = true;
        Settings.RestoreToolColor();
        boardBrush = brush;
        boardBrushKey = (Settings.BoardBackground, Settings.BoardColor, Settings.BoardImagePath);
        foreach (var overlay in overlays) overlay.SetBoard(true, brush);
        UpdateTools(); UpdateMode();
    }
    private void UpdateTools()
    {
        if (tray?.ContextMenuStrip is { } menu)
            foreach (Forms.ToolStripItem item in menu.Items)
                if (item.Tag is ActionId action)
                {
                    item.ToolTipText = $"{Settings.Label(action)} · {Settings.Shortcuts[action]}" + (Settings.IsShortcutEnabled(action) ? "" : " · 단축키 꺼짐");
                }
        foreach (var overlay in overlays)
        {
            overlay.Surface.Finish(); overlay.Surface.Tool = Settings.EffectiveTool;
            overlay.Surface.BrushTool = Settings.BrushTool;
            overlay.Surface.InkColor = (Color)ColorConverter.ConvertFromString(Settings.Color);
            overlay.Surface.CycleColors = Settings.CycleColors;
            overlay.Surface.ColorCycleSpeed = Settings.ColorCycleSpeed;
            overlay.Surface.InkWidth = Settings.Width;
            overlay.Surface.InkOpacity = Settings.InkOpacity;
            overlay.Surface.AutoFadeEnabled = Settings.AutoFadeEnabled;
            overlay.Surface.AutoFadeHoldSeconds = Settings.AutoFadeHoldSeconds;
            overlay.Surface.AutoFadeDurationSeconds = Settings.AutoFadeDurationSeconds;
            overlay.Surface.Cursor = Settings.Tool == Tool.Eraser ? Cursors.Cross : Cursors.Pen;
        }
        toolbar?.Refresh();
    }
    private void UpdateMode()
    {
        if (hotkeys != null) hotkeys.EditingEnabled = Drawing;
        if (!Settings.WhiteboardEnabled && EffectiveDrawing && overlays.Any(o => o.Zoom > 1)) UpdateExclusion(true);
        foreach (var overlay in overlays)
        {
            // Esc hides retained ink; the temporary hold key keeps its existing behavior.
            overlay.Surface.Visibility = Drawing ? Visibility.Visible : Visibility.Hidden;
            overlay.SetEnabled(EffectiveDrawing);
        }
        if (Settings.WhiteboardEnabled || !EffectiveDrawing || overlays.All(o => o.Zoom == 1)) UpdateExclusion(false);
        KeepControlsAboveCanvas();
        toolbar?.Refresh(holding && Drawing ? "아래 화면 조작 중 · 키를 떼면 필기로 복귀" : null);
    }
    private OverlayWindow Target()
    {
        Native.GetCursorPos(out var point);
        return overlays.FirstOrDefault(o => o.Screen.Bounds.Contains(point.X, point.Y)) ?? overlays[0];
    }
    private void KeepControlsAboveCanvas()
    {
        if (Disposing || toolbar?.IsVisible != true) return;
        // The canvas is revealed asynchronously after its first rendered frame.
        // Restore stacking then, without activating or showing a hidden toolbar.
        Native.SetWindowPos(new WindowInteropHelper(toolbar).Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
        foreach (Window dialog in toolbar.OwnedWindows)
            if (dialog.IsVisible)
                Native.SetWindowPos(new WindowInteropHelper(dialog).Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
    }
    internal void ExecuteShortcut(ActionId action)
    {
        if (Settings.IsShortcutEnabled(action)) Execute(action);
    }
    internal void Execute(ActionId action)
    {
        if (Disposing || settingsOpen || savingScreen || !Enum.IsDefined(action)) return;
        if (action == ActionId.ToggleBoard) { ToggleBoard(); return; }
        foreach (var background in Whiteboard.Palette)
            if (action == background.Action) { SelectBoard(background.Background); return; }
        foreach (var color in Settings.Palette)
            if (action == color.Action) { ChangeColor(color.Hex); return; }
        if (Enum.TryParse<Tool>(action.ToString(), out var tool)) { SelectTool(tool); return; }
        switch (action)
        {
            case ActionId.ToggleAutoFade: Settings.AutoFadeEnabled = !Settings.AutoFadeEnabled; UpdateTools(); break;
            case ActionId.Freehand: SelectFreehand(); break;
            case ActionId.SaveScreen: SaveScreen(); break;
            case ActionId.Quit: Quit(); break;
            case ActionId.ToggleToolbar: ToggleToolbar(); break;
            case ActionId.CycleColor: ToggleCycleColor(); break;
            case ActionId.ZoomIn: Zoom(Settings.ZoomStepPercent / 100.0); break;
            case ActionId.ZoomOut: Zoom(-Settings.ZoomStepPercent / 100.0); break;
            case ActionId.ZoomReset: ResetZoom(); break;
            case ActionId.Undo: Target().Surface.Undo(); break;
            case ActionId.Redo: Target().Surface.Redo(); break;
            case ActionId.Clear: foreach (var overlay in overlays) overlay.Surface.Clear(); break;
        }
    }
    private void Zoom(double change, OverlayWindow? target = null)
    {
        target ??= Target();
        if (!Settings.WhiteboardEnabled && change > 0 && !UpdateExclusion(true)) return;
        Drawing = true;
        target.ChangeZoom(Settings.ClampZoom(target.Zoom + change));
        UpdateMode();
        toolbar?.Refresh($"화면 + 필기 확대  {target.Zoom:P0}\n조작 모드에서는 잠시 원래 배율로 표시");
    }
    private void ResetZoom()
    {
        foreach (var overlay in overlays) overlay.ChangeZoom(1);
        UpdateExclusion(false); toolbar?.Refresh();
    }
    private bool UpdateExclusion(bool value)
    {
        if (excluded == value) return true;
        var handles = new List<IntPtr>();
        if (toolbar != null) handles.Add(new WindowInteropHelper(toolbar).Handle);
        foreach (var handle in handles)
        {
            if (!Native.SetWindowDisplayAffinity(handle, value ? 0x11u : 0u) && value)
            {
                foreach (var rollback in handles) Native.SetWindowDisplayAffinity(rollback, 0);
                toolbar?.Refresh("이 환경에서는 화면 확대를 시작할 수 없습니다.");
                return false;
            }
        }
        excluded = value; return true;
    }
    private void ForwardWheel(OverlayWindow source, int delta)
    {
        if (Settings.WhiteboardEnabled) return;
        Native.GetCursorPos(out var position);
        if (source.Surface.Zoom > 1)
        {
            Point local = source.PointFromScreen(new Point(position.X, position.Y));
            Point physical = source.PointToScreen(source.Surface.ToDocument(local));
            position.X = (int)physical.X; position.Y = (int)physical.Y;
        }
        // Walk the native z-order without toggling or hiding the drawing windows.
        var excludedWindows = overlays.Select(o => o.Handle).ToHashSet();
        if (toolbar != null) excludedWindows.Add(new WindowInteropHelper(toolbar).Handle);
        IntPtr target = Native.WindowBelowAt(position, excludedWindows);
        if (target == IntPtr.Zero) return;
        if (overlays.Any(o => o.Handle == target) || (toolbar != null && target == new WindowInteropHelper(toolbar).Handle)) return;
        int modifiers = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= 4;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= 8;
        var wp = new IntPtr(unchecked((delta << 16) | modifiers));
        var lp = new IntPtr(unchecked((position.Y << 16) | (position.X & 0xFFFF)));
        if (!Native.PostMessage(target, 0x020A, wp, lp)) toolbar?.Refresh("이 프로그램에는 휠 전달이 제한됩니다. 일시 조작 키를 사용하세요.");
    }
    private async void SaveScreen()
    {
        if (savingScreen || Disposing || toolbar == null || hotkeys == null) return;
        savingScreen = true;
        bool wasVisible = toolbar.IsVisible, hidden = false;
        string? message = null;
        try
        {
            var target = Target();
            target.Surface.Finish();
            hotkeys.Suspended = true;
            hotkeys.ResetHold();
            hotkeys.Apply(new Dictionary<ActionId, Shortcut>());
            // A shortcut may include the temporary screen-operation modifier.
            // Let its released drawing view settle before taking the snapshot.
            for (int i = 0; Drawing && Math.Abs(target.Surface.Zoom - target.Zoom) > .001 && i < 90 && !Disposing; i++)
                await System.Threading.Tasks.Task.Delay(16);
            if (Disposing) return;
            if (target.SnapshotUsesDesktop && wasVisible) { toolbar.Hide(); hidden = true; }
            await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            if (Disposing) return;
            Native.DwmFlush();
            var snapshot = target.CaptureSnapshot();
            if (hidden) { toolbar.ShowActivated = false; toolbar.Show(); toolbar.ShowActivated = true; hidden = false; }
            string? path = null;
            if (!Settings.UseDefaultCaptureName)
            {
                var dialog = new Microsoft.Win32.SaveFileDialog {
                    Title = "화면 저장", Filter = "PNG 이미지|*.png", DefaultExt = ".png", AddExtension = true,
                    InitialDirectory = Settings.CaptureDirectory, FileName = ScreenSave.DefaultName(DateTime.Now),
                    OverwritePrompt = false
                };
                if (dialog.ShowDialog(toolbar) != true) return;
                path = System.IO.Path.ChangeExtension(dialog.FileName, ".png");
            }
            string directory = Settings.CaptureDirectory;
            string destination = path ?? System.IO.Path.Combine(directory, ScreenSave.DefaultName(DateTime.Now));
            pendingScreenSave = System.Threading.Tasks.Task.Run(() => ScreenSave.Write(snapshot, destination));
            path = await pendingScreenSave;
            if (Disposing) return;
            message = "화면 저장 완료: " + path;
            tray?.ShowBalloonTip(3000, "ScreenBrush · PNG 저장", path, Forms.ToolTipIcon.Info);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception or ExternalException or NotSupportedException)
        {
            if (Disposing) return;
            message = "화면 저장 실패: " + error.Message;
            tray?.ShowBalloonTip(5000, "ScreenBrush · 저장 실패", error.Message, Forms.ToolTipIcon.Error);
        }
        finally
        {
            pendingScreenSave = null;
            savingScreen = false;
            if (!Disposing)
            {
                if (hidden) { toolbar.ShowActivated = false; toolbar.Show(); toolbar.ShowActivated = true; }
                string? error = hotkeys.Apply(Settings.ActiveShortcuts());
                hotkeys.Suspended = false;
                KeepControlsAboveCanvas();
                toolbar.Refresh(error ?? message);
            }
        }
    }
    internal void OpenSettings()
    {
        if (Disposing || settingsOpen || savingScreen || toolbar == null || hotkeys == null) return;
        settingsOpen = true; hotkeys.Suspended = true; hotkeys.ResetHold(); holding = false;
        hotkeys.Apply(new Dictionary<ActionId, Shortcut>());
        UpdateMode();
        try { new SettingsWindow(this) { Owner = toolbar, WindowStartupLocation = toolbar.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen }.ShowDialog(); }
        finally
        {
            settingsOpen = false;
            if (!Disposing)
            {
                string? error = hotkeys.Apply(Settings.ActiveShortcuts());
                hotkeys.HoldKey = Settings.HoldKey; hotkeys.Suspended = false;
                UpdateMode(); if (error != null) toolbar.Refresh(error);
            }
        }
    }
    internal string? PreviewShortcuts(Settings draft) => draft.Validate() ?? hotkeys?.Apply(draft.ActiveShortcuts());
    internal string? ApplySettings(Settings draft, bool? startWithWindows = null)
    {
        draft.ToolbarX = Settings.ToolbarX;
        draft.ToolbarY = Settings.ToolbarY;
        if (draft.Validate() is string error) return error;
        if (!settingsOpen && hotkeys?.Apply(draft.ActiveShortcuts()) is string registrationError) return registrationError;
        try
        {
            if (startWithWindows is bool enabled) StartupRegistration.Save(enabled, () => draft.Save());
            else draft.Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            hotkeys?.Apply(new Dictionary<ActionId, Shortcut>());
            return "설정을 저장하지 못했습니다: " + e.Message;
        }
        startupWarning = null;
        Settings = draft.Clone(); if (hotkeys != null) hotkeys.HoldKey = Settings.HoldKey;
        Settings.InitializeDrawingSelection();
        Settings.InitializeToolColors();
        Settings.RestoreToolColor();
        foreach (var overlay in overlays)
            if (overlay.Zoom > Settings.MaxZoomPercent / 100.0) overlay.ChangeZoom(Settings.ClampZoom(overlay.Zoom));
        ApplyBoard(); UpdateTools(); UpdateMode(); return null;
    }
    internal void Quit() { Dispose(); Application.Current.Shutdown(); }
    public void Dispose()
    {
        if (Disposing) return; Disposing = true;
        // Encoding only uses frozen pixels on a worker; it never needs the UI thread.
        // Complete an already-started write before process shutdown interrupts the file.
        if (pendingScreenSave != null)
        {
            try { pendingScreenSave.GetAwaiter().GetResult(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or ExternalException or NotSupportedException)
            { MessageBox.Show("화면 저장을 완료하지 못했습니다.\n" + e.Message, "ScreenBrush"); }
        }
        wheelZoom?.Dispose(); hotkeys?.Dispose(); tray?.Dispose(); trayIcon?.Dispose();
        foreach (var overlay in overlays) overlay.Close();
        toolbar?.Close();
        try { if (saveOnExit && startupWarning == null) Settings.Save(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MessageBox.Show("설정을 저장하지 못했습니다.\n" + e.Message, "ScreenBrush"); }
    }
}
