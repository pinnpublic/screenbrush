using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Controls.Primitives;

namespace ScreenBrush;

internal sealed class SettingsWindow : Window
{
    private readonly Settings draft;
    private bool quitQueued;
    internal SettingsWindow(AppController controller)
    {
        draft = controller.Settings.Clone();
        PreviewKeyDown += (_, e) =>
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                Close();
                return;
            }
            if (key != Key.F4 || Keyboard.Modifiers != ModifierKeys.Alt) return;
            e.Handled = true;
            if (quitQueued || controller.Disposing || (!controller.Drawing || !controller.Settings.IsShortcutEnabled(ActionId.Quit))) return;
            quitQueued = true;
            Dispatcher.BeginInvoke(() => { if (!controller.Disposing) controller.Quit(); });
        };
        var startup = new CheckBox { Content = "Windows 시작 시 자동 실행", Foreground = ToolbarWindow.Brush("#EDF1FA"), Margin = new Thickness(0, 0, 0, 8) };
        string? startupError = null;
        try { startup.IsChecked = StartupRegistration.IsEnabled(); }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            startup.IsEnabled = false;
            startupError = "시작 프로그램 설정을 읽지 못했습니다: " + e.Message;
        }
        Icon = AppIcon.WindowImage;
        Title = "ScreenBrush · 설정"; Width = 600; MinWidth = 520; Height = Math.Min(800, SystemParameters.WorkArea.Height - 40); MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Topmost = true;
        Background = ToolbarWindow.Brush("#1B2030"); Foreground = ToolbarWindow.Brush("#EDF1FA"); FontSize = 14;
        var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
        var heading = new TextBlock { Text = "설정", FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 20) };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var error = new TextBlock { Foreground = ToolbarWindow.Brush("#FCA5A5"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) }; bottom.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(ToolbarWindow.Button("취소", Close));
        var saveButton = ToolbarWindow.Button("저장", () => { string? problem = controller.ApplySettings(draft, startup.IsEnabled ? startup.IsChecked == true : null); if (problem != null) error.Text = problem; else Close(); });
        actions.Children.Add(saveButton); bottom.Children.Add(actions);
        void CheckShortcuts()
        {
            string? problem = controller.PreviewShortcuts(draft);
            error.Text = problem ?? "";
            saveButton.IsEnabled = problem == null;
        }
        Loaded += (_, _) => CheckShortcuts();
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        scroll.Resources[typeof(ScrollBar)] = (Style)XamlReader.Parse("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ScrollBar">
              <Setter Property="Width" Value="10"/>
              <Setter Property="Background" Value="Transparent"/>
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="ScrollBar">
                    <Grid Background="Transparent">
                      <Track x:Name="PART_Track" Orientation="Vertical" IsDirectionReversed="True">
                        <Track.DecreaseRepeatButton>
                          <RepeatButton Command="ScrollBar.PageUpCommand" Focusable="False">
                            <RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template>
                          </RepeatButton>
                        </Track.DecreaseRepeatButton>
                        <Track.Thumb>
                          <Thumb MinHeight="28" Background="#505B73">
                            <Thumb.Template>
                              <ControlTemplate TargetType="Thumb">
                                <Border Width="6" HorizontalAlignment="Center" CornerRadius="3" Background="{TemplateBinding Background}"/>
                                <ControlTemplate.Triggers>
                                  <Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="#8795B2"/></Trigger>
                                  <Trigger Property="IsDragging" Value="True"><Setter Property="Background" Value="#A8B8D6"/></Trigger>
                                </ControlTemplate.Triggers>
                              </ControlTemplate>
                            </Thumb.Template>
                          </Thumb>
                        </Track.Thumb>
                        <Track.IncreaseRepeatButton>
                          <RepeatButton Command="ScrollBar.PageDownCommand" Focusable="False">
                            <RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template>
                          </RepeatButton>
                        </Track.IncreaseRepeatButton>
                      </Track>
                    </Grid>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
            """);
        root.Children.Add(scroll);
        var sections = new StackPanel { Margin = new Thickness(0, 0, 16, 0) }; scroll.Content = sections;
        var panel = AddSection(sections, "시작 옵션");
        panel.Children.Add(startup);
        panel.Children.Add(new TextBlock { Text = startupError ?? "로그인하면 아래 시작 옵션에 따라 실행합니다. 변경 사항은 저장 시 적용됩니다.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 18) });
        panel.Children.Add(new TextBlock { Text = "시작 시 표시", Margin = new Thickness(0, 0, 0, 8) });
        var startupWindow = new ComboBox { ItemsSource = new[] { "트레이에서 시작", "도구 모음 창 표시" }, SelectedIndex = draft.ShowToolbarOnStartup ? 1 : 0, Padding = new Thickness(8) };
        startupWindow.SelectionChanged += (_, _) => draft.ShowToolbarOnStartup = startupWindow.SelectedIndex == 1;
        panel.Children.Add(startupWindow);
        panel.Children.Add(new TextBlock { Text = "다음 실행부터 적용됩니다. 앱은 화면을 가리지 않고 대기하며, 그리기 모드 버튼이나 전환 단축키로 그리기를 시작합니다.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 8, 0, 0) });
        var rememberPosition = new CheckBox { Content = "도구 모음 마지막 위치 기억", IsChecked = draft.RememberToolbarPosition, Foreground = Foreground, Margin = new Thickness(0, 16, 0, 8) };
        rememberPosition.Checked += (_, _) => draft.RememberToolbarPosition = true;
        rememberPosition.Unchecked += (_, _) => draft.RememberToolbarPosition = false;
        panel.Children.Add(rememberPosition);
        panel.Children.Add(new TextBlock { Text = "기본: 켜짐. 다음 실행 시 마지막 위치에서 엽니다. 해제하면 기본 위치에서 열며, 모니터 변경 시 화면 안으로 위치를 보정합니다.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        panel = AddSection(sections, "그리기 해제 / Esc");
        var preserveSession = new CheckBox { Content = "Esc 후 필기·기록·확대 유지", IsChecked = draft.PreserveSessionOnEscape, Foreground = Foreground, Margin = new Thickness(0, 0, 0, 12) };
        preserveSession.Checked += (_, _) => draft.PreserveSessionOnEscape = true;
        preserveSession.Unchecked += (_, _) => draft.PreserveSessionOnEscape = false;
        panel.Children.Add(preserveSession);
        panel.Children.Add(new TextBlock { Text = "기본: 해제. Esc 또는 모드 전환으로 그리기를 해제하면 모든 필기·실행 취소·다시 실행 기록을 지우고 화이트보드를 끄며 확대를 100%로 초기화합니다.\n체크하면 내용·기록을 유지하고 그리기 모드를 켜면 필기와 확대를 복원합니다. 화이트보드는 자동으로 다시 켜지지 않습니다. 설정창의 Esc 취소에는 적용하지 않습니다.", FontSize = 12, TextWrapping = TextWrapping.Wrap });
        panel = AddSection(sections, "확대 / 축소");
        var toggleZoomLabel = new TextBlock { Text = $"확대 토글 배율  {draft.ToggleZoomPercent}%", Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(toggleZoomLabel);
        var toggleZoom = new Slider { Minimum = 150, Maximum = 500, TickFrequency = 25, IsSnapToTickEnabled = true, Value = draft.ToggleZoomPercent };
        toggleZoom.ValueChanged += (_, _) => { draft.ToggleZoomPercent = (int)Math.Round(toggleZoom.Value); toggleZoomLabel.Text = $"확대 토글 배율  {draft.ToggleZoomPercent}%"; };
        panel.Children.Add(toggleZoom);
        panel.Children.Add(new TextBlock { Text = "150~500% · 기본 300% · 확대 토글로 지정 배율과 100%를 전환합니다. 마우스 휠은 확대에 사용하지 않습니다.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 8, 0, 12) });
        var zoomLabel = new TextBlock { Text = $"확대·축소 간격  {draft.ZoomStepPercent}%p", Margin = new Thickness(0, 12, 0, 8) };
        panel.Children.Add(zoomLabel);
        var zoomStep = new Slider { Minimum = 5, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true, Value = draft.ZoomStepPercent };
        zoomStep.ValueChanged += (_, _) => { draft.ZoomStepPercent = (int)Math.Round(zoomStep.Value); zoomLabel.Text = $"확대·축소 간격  {draft.ZoomStepPercent}%p"; };
        panel.Children.Add(zoomStep);
        panel.Children.Add(new TextBlock { Text = "5~100%p · 기본 10%p (100% → 110% → 120%)\n단계별 확대·축소 단축키와 버튼에 적용", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 8, 0, 18) });
        var maxZoomLabel = new TextBlock { Text = $"단계별 확대 최대 배율  {draft.MaxZoomPercent}%", Margin = new Thickness(0, 8, 0, 8) };
        panel.Children.Add(maxZoomLabel);
        var maxZoom = new Slider { Minimum = 100, Maximum = 600, TickFrequency = 25, IsSnapToTickEnabled = true, Value = draft.MaxZoomPercent };
        maxZoom.ValueChanged += (_, _) => { draft.MaxZoomPercent = (int)Math.Round(maxZoom.Value); maxZoomLabel.Text = $"단계별 확대 최대 배율  {draft.MaxZoomPercent}%"; };
        panel.Children.Add(maxZoom);
        panel.Children.Add(new TextBlock { Text = "100~600% · 기본 300% · 단계별 확대에 적용 · 토글 배율은 별도", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 8, 0, 18) });
        panel = AddSection(sections, "자동 사라지기");
        var autoFade = new CheckBox { Content = "자동 사라지기 사용", IsChecked = draft.AutoFadeEnabled, Foreground = Foreground, Margin = new Thickness(0, 0, 0, 12) };
        autoFade.Checked += (_, _) => draft.AutoFadeEnabled = true;
        autoFade.Unchecked += (_, _) => draft.AutoFadeEnabled = false;
        panel.Children.Add(autoFade);
        var holdLabel = new TextBlock { Text = $"완성 후 유지 시간  {draft.AutoFadeHoldSeconds:0.0}초", Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(holdLabel);
        var fadeHold = new Slider { Minimum = 0, Maximum = 60, TickFrequency = 0.5, IsSnapToTickEnabled = true, Value = draft.AutoFadeHoldSeconds };
        fadeHold.ValueChanged += (_, _) => { draft.AutoFadeHoldSeconds = fadeHold.Value; holdLabel.Text = $"완성 후 유지 시간  {fadeHold.Value:0.0}초"; };
        panel.Children.Add(fadeHold);
        var fadeLabel = new TextBlock { Text = $"서서히 사라지는 시간  {draft.AutoFadeDurationSeconds:0.0}초", Margin = new Thickness(0, 12, 0, 8) };
        panel.Children.Add(fadeLabel);
        var fadeDuration = new Slider { Minimum = 0.1, Maximum = 10, TickFrequency = 0.1, IsSnapToTickEnabled = true, Value = draft.AutoFadeDurationSeconds };
        fadeDuration.ValueChanged += (_, _) => { draft.AutoFadeDurationSeconds = Math.Round(fadeDuration.Value, 1); fadeLabel.Text = $"서서히 사라지는 시간  {fadeDuration.Value:0.0}초"; };
        panel.Children.Add(fadeDuration);
        panel.Children.Add(new TextBlock { Text = "기본: 사용 안 함 · 유지 3초 + 페이드 1.5초. 새로 완성하는 선·도형부터 적용됩니다. 각 획은 독립적으로 사라지며, 완전히 사라진 획은 실행 취소/다시 실행으로 복원되지 않습니다.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 8, 0, 0) });
        panel = AddSection(sections, "순환색");
        var speedLabel = new TextBlock { Text = $"색 변화 속도  {draft.ColorCycleSpeed:0.0}배", Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(speedLabel);
        var speed = new Slider { Minimum = 0.1, Maximum = 10, TickFrequency = 0.1, IsSnapToTickEnabled = true,
            Value = draft.ColorCycleSpeed, Margin = new Thickness(0, 0, 0, 8) };
        speed.ValueChanged += (_, _) => { draft.ColorCycleSpeed = Math.Round(speed.Value, 1); speedLabel.Text = $"색 변화 속도  {draft.ColorCycleSpeed:0.0}배"; };
        panel.Children.Add(speed);
        panel.Children.Add(new TextBlock { Text = "느리게 0.1배 ← → 빠르게 10.0배\n기본 2.0배 · 기존 속도 1.0배 · 새로 그리는 획에 적용", FontSize = 12, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(ToolbarWindow.Button("기본 속도 복원 (2.0배)", () => speed.Value = 2));
        panel = AddSection(sections, "화이트보드 · 사용자 이미지");
        panel.Children.Add(new TextBlock { Text = "굵기 아래에서 배경을 선택합니다. 배경을 바꿔도 필기는 유지됩니다. 화면 조작 모드에서는 배경을 잠시 숨깁니다.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var imagePath = new TextBox { Text = draft.BoardImagePath, IsReadOnly = true, Padding = new Thickness(8), TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(imagePath);
        panel.Children.Add(ToolbarWindow.Button("이미지 파일 선택…", () =>
        {
            var picker = new Microsoft.Win32.OpenFileDialog { Filter = "이미지|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff", CheckFileExists = true };
            if (picker.ShowDialog(this) != true) return;
            var candidate = draft.Clone(); candidate.BoardImagePath = picker.FileName; candidate.BoardBackground = BoardBackground.CustomImage;
            if (!Whiteboard.TryLoad(candidate, out _, out var problem)) { error.Text = problem; return; }
            draft.BoardImagePath = picker.FileName; imagePath.Text = picker.FileName; error.Text = "";
        }));
        panel.Children.Add(new TextBlock { Text = "저장 후 ‘사용자 이미지’를 선택하세요. 화면을 채우도록 비율을 유지하며 가장자리가 일부 잘릴 수 있습니다. 다른 컴퓨터에서는 이미지 파일도 함께 옮겨 다시 지정하세요.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        panel = AddSection(sections, "화면 저장 · PNG");
        var defaultName = new CheckBox { Content = "기본 파일명 사용 (screenbrush_년월일시분초.png)", IsChecked = draft.UseDefaultCaptureName, Foreground = Foreground, Margin = new Thickness(0, 0, 0, 12) };
        defaultName.Checked += (_, _) => draft.UseDefaultCaptureName = true;
        defaultName.Unchecked += (_, _) => draft.UseDefaultCaptureName = false;
        panel.Children.Add(defaultName);
        var capturePath = new TextBox { Text = draft.CaptureDirectory, IsReadOnly = true, Padding = new Thickness(8), TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(capturePath);
        panel.Children.Add(ToolbarWindow.Button("저장 폴더 선택…", () =>
        {
            var picker = new Microsoft.Win32.OpenFolderDialog { Title = "PNG 저장 폴더", InitialDirectory = draft.CaptureDirectory };
            if (picker.ShowDialog(this) == true) { draft.CaptureDirectory = picker.FolderName; capturePath.Text = picker.FolderName; }
        }));
        panel.Children.Add(new TextBlock { Text = "마우스가 있는 모니터의 화면과 필기를 PNG로 저장합니다. 기본 파일명을 끄면 저장할 때 이름을 지정합니다. 같은 이름은 번호를 붙여 보존합니다.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        panel = AddSection(sections, "단축키");
        panel.Children.Add(new TextBlock { Text = "그리기 모드 전환 키는 항상, 다른 단축키는 그리기 모드에서만 사용합니다. 체크된 단축키만 등록합니다(기본: 모두 켜짐). 해제해도 버튼·컨트롤은 사용할 수 있습니다. 입력 즉시 중복·등록 충돌을 확인합니다. 충돌한 키를 변경하거나 체크를 해제하면 저장할 수 있습니다.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        var boxes = new Dictionary<ActionId, TextBox>();
        foreach (ActionId action in Enum.GetValues<ActionId>().OrderBy(action => action >= ActionId.ToggleBoard && action <= ActionId.BoardImage ? 2 : Settings.Palette.Any(color => color.Action == action) ? 1 : 0))
        {
            if (action == ActionId.ToggleBoard) panel = AddSection(sections, "화이트보드 단축키");
            if (action == ActionId.ColorBlue)
            {
                panel = AddSection(sections, "색상 단축키");
                panel.Children.Add(new TextBlock { Text = "팔레트 순서대로 Ctrl + Alt + 1~7입니다. 각 입력 칸에서 변경할 수 있습니다.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
            }
            var row = new Grid { Margin = new Thickness(0, 7, 0, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(155) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            var enabled = new CheckBox { Content = new TextBlock { Text = Settings.Label(action), TextWrapping = TextWrapping.Wrap }, IsChecked = draft.IsShortcutEnabled(action), Foreground = Foreground, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), ToolTip = "해제하면 이 단축키만 사용하지 않습니다. 버튼·컨트롤은 계속 사용할 수 있습니다." };
            row.Children.Add(enabled);
            var box = new TextBox { Text = draft.Shortcuts[action].ToString(), IsReadOnly = true, Padding = new Thickness(8), CaretBrush = System.Windows.Media.Brushes.Transparent };
            box.IsEnabled = draft.IsShortcutEnabled(action);
            enabled.Checked += (_, _) => { draft.EnabledActions[action] = true; box.IsEnabled = true; CheckShortcuts(); };
            enabled.Unchecked += (_, _) => { draft.EnabledActions[action] = false; box.IsEnabled = false; CheckShortcuts(); };
            box.PreviewKeyDown += (_, e) =>
            {
                e.Handled = true;
                Key key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (Settings.IsModifier(key)) return;
                draft.Shortcuts[action] = new Shortcut(key, Keyboard.Modifiers); box.Text = draft.Shortcuts[action].ToString();
                CheckShortcuts();
                box.ToolTip = string.IsNullOrEmpty(error.Text) ? "사용 가능한 단축키입니다." : error.Text;
            };
            boxes[action] = box; Grid.SetColumn(box, 1); row.Children.Add(box); panel.Children.Add(row);
        }
        panel = AddSection(sections, "일시 화면 조작 / 단축키 복원");
        var holdEnabled = new CheckBox { Content = "누르는 동안 화면 조작 사용", IsChecked = draft.HoldToInteractEnabled, Foreground = Foreground, Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(holdEnabled);
        panel.Children.Add(new TextBlock { Text = "누르는 동안 아래 화면 조작", Margin = new Thickness(0, 18, 0, 8) });
        var hold = new ComboBox { ItemsSource = new[] { Key.LeftCtrl, Key.RightCtrl, Key.LeftAlt, Key.RightAlt, Key.LeftShift, Key.RightShift, Key.Space }, SelectedItem = draft.HoldKey, IsEnabled = draft.HoldToInteractEnabled, Padding = new Thickness(8) };
        holdEnabled.Checked += (_, _) => { draft.HoldToInteractEnabled = true; hold.IsEnabled = true; };
        holdEnabled.Unchecked += (_, _) => { draft.HoldToInteractEnabled = false; hold.IsEnabled = false; };
        panel.Children.Add(new TextBlock { Text = "기본: 켜짐. 끄면 지정 키를 눌러도 그리기를 유지합니다. 일시 화면 조작 전환 단축키로도 켜고 끌 수 있습니다.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 8) });
        hold.SelectionChanged += (_, _) => { if (hold.SelectedItem is Key key) { draft.HoldKey = key; CheckShortcuts(); } }; panel.Children.Add(hold);
        panel.Children.Add(new TextBlock { Text = "기능 단축키는 다른 앱에서도 동작합니다.\n일시 조작 키는 아래 프로그램에도 전달됩니다.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12), FontSize = 12 });
        panel.Children.Add(ToolbarWindow.Button("기본 단축키 복원", () => { draft.Shortcuts = Settings.Defaults(); draft.HoldKey = Key.LeftCtrl; hold.SelectedItem = draft.HoldKey; foreach (var pair in boxes) pair.Value.Text = draft.Shortcuts[pair.Key].ToString(); CheckShortcuts(); }));
        foreach (Border section in sections.Children)
        foreach (UIElement element in ((StackPanel)section.Child).Children)
        {
            if (element is TextBlock text && text.FontSize == 12)
            {
                text.Foreground = ToolbarWindow.Brush("#B4C0D6");
                text.TextWrapping = TextWrapping.Wrap; text.LineHeight = 19;
            }
            if (element is Slider slider) slider.MinHeight = 26;
        }
    }
    private static StackPanel AddSection(StackPanel sections, string title)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold,
            Foreground = ToolbarWindow.Brush("#D7E4FF"), Margin = new Thickness(0, 0, 0, 20) });
        sections.Children.Add(new Border { Child = content, Padding = new Thickness(20), Margin = new Thickness(0, 0, 0, 20),
            Background = ToolbarWindow.Brush("#232B3D"), BorderBrush = ToolbarWindow.Brush("#354159"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10) });
        return content;
    }
}
