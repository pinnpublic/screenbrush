using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace ScreenBrush;

internal sealed class ToolbarWindow : Window
{
    private readonly AppController controller;
    private readonly TextBlock status = new();
    internal string StatusText => status.Text;
    private readonly Button freehand;
    private readonly Button cycleColor;
    private readonly Button customColor;
    private readonly Button editBoardColor;
    private readonly TextBlock colorLabel;
    private readonly Dictionary<Tool, Button> tools = new();
    private readonly Dictionary<ActionId, Button> colorButtons = new();
    private readonly Dictionary<ActionId, Button> shortcutButtons = new();
    private readonly Button boardToggle;
    private readonly Dictionary<BoardBackground, Button> boardButtons = new();
    private bool closeQueued;
    private bool positionReady;
    internal ToolbarWindow(AppController controller)
    {
        this.controller = controller;
        PreviewKeyDown += (_, e) =>
        {
            if ((e.Key == Key.System ? e.SystemKey : e.Key) == Key.F4 && Keyboard.Modifiers == ModifierKeys.Alt && !controller.Settings.IsShortcutEnabled(ActionId.Quit))
            { e.Handled = true; return; }
            if (e.Key != Key.Escape || Keyboard.Modifiers != ModifierKeys.None) return;
            e.Handled = true;
            controller.EnterScreenMode();
        };
        Icon = AppIcon.WindowImage;
        Title = "ScreenBrush"; Width = 300; SizeToContent = SizeToContent.Height;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        Background = Brush("#1B2030");
        Topmost = true; ShowInTaskbar = true;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
        Foreground = Brush("#E8ECF5");
        Left = SystemParameters.WorkArea.Right - Width - 24; Top = SystemParameters.WorkArea.Top + 32;
        Loaded += (_, _) => InitializePosition();
        LocationChanged += (_, _) => RememberPosition();
        var panel = new StackPanel { Margin = new Thickness(18) };
        Content = new Border { Background = Brush("#F51B2030"), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), BorderBrush = Brush("#414A61"), Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = SystemParameters.WorkArea.Height - 64 } };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var close = Button("×", () => controller.Execute(ActionId.Quit)); close.Width = 30;
        shortcutButtons[ActionId.Quit] = close;
        DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        var minimize = ShortcutButton("−", ActionId.ToggleToolbar); minimize.Width = 30;
        DockPanel.SetDock(minimize, Dock.Right); header.Children.Add(minimize);
        header.Children.Add(new Image { Source = Icon, Width = 26, Height = 26, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        var title = new TextBlock { Text = "ScreenBrush", FontSize = 19, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.SizeAll };
        title.MouseLeftButtonDown += (_, _) => DragMove(); header.Children.Add(title); panel.Children.Add(header);
        status.Margin = new Thickness(0, 10, 0, 5); status.Foreground = Brush("#ABB7D0"); status.TextWrapping = TextWrapping.Wrap; panel.Children.Add(status);
        panel.Children.Add(Divider());
        panel.Children.Add(Label("필기 도구"));
        var brushGrid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
        panel.Children.Add(brushGrid);
        panel.Children.Add(Label("그리기 방식"));
        var toolGrid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
        freehand = ShortcutButton("자유 필기", ActionId.Freehand);
        toolGrid.Children.Add(freehand);
        foreach (Tool tool in new[] { Tool.Marker, Tool.Ballpoint, Tool.Pencil, Tool.Line, Tool.Arrow, Tool.Rectangle, Tool.Ellipse, Tool.Eraser })
        {
            Tool selected = tool;
            var button = Button(Settings.Label(Enum.Parse<ActionId>(tool.ToString())), () => controller.Execute(Enum.Parse<ActionId>(selected.ToString())));
            tools[tool] = button;
            if (tool is Tool.Marker or Tool.Ballpoint or Tool.Pencil) brushGrid.Children.Add(button); else toolGrid.Children.Add(button);
        }
        panel.Children.Add(toolGrid);
        panel.Children.Add(Divider());
        colorLabel = Label("잉크 색상"); panel.Children.Add(colorLabel);
        var palette = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var entry in Settings.Palette)
        {
            string color = entry.Hex;
            var swatch = Button("", () => controller.Execute(entry.Action));
            swatch.Background = Brush(color); swatch.Width = 29; swatch.Height = 29; swatch.Padding = new Thickness(0); swatch.ToolTip = color;
            palette.Children.Add(swatch);
            colorButtons[entry.Action] = swatch;
        }
        customColor = Button("…", () => controller.ChooseColor()); customColor.Width = 29; customColor.Height = 29; customColor.Padding = new Thickness(0);
        palette.Children.Add(customColor); panel.Children.Add(palette);
        cycleColor = Button("", () => controller.Execute(ActionId.CycleColor));
        shortcutButtons[ActionId.CycleColor] = cycleColor;
        panel.Children.Add(cycleColor);
        var opacityLabel = Label($"잉크 불투명도  {controller.Settings.InkOpacity:P0}"); panel.Children.Add(opacityLabel);
        var opacity = new Slider { Minimum = 0, Maximum = 1, Value = controller.Settings.InkOpacity, TickFrequency = 0.01, IsSnapToTickEnabled = true, Margin = new Thickness(0, 4, 0, 4) };
        opacity.ValueChanged += (_, _) => { controller.ChangeOpacity(opacity.Value); opacityLabel.Text = $"잉크 불투명도  {opacity.Value:P0}"; };
        panel.Children.Add(opacity);
        panel.Children.Add(new TextBlock { Text = "투명 0% ↔ 불투명 100% · 새 획에 적용", FontSize = 11, Foreground = Brush("#ABB7D0") });
        var widthLabel = Label($"굵기  {controller.Settings.Width:0.0} px"); panel.Children.Add(widthLabel);
        var width = new Slider { Minimum = 0.5, Maximum = 18, Value = controller.Settings.Width, Margin = new Thickness(0, 4, 0, 3), TickFrequency = 0.5 };
        width.ValueChanged += (_, _) => { controller.ChangeWidth(width.Value); widthLabel.Text = $"굵기  {width.Value:0.0} px"; }; panel.Children.Add(width);
        panel.Children.Add(Divider());
        boardToggle = ShortcutButton("화이트보드", ActionId.ToggleBoard); panel.Children.Add(boardToggle);
        var boardPalette = new System.Windows.Controls.Primitives.UniformGrid { Columns = 8, Margin = new Thickness(0, 4, 0, 3) };
        foreach (var entry in Whiteboard.Palette)
        {
            var button = ShortcutButton(entry.Name, entry.Action);
            button.FontSize = 12; button.Height = 29; button.Padding = new Thickness(0);
            button.Content = entry.Background switch
            {
                BoardBackground.White or BoardBackground.Black or BoardBackground.CustomColor => "",
                BoardBackground.Chalkboard => "1", BoardBackground.Notebook => "2",
                BoardBackground.Dots => "3", _ => "▧"
            };
            boardButtons[entry.Background] = button; boardPalette.Children.Add(button);
        }
        editBoardColor = Button("…", () => controller.ChooseColor(true));
        editBoardColor.Height = 29; editBoardColor.Padding = new Thickness(0);
        editBoardColor.ToolTip = "화이트보드 사용자 배경색 지정";
        boardPalette.Children.Add(editBoardColor); panel.Children.Add(boardPalette);
        panel.Children.Add(Divider());
        var zoomRow = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
        zoomRow.Children.Add(ShortcutButton("− 축소", ActionId.ZoomOut));
        zoomRow.Children.Add(ShortcutButton("100%", ActionId.ZoomReset));
        zoomRow.Children.Add(ShortcutButton("+ 확대", ActionId.ZoomIn)); panel.Children.Add(zoomRow);
        var editRow = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        editRow.Children.Add(ShortcutButton("실행 취소", ActionId.Undo));
        editRow.Children.Add(ShortcutButton("다시 실행", ActionId.Redo));
        editRow.Children.Add(ShortcutButton("모두 지우기", ActionId.Clear));
        editRow.Children.Add(ShortcutButton("화면 저장", ActionId.SaveScreen)); panel.Children.Add(editRow);
        panel.Children.Add(Divider());
        panel.Children.Add(Button("설정 · 단축키", controller.OpenSettings));
        panel.Children.Add(new TextBlock { Text = "휠: 아래 화면 스크롤 · Shift: 도형 비율 고정", FontSize = 11, Foreground = Brush("#9DAAC3"), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
        Closing += (_, e) =>
        {
            if (controller.Disposing) return;
            e.Cancel = true;
            if (closeQueued) return;
            closeQueued = true;
            // WPF forbids closing this window again inside its Closing event.
            Dispatcher.BeginInvoke(() => { if (!controller.Disposing) controller.Quit(); });
        };
        Refresh();
    }
    private void InitializePosition()
    {
        if (positionReady) return;
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var settings = controller.Settings;
        if (settings.RememberToolbarPosition && settings.ToolbarX is int x && settings.ToolbarY is int y &&
            Native.GetWindowRect(handle, out var bounds))
        {
            var area = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(x, y)).WorkingArea;
            var location = ClampPosition(x, y, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, area);
            Native.SetWindowPos(handle, IntPtr.Zero, location.X, location.Y, 0, 0, 0x15);
        }
        positionReady = true;
        RememberPosition();
    }
    internal static System.Drawing.Point ClampPosition(int x, int y, int width, int height, System.Drawing.Rectangle area) =>
        new(Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - width)),
            Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - height)));
    private void RememberPosition()
    {
        if (!positionReady || controller.Disposing || WindowState != WindowState.Normal) return;
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (!Native.GetWindowRect(handle, out var bounds)) return;
        controller.Settings.ToolbarX = bounds.Left;
        controller.Settings.ToolbarY = bounds.Top;
    }
    internal void Refresh(string? message = null)
    {
        foreach (var pair in shortcutButtons)
        {
            ToolTipService.SetShowOnDisabled(pair.Value, true);
            pair.Value.ToolTip = $"{Settings.Label(pair.Key)} · {controller.Settings.Shortcuts[pair.Key]}" + (controller.Settings.IsShortcutEnabled(pair.Key) ? "" : " · 단축키 꺼짐");
        }
        boardToggle.Content = controller.Settings.WhiteboardEnabled ? "✓ 화이트보드 켜짐" : "화이트보드 켜기";
        boardToggle.Background = controller.Settings.WhiteboardEnabled ? Brush("#3158B0") : Brush("#2C3448");
        foreach (var pair in boardButtons)
        {
            pair.Value.Background = pair.Key switch
            {
                BoardBackground.White => Brushes.White,
                BoardBackground.Black => Brushes.Black,
                BoardBackground.CustomColor => Brush(controller.Settings.BoardColor),
                _ => Brush("#2C3448")
            };
            pair.Value.BorderThickness = new Thickness(2);
            pair.Value.BorderBrush = controller.Settings.WhiteboardEnabled && controller.Settings.BoardBackground == pair.Key ? Brush("#8DB7FF") : Brushes.Transparent;
        }
        cycleColor.ToolTip += "\n빨강 → 주황 → 노랑 → 초록 → 파랑 → 보라 순서로 부드럽게 변합니다. 단색을 선택하면 해제됩니다.";
        foreach (var action in new[] { ActionId.Undo, ActionId.Redo }) shortcutButtons[action].ToolTip += "\n단축키는 그리기 모드에서 동작합니다.";
        status.ToolTip = $"{controller.Settings.HoldKey} 누르는 동안 아래 화면 조작";
        var selectedColor = (Color)ColorConverter.ConvertFromString(controller.Settings.Color);
        bool paletteSelected = false;
        colorLabel.Text = controller.Settings.CycleColors ? "잉크 색상 · 순환색" : "잉크 색상 · 사용자 색";
        foreach (var entry in Settings.Palette)
        {
            var button = colorButtons[entry.Action];
            ToolTipService.SetShowOnDisabled(button, true);
            var color = (Color)ColorConverter.ConvertFromString(entry.Hex);
            bool selected = !controller.Settings.CycleColors && selectedColor == color;
            MarkColorSelection(button, selected, color);
            button.ToolTip = $"{entry.Name} · {controller.Settings.Shortcuts[entry.Action]}" + (selected ? " · 선택됨" : "") + (controller.Settings.IsShortcutEnabled(entry.Action) ? "" : " · 단축키 꺼짐");
            if (selected) { paletteSelected = true; colorLabel.Text = $"잉크 색상 · {entry.Name}"; }
        }
        bool customSelected = !controller.Settings.CycleColors && !paletteSelected;
        customColor.Background = customSelected ? new SolidColorBrush(selectedColor) : Brush("#2C3448");
        MarkColorSelection(customColor, customSelected, selectedColor);
        if (!customSelected) { customColor.Content = "…"; customColor.Foreground = Brush("#F0F3FA"); }
        customColor.ToolTip = "사용자 잉크색 지정" + (customSelected ? $" · 선택됨 ({controller.Settings.Color})" : "");
        cycleColor.Content = controller.Settings.CycleColors ? "✓  순환색 · 빨 → 주 → 노 → 초 → 파 → 보" : "순환색 · 빨 → 주 → 노 → 초 → 파 → 보";
        cycleColor.Background = controller.Settings.CycleColors ? Brush("#415277") : Brush("#2C3448");
        status.Text = message ?? (controller.Drawing ? "Esc · 아래 화면 조작" : "도구 선택 · 바로 그리기") + $"\n{controller.Settings.HoldKey} 누르는 동안 아래 화면 조작";
        freehand.Background = controller.Settings.Tool != Tool.Eraser && controller.Settings.DrawingMode == DrawingMode.Freehand ? Brush("#415277") : Brush("#2C3448");
        foreach (var item in tools)
        {
            bool selected = item.Key == Tool.Eraser ? controller.Settings.Tool == Tool.Eraser :
                controller.Settings.Tool != Tool.Eraser && (item.Key is Tool.Marker or Tool.Ballpoint or Tool.Pencil
                    ? item.Key == controller.Settings.BrushTool
                    : item.Key.ToString() == controller.Settings.DrawingMode.ToString());
            item.Value.Background = selected ? Brush("#415277") : Brush("#2C3448");
            var action = Enum.Parse<ActionId>(item.Key.ToString());
            ToolTipService.SetShowOnDisabled(item.Value, true);
            item.Value.ToolTip = $"{Settings.Label(action)} · {controller.Settings.Shortcuts[action]}" + (controller.Settings.IsShortcutEnabled(action) ? "" : " · 단축키 꺼짐");
            if (item.Key is Tool.Line or Tool.Arrow or Tool.Rectangle or Tool.Ellipse)
                item.Value.ToolTip += "\nShift: 45도 방향 또는 정사각형·원으로 제한";
        }
    }
    private Button ShortcutButton(string text, ActionId action)
    {
        var button = Button(text, () => controller.Execute(action));
        shortcutButtons[action] = button;
        return button;
    }
    private static void MarkColorSelection(Button button, bool selected, Color color)
    {
        Brush contrast = color.R * .299 + color.G * .587 + color.B * .114 > 128 ? Brushes.Black : Brushes.White;
        button.BorderThickness = new Thickness(2);
        button.BorderBrush = selected ? contrast : Brushes.Transparent;
        button.Content = selected ? "✓" : "";
        button.Foreground = contrast; button.FontSize = 16; button.FontWeight = FontWeights.Bold;
    }
    internal static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    private static readonly ControlTemplate DarkButtonTemplate = (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
          <Border x:Name="Frame" Background="{TemplateBinding Background}"
                  BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}"
                  SnapsToDevicePixels="True">
            <Grid>
              <Border x:Name="Hover" Background="#12FFFFFF" Opacity="0" IsHitTestVisible="False"/>
              <Border x:Name="Pressed" Background="#26000000" Opacity="0" IsHitTestVisible="False"/>
              <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="Center"
                                VerticalAlignment="Center" RecognizesAccessKey="True"
                                TextElement.Foreground="{TemplateBinding Foreground}"/>
            </Grid>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Hover" Property="Opacity" Value="1"/></Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Setter TargetName="Hover" Property="Opacity" Value="0"/>
              <Setter TargetName="Pressed" Property="Opacity" Value="1"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter TargetName="Frame" Property="Opacity" Value="0.45"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """);
    internal static Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Template = DarkButtonTemplate, Margin = new Thickness(2), Padding = new Thickness(6, 7, 6, 7), Background = Brush("#2C3448"), Foreground = Brush("#F0F3FA"), BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
        button.Click += (_, _) => action(); return button;
    }
    private static Border Divider() => new() { Height = 1, Background = Brush("#8795B2"), Margin = new Thickness(2, 10, 2, 10), SnapsToDevicePixels = true, IsHitTestVisible = false };
    private static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 3, 0, 5), Foreground = Brush("#ABB7D0"), FontSize = 12 };
}
