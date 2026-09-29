using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;

namespace ScreenBrush;

// Append new values so previously saved numeric tool identifiers remain valid.
public enum Tool { Ballpoint, Pencil, Line, Arrow, Rectangle, Ellipse, Eraser, Marker }
public enum DrawingMode { Freehand, Line, Arrow, Rectangle, Ellipse }
public enum ActionId { Ballpoint = 1, Pencil, Line, Arrow, Rectangle, Ellipse, Eraser, ZoomIn, ZoomOut, ZoomReset, Undo, Clear, Marker, CycleColor, Quit, Redo, ColorBlue, ColorRed, ColorOrange, ColorGreen, ColorPurple, ColorBlack, ColorWhite, ToggleToolbar, ToggleBoard, BoardWhite, BoardBlack, BoardColor, BoardChalk, BoardNotebook, BoardDots, BoardImage, SaveScreen = 34, Freehand, ToggleAutoFade, ToggleDrawing, ToggleZoom, ToggleHoldInteraction }

public sealed record ToolColor(string Color, bool CycleColors);

public sealed record Shortcut(Key Key, ModifierKeys Modifiers)
{
    public override string ToString() => (Modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl + " : "") +
        (Modifiers.HasFlag(ModifierKeys.Alt) ? "Alt + " : "") +
        (Modifiers.HasFlag(ModifierKeys.Shift) ? "Shift + " : "") +
        (Modifiers.HasFlag(ModifierKeys.Windows) ? "Win + " : "") + Key;
}

public sealed class Settings
{
    internal static readonly (ActionId Action, string Name, string Hex)[] Palette =
    {
        (ActionId.ColorBlue, "파랑", "#FF2563EB"), (ActionId.ColorRed, "빨강", "#FFEF4444"),
        (ActionId.ColorOrange, "주황", "#FFF59E0B"), (ActionId.ColorGreen, "초록", "#FF22C55E"),
        (ActionId.ColorPurple, "보라", "#FFAB72F5"), (ActionId.ColorBlack, "검정", "#FF20242D"),
        (ActionId.ColorWhite, "흰색", "#FFFFFFFF")
    };
    public Dictionary<ActionId, Shortcut> Shortcuts { get; set; } = Defaults();
    public Dictionary<ActionId, bool> EnabledActions { get; set; } = new();
    internal bool IsShortcutEnabled(ActionId action) => Enum.IsDefined(action) && (!EnabledActions.TryGetValue(action, out bool enabled) || enabled);
    internal Dictionary<ActionId, Shortcut> ActiveShortcuts(bool drawing = true) => Shortcuts.Where(pair => IsShortcutEnabled(pair.Key) && (drawing || pair.Key == ActionId.ToggleDrawing)).ToDictionary(pair => pair.Key, pair => pair.Value);
    public bool HoldToInteractEnabled { get; set; } = true;
    public Key HoldKey { get; set; } = Key.LeftCtrl;
    public string Color { get; set; } = "#FF2563EB";
    public bool CycleColors { get; set; } = true;
    public Dictionary<Tool, ToolColor> ToolColors { get; set; } = new();
    public Dictionary<Tool, ToolColor> BoardToolColors { get; set; } = new();
    private Dictionary<Tool, ToolColor> ActiveToolColors => WhiteboardEnabled ? BoardToolColors : ToolColors;
    internal void InitializeToolColors()
    {
        foreach (var tool in Enum.GetValues<Tool>())
        {
            ToolColors.TryAdd(tool, new ToolColor(Color, CycleColors));
            // Seed both modes with existing preferences when upgrading.
            BoardToolColors.TryAdd(tool, ToolColors[tool]);
        }
    }
    internal void RememberToolColor() => ActiveToolColors[Tool] = new ToolColor(Color, CycleColors);
    internal void RestoreToolColor()
    {
        var saved = ActiveToolColors[Tool];
        Color = saved.Color; CycleColors = saved.CycleColors;
    }
    public double ColorCycleSpeed { get; set; } = 2;
    public int ToggleZoomPercent { get; set; } = 300;
    public int ZoomStepPercent { get; set; } = 10;
    public int MaxZoomPercent { get; set; } = 300;
    internal double ClampZoom(double value) => Math.Clamp(value, 1, MaxZoomPercent / 100.0);
    public bool UseDefaultCaptureName { get; set; } = true;
    public string CaptureDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) is { Length: > 0 } pictures ? pictures : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures"), "ScreenBrush");
    public double Width { get; set; } = 9;
    public double InkOpacity { get; set; } = 0.9;
    public bool AutoFadeEnabled { get; set; }
    public double AutoFadeHoldSeconds { get; set; } = 3;
    public double AutoFadeDurationSeconds { get; set; } = 1.5;
    public bool ShowToolbarOnStartup { get; set; }
    public bool RememberToolbarPosition { get; set; } = true;
    public int? ToolbarX { get; set; }
    public int? ToolbarY { get; set; }
    public bool PreserveSessionOnEscape { get; set; }
    public bool WhiteboardEnabled { get; set; }
    public BoardBackground BoardBackground { get; set; } = BoardBackground.White;
    public string BoardColor { get; set; } = "#FFF4EAD5";
    public string BoardImagePath { get; set; } = "";
    public Tool Tool { get; set; } = Tool.Marker;
    public Tool BrushTool { get; set; } = Tool.Marker;
    public DrawingMode DrawingMode { get; set; }
    internal Tool EffectiveTool => Tool == Tool.Eraser ? Tool.Eraser : DrawingMode == DrawingMode.Freehand ? BrushTool : Enum.Parse<Tool>(DrawingMode.ToString());
    internal void InitializeDrawingSelection()
    {
        if (Tool is Tool.Marker or Tool.Ballpoint or Tool.Pencil) BrushTool = Tool;
        else if (Tool is Tool.Line or Tool.Arrow or Tool.Rectangle or Tool.Ellipse)
            DrawingMode = Enum.Parse<DrawingMode>(Tool.ToString());
    }
    public static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenBrush", "settings.json");
    public static Dictionary<ActionId, Shortcut> Defaults() => new()
    {
        [ActionId.ToggleHoldInteraction] = new(Key.H, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift),
        [ActionId.ToggleDrawing] = new(Key.F2, ModifierKeys.Control | ModifierKeys.Shift),
        [ActionId.ToggleZoom] = new(Key.F3, ModifierKeys.Control | ModifierKeys.Shift),
        [ActionId.ToggleAutoFade] = new(Key.A, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift),
        [ActionId.Freehand] = new(Key.F, ModifierKeys.Control | ModifierKeys.Shift),
        [ActionId.SaveScreen] = new(Key.S, ModifierKeys.Control | ModifierKeys.Shift),
        [ActionId.Ballpoint] = new(Key.D1, ModifierKeys.Control | ModifierKeys.Shift),
        [ActionId.Pencil] = new(Key.D2, ModifierKeys.Control | ModifierKeys.Shift),
        [ActionId.Line] = new(Key.D3, ModifierKeys.Control | ModifierKeys.Shift),
        [ActionId.Arrow] = new(Key.D4, ModifierKeys.Control | ModifierKeys.Shift),
        [ActionId.Rectangle] = new(Key.D5, ModifierKeys.Control | ModifierKeys.Shift),
        [ActionId.Ellipse] = new(Key.D6, ModifierKeys.Control | ModifierKeys.Shift),
        [ActionId.Eraser] = new(Key.D7, ModifierKeys.Control | ModifierKeys.Shift),
        [ActionId.ZoomIn] = new(Key.Up, ModifierKeys.Control | ModifierKeys.Shift),
        [ActionId.ZoomOut] = new(Key.Down, ModifierKeys.Control | ModifierKeys.Shift),
        [ActionId.ZoomReset] = new(Key.D0, ModifierKeys.Control | ModifierKeys.Shift),
        [ActionId.Undo] = new(Key.Z, ModifierKeys.Control),
        [ActionId.Redo] = new(Key.Y, ModifierKeys.Control),
        [ActionId.ColorBlue] = new(Key.D1, ModifierKeys.Control | ModifierKeys.Alt),
        [ActionId.ColorRed] = new(Key.D2, ModifierKeys.Control | ModifierKeys.Alt),
        [ActionId.ColorOrange] = new(Key.D3, ModifierKeys.Control | ModifierKeys.Alt),
        [ActionId.ColorGreen] = new(Key.D4, ModifierKeys.Control | ModifierKeys.Alt),
        [ActionId.ColorPurple] = new(Key.D5, ModifierKeys.Control | ModifierKeys.Alt),
        [ActionId.ColorBlack] = new(Key.D6, ModifierKeys.Control | ModifierKeys.Alt),
        [ActionId.ColorWhite] = new(Key.D7, ModifierKeys.Control | ModifierKeys.Alt),
        [ActionId.Clear] = new(Key.Delete, ModifierKeys.Control | ModifierKeys.Shift),
        [ActionId.Marker] = new(Key.D8, ModifierKeys.Control | ModifierKeys.Shift),
        [ActionId.CycleColor] = new(Key.D9, ModifierKeys.Control | ModifierKeys.Shift),
        [ActionId.Quit] = new(Key.F4, ModifierKeys.Alt),
        [ActionId.ToggleBoard] = new(Key.B, ModifierKeys.Control | ModifierKeys.Alt),
        [ActionId.BoardWhite] = new(Key.D1, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift),
        [ActionId.BoardBlack] = new(Key.D2, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift),
        [ActionId.BoardColor] = new(Key.D3, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift),
        [ActionId.BoardChalk] = new(Key.D4, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift),
        [ActionId.BoardNotebook] = new(Key.D5, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift),
        [ActionId.BoardDots] = new(Key.D6, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift),
        [ActionId.BoardImage] = new(Key.D7, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift),
        [ActionId.ToggleToolbar] = new(Key.F1, ModifierKeys.Control | ModifierKeys.Shift)
    };
    public static string Label(ActionId action) => action switch
    {
        ActionId.ToggleHoldInteraction => "일시 화면 조작 켜기 / 끄기",
        ActionId.ToggleDrawing => "그리기 모드 켜기 / 해제",
        ActionId.ToggleZoom => "확대 / 원래 배율 토글",
        ActionId.ToggleAutoFade => "자동 사라지기 켜기 / 끄기",
        ActionId.Freehand => "자유 필기",
        ActionId.SaveScreen => "화면 저장 (PNG)",
        ActionId.ToggleBoard => "화이트보드 켜기 / 끄기",
        ActionId.BoardWhite => "배경 흰색", ActionId.BoardBlack => "배경 검정", ActionId.BoardColor => "배경 사용자 색",
        ActionId.BoardChalk => "배경 1 칠판", ActionId.BoardNotebook => "배경 2 노트", ActionId.BoardDots => "배경 3 도트", ActionId.BoardImage => "배경 사용자 이미지",
        ActionId.ToggleToolbar => "도구 모음 표시 / 숨기기",
        ActionId.ColorBlue => "파랑", ActionId.ColorRed => "빨강", ActionId.ColorOrange => "주황", ActionId.ColorGreen => "초록",
        ActionId.ColorPurple => "보라", ActionId.ColorBlack => "검정", ActionId.ColorWhite => "흰색",
        ActionId.Ballpoint => "볼펜", ActionId.Pencil => "연필",
        ActionId.Line => "직선", ActionId.Arrow => "화살표", ActionId.Rectangle => "사각형", ActionId.Ellipse => "원 / 타원",
        ActionId.Eraser => "지우개", ActionId.ZoomIn => "확대", ActionId.ZoomOut => "축소", ActionId.ZoomReset => "원래 배율",
        ActionId.Undo => "실행 취소", ActionId.Redo => "다시 실행", ActionId.Marker => "싸인펜", ActionId.CycleColor => "순환색 켜기 / 끄기", ActionId.Quit => "앱 종료", _ => "모두 지우기"
    };
    public static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
    public string? Validate()
    {
        if (!double.IsFinite(AutoFadeHoldSeconds) || AutoFadeHoldSeconds < 0 || AutoFadeHoldSeconds > 60 || !double.IsFinite(AutoFadeDurationSeconds) || AutoFadeDurationSeconds < 0.1 || AutoFadeDurationSeconds > 10) return "자동 사라지기 유지 시간은 0~60초, 페이드 시간은 0.1~10초로 지정하세요.";
        if (EnabledActions == null || EnabledActions.Keys.Any(action => !Enum.IsDefined(action))) return "단축키 활성화 설정이 올바르지 않습니다.";
        if (BrushTool is not (Tool.Marker or Tool.Ballpoint or Tool.Pencil) || !Enum.IsDefined(DrawingMode)) return "필기 도구와 그리기 방식 설정이 올바르지 않습니다.";
        if (string.IsNullOrWhiteSpace(CaptureDirectory) || !Path.IsPathFullyQualified(CaptureDirectory) || CaptureDirectory.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return "저장 폴더를 올바른 절대 경로로 지정하세요.";
        if (ToolColors == null || BoardToolColors == null || ToolColors.Concat(BoardToolColors).Any(pair => !Enum.IsDefined(pair.Key) || pair.Value == null))
            return "도구별 색상 설정이 올바르지 않습니다.";
        try
        {
            if (ToolColors.Values.Concat(BoardToolColors.Values).Any(value => System.Windows.Media.ColorConverter.ConvertFromString(value.Color) is not System.Windows.Media.Color))
                return "도구별 색상 설정이 올바르지 않습니다.";
        }
        catch { return "도구별 색상 설정이 올바르지 않습니다."; }
        if (!Enum.IsDefined(BoardBackground)) return "화이트보드 배경을 선택하세요.";
        try { if (System.Windows.Media.ColorConverter.ConvertFromString(BoardColor) is not System.Windows.Media.Color) return "배경색이 올바르지 않습니다."; }
        catch { return "배경색이 올바르지 않습니다."; }
        if (!double.IsFinite(InkOpacity) || InkOpacity < 0 || InkOpacity > 1) return "잉크 불투명도는 0~100% 사이로 지정하세요.";
        if (ToggleZoomPercent < 150 || ToggleZoomPercent > 500) return "확대 토글 배율은 150~500%로 지정하세요.";
        if (MaxZoomPercent < 100 || MaxZoomPercent > 600) return "최대 확대 배율은 100~600% 사이로 지정하세요.";
        if (ZoomStepPercent < 5 || ZoomStepPercent > 100) return "확대·축소 간격은 5~100% 사이로 지정하세요.";
        if (Shortcuts == null || Enum.GetValues<ActionId>().Any(a => !Shortcuts.ContainsKey(a))) return "빠진 단축키가 있습니다.";
        if (Shortcuts.Count != Enum.GetValues<ActionId>().Length) return "알 수 없는 단축키가 있습니다.";
        if (Shortcuts.Values.Any(s => s == null || s.Key == Key.None || !Enum.IsDefined(s.Key) || IsModifier(s.Key) || (s.Modifiers & ~(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows)) != 0)) return "유효한 키 조합을 입력하세요.";
        if (Shortcuts.Values.Any(s => s.Modifiers == ModifierKeys.None && s.Key != Key.Escape && (s.Key < Key.F1 || s.Key > Key.F24))) return "문자 키에는 Ctrl, Alt, Shift 또는 Win을 함께 지정하세요.";
        if (Shortcuts.Values.Any(s => s.Key == Key.Escape && s.Modifiers == ModifierKeys.None)) return "Esc는 화면 조작으로 돌아가는 고정 키입니다.";
        if (ActiveShortcuts().Values.Distinct().Count() != ActiveShortcuts().Count) return "중복된 단축키가 있습니다.";
        if (!IsModifier(HoldKey) && HoldKey != Key.Space) return "일시 조작 키는 보조 키 또는 Space를 선택하세요.";
        if (Shortcuts.Values.Any(s => s.Key == HoldKey)) return "일시 조작 키와 기능 키가 겹칩니다.";
        if (!double.IsFinite(Width) || Width < 0.5 || Width > 24 || !Enum.IsDefined(Tool)) return "잘못된 도구 설정입니다.";
        if (!double.IsFinite(ColorCycleSpeed) || ColorCycleSpeed < 0.1 || ColorCycleSpeed > 10) return "색 변화 속도는 0.1~10.0배 사이로 지정하세요.";
        try { if (System.Windows.Media.ColorConverter.ConvertFromString(Color) is not System.Windows.Media.Color) return "잘못된 색상입니다."; } catch { return "잘못된 색상입니다."; }
        return null;
    }
    public Settings Clone() => JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(this))!;
    internal void AddNewShortcuts()
    {
        // Retired text-tool IDs: preserve all other settings from that version.
        ToolColors?.Remove((Tool)8);
        if ((int)Tool == 8) Tool = Tool.Marker;
        if (Shortcuts == null) return;
        Shortcuts.Remove((ActionId)33);
        Shortcuts.Remove((ActionId)0); // Retired drawing-mode toggle.
        foreach (var action in Shortcuts.Where(pair => pair.Value == new Shortcut(Key.Escape, ModifierKeys.None)).Select(pair => pair.Key).ToArray())
        {
            if (!Defaults().TryGetValue(action, out var replacement)) continue;
            if (Shortcuts.ContainsValue(replacement))
                replacement = Enumerable.Range((int)Key.F1, 24)
                    .Select(k => new Shortcut((Key)k, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift))
                    .First(s => !Shortcuts.ContainsValue(s));
            Shortcuts[action] = replacement;
        }
        if (Shortcuts.TryGetValue(ActionId.Undo, out var undo) && undo == new Shortcut(Key.Z, ModifierKeys.Control | ModifierKeys.Shift) && !Shortcuts.ContainsValue(Defaults()[ActionId.Undo]))
            Shortcuts[ActionId.Undo] = Defaults()[ActionId.Undo];
        // Migrate the original auto-fade default, which conflicts with another app.
        if (Shortcuts.TryGetValue(ActionId.ToggleAutoFade, out var fadeKey) &&
            fadeKey == new Shortcut(Key.A, ModifierKeys.Control | ModifierKeys.Shift))
        {
            var replacement = Defaults()[ActionId.ToggleAutoFade];
            if (Shortcuts.ContainsValue(replacement))
                replacement = Enumerable.Range((int)Key.F1, 24)
                    .Select(k => new Shortcut((Key)k, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift))
                    .First(s => !Shortcuts.ContainsValue(s));
            Shortcuts[ActionId.ToggleAutoFade] = replacement;
        }
        foreach (var action in new[] { ActionId.Marker, ActionId.CycleColor, ActionId.Quit, ActionId.Redo, ActionId.ToggleToolbar, ActionId.ToggleBoard, ActionId.SaveScreen, ActionId.Freehand, ActionId.ToggleAutoFade, ActionId.ToggleDrawing, ActionId.ToggleZoom, ActionId.ToggleHoldInteraction }.Concat(Whiteboard.Palette.Select(item => item.Action)).Concat(Palette.Select(color => color.Action)))
        {
            if (Shortcuts.ContainsKey(action)) continue;
            var binding = Defaults()[action];
            if (Shortcuts.ContainsValue(binding))
                binding = Enumerable.Range((int)Key.F1, 24)
                    .Select(k => new Shortcut((Key)k, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift))
                    .First(s => !Shortcuts.ContainsValue(s));
            Shortcuts[action] = binding;
        }
    }
    internal static Settings ReadSettings(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException();
        // Dictionary enum keys are serialized as names. Remove retired keys BEFORE
        // deserialization; their enum values no longer exist in the current model.
        foreach (string map in new[] { "Shortcuts", "EnabledActions" })
        if (root[map] is JsonObject shortcuts)
            foreach (string key in shortcuts.Select(pair => pair.Key).Where(key =>
                key.Equals("Toggle", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Text", StringComparison.OrdinalIgnoreCase) || key is "0" or "33").ToArray())
                shortcuts.Remove(key);
        foreach (string name in new[] { "ToolColors", "BoardToolColors" })
            if (root[name] is JsonObject colors)
                foreach (string key in colors.Select(pair => pair.Key).Where(key =>
                    key.Equals("Text", StringComparison.OrdinalIgnoreCase) || key == "8").ToArray())
                    colors.Remove(key);
        var settings = root.Deserialize<Settings>() ?? throw new InvalidDataException();
        settings.AddNewShortcuts();
        settings.WhiteboardEnabled = false; // Never reactivate a board from a previous run.
        if (settings.Validate() is string error) throw new InvalidDataException(error);
        return settings;
    }
    public static Settings Load(out string? warning, string? path = null)
    {
        warning = null;
        try
        {
            path ??= FilePath;
            if (!File.Exists(path)) return new();
            return ReadSettings(File.ReadAllText(path));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or System.Security.SecurityException)
        { warning = "설정을 읽지 못해 기본값을 사용합니다: " + e.Message; return new(); }
    }
    public void Save(string? path = null)
    {
        if (Validate() is string error) throw new InvalidDataException(error);
        path ??= FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".tmp";
        var saved = JsonSerializer.SerializeToNode(this)!;
        saved[nameof(WhiteboardEnabled)] = false;
        File.WriteAllText(temp, saved.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }
}
