using ScreenBrush;
using System;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class Program
{
    private static int count;
    private static void Check(bool result, string message) { if (!result) throw new Exception(message); count++; }
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--review-render")) { ReviewChecks.Render(args.Last()); return; }
        if (args.Contains("--review-zoom")) { ReviewChecks.RunZoom(); return; }
        if (args.Contains("--settings-load"))
        {
            var actualSettings = Settings.Load(out var warning);
            if (warning != null) throw new Exception(warning);
            if (actualSettings.Validate() != null) throw new Exception(actualSettings.Validate());
            Console.WriteLine("PASS: existing user settings loaded and validated without fallback (read-only).");
            return;
        }
        if (args.Contains("--error-paths")) { ErrorPathChecks.Run(); return; }
        if (args.Contains("--auto-fade")) { AutoFadeChecks.Run(); return; }
        if (args.Contains("--zoom-interaction")) { ZoomInteractionChecks.Run(); return; }
        if (args.Contains("--mode-shortcuts")) { ModeShortcutChecks.Run(); return; }
        if (args.Contains("--disabled-actions")) { DisabledActionChecks.Run(); return; }
        if (args.Contains("--escape")) { EscapeChecks.Run(); return; }
        if (args.Contains("--screen-save")) { ScreenSaveChecks.Run(); return; }
        if (args.Contains("--board-performance")) { BoardPerformanceChecks.Run(args.Last()); return; }
        if (args.Contains("--alt-f4")) { DesktopChecks.AltF4(); return; }
        if (args.Contains("--toggle-hotkeys")) { DesktopChecks.AltF4(false, true); return; }
        if (args.Contains("--settings-alt-f4")) { DesktopChecks.AltF4(true); return; }
        if (args.Contains("--resources")) { ResourceChecks.Run(args.Length > 1 ? args.Last() : "artifacts/resource-check.json"); return; }
        if (args.Contains("--startup-options")) { DesktopChecks.StartupOptions(); return; }
        if (args.Contains("--performance")) { PerformanceChecks.Run(); return; }
        if (args.Contains("--capture-performance")) { PerformanceChecks.RunCapture(); return; }
        if (args.Contains("--desktop")) { DesktopChecks.Run(); return; }
        if (args.Contains("--settings-preview")) { DesktopChecks.PreviewSettings(); return; }
        if (args.Contains("--whiteboard")) { WhiteboardChecks.Run(); return; }
        Check(ScreenSave.DefaultName(new DateTime(2026, 9, 25, 14, 30, 5)) == "screenbrush_20260925143005.png", "Capture filename includes prefix and full timestamp.");
        string captureTest = Path.Combine(Path.GetTempPath(), "ScreenBrush-save-" + Guid.NewGuid());
        try
        {
            var pixel = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgr32, null, new byte[] { 30, 20, 10, 0 }, 4);
            pixel.Freeze();
            string first = ScreenSave.Write(pixel, Path.Combine(captureTest, "test.jpg"));
            string second = ScreenSave.Write(pixel, Path.Combine(captureTest, "test.jpg"));
            Check(first.EndsWith("test.png") && second.EndsWith("test_1.png"), "PNG saving enforces extension and preserves duplicate files.");
            using var stream = File.OpenRead(first);
            var decoded = new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            byte[] actual = new byte[4]; decoded.Frames[0].CopyPixels(actual, 4, 0);
            Check(actual[0] == 30 && actual[1] == 20 && actual[2] == 10, "PNG preserves captured pixels.");
        }
        finally { if (Directory.Exists(captureTest)) Directory.Delete(captureTest, true); }
        var captureSettings = new Settings();
        captureSettings.Shortcuts.Remove(ActionId.SaveScreen);
        captureSettings.Shortcuts[ActionId.Pencil] = Settings.Defaults()[ActionId.SaveScreen];
        captureSettings.AddNewShortcuts();
        Check(captureSettings.Validate() == null && captureSettings.Shortcuts[ActionId.SaveScreen] != captureSettings.Shortcuts[ActionId.Pencil], "Capture shortcut migration preserves custom assignments.");
        var legacyFileSettings = new Settings { Width = 11, InkOpacity = .65, Color = "#FF123456", WhiteboardEnabled = true };
        legacyFileSettings.Shortcuts[ActionId.Marker] = new Shortcut(Key.F10, ModifierKeys.Alt);
        var legacyJson = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(legacyFileSettings))!;
        legacyJson["Shortcuts"]!["Toggle"] = new System.Text.Json.Nodes.JsonObject { ["Key"] = (int)Key.F2, ["Modifiers"] = 6 };
        legacyJson["Shortcuts"]!["Text"] = new System.Text.Json.Nodes.JsonObject { ["Key"] = (int)Key.T, ["Modifiers"] = 6 };
        legacyJson["ToolColors"]!["Text"] = new System.Text.Json.Nodes.JsonObject { ["Color"] = "#FFFF0000", ["CycleColors"] = false };
        var migratedJson = Settings.ReadSettings(legacyJson.ToJsonString());
        Check(migratedJson.Width == 11 && migratedJson.InkOpacity == .65 && migratedJson.Color == "#FF123456" && !migratedJson.WhiteboardEnabled, "Legacy JSON retains appearance without automatically enabling whiteboard.");
        Check(migratedJson.Shortcuts[ActionId.Marker] == legacyFileSettings.Shortcuts[ActionId.Marker] && migratedJson.Validate() == null, "Named retired actions are removed before deserialization without resetting customized shortcuts.");
        string migrationFile = Path.Combine(Path.GetTempPath(), "ScreenBrush-migration-" + Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(migrationFile, legacyJson.ToJsonString());
            var loadedLegacy = Settings.Load(out var warning, migrationFile);
            Check(warning == null && loadedLegacy.Width == 11, "Load migrates actual JSON file without fallback warning.");
            Check(File.ReadAllText(migrationFile) == legacyJson.ToJsonString(), "Loading does not modify the original file.");
            loadedLegacy.Save(migrationFile);
            var loadedAgain = Settings.Load(out warning, migrationFile);
            Check(warning == null && loadedAgain.Shortcuts[ActionId.Marker] == legacyFileSettings.Shortcuts[ActionId.Marker], "Migrated settings survive save and reload.");
        }
        finally { File.Delete(migrationFile); }
        string badSettingsPath = Path.Combine(Path.GetTempPath(), "ScreenBrush-invalid-" + Guid.NewGuid() + ".json");
        try
        {
            foreach (string malformedJson in new[] { "null", "[]", "{", "{\"Width\":-1}", "{\"Shortcuts\":null}", "{\"BoardToolColors\":null}", "{\"InkOpacity\":5}", "{\"Color\":\"invalid\"}" })
            {
                File.WriteAllText(badSettingsPath, malformedJson);
                var fallback = Settings.Load(out var loadWarning, badSettingsPath);
                Check(loadWarning != null && fallback.Validate() == null && File.ReadAllText(badSettingsPath) == malformedJson, "Malformed or invalid settings fall back without throwing or rewriting the file.");
            }
        }
        finally { File.Delete(badSettingsPath); }
        using (var invalidActions = new AppController(new Settings(), false))
        {
            invalidActions.Execute((ActionId)999);
            Check(invalidActions.Settings.Tool == Tool.Marker && !invalidActions.Drawing, "Unknown action IDs cannot select invalid tools.");
        }
        Check(!new Settings().PreserveSessionOnEscape && !Settings.ReadSettings("{}").PreserveSessionOnEscape, "Esc preservation defaults off for fresh and legacy settings.");
        Check(new Settings { PreserveSessionOnEscape = true }.Clone().PreserveSessionOnEscape, "Esc preservation survives serialization.");
        using (var independent = new AppController(new Settings(), false))
        {
            independent.SelectTool(Tool.Pencil); independent.SelectTool(Tool.Line);
            Check(independent.Settings.BrushTool == Tool.Pencil && independent.Settings.EffectiveTool == Tool.Line, "Pencil and line can be selected together.");
            independent.SelectTool(Tool.Ballpoint);
            Check(independent.Settings.DrawingMode == DrawingMode.Line && independent.Settings.BrushTool == Tool.Ballpoint, "Changing brush preserves shape selection.");
            independent.SelectTool(Tool.Rectangle);
            Check(independent.Settings.BrushTool == Tool.Ballpoint && independent.Settings.EffectiveTool == Tool.Rectangle, "Changing shape preserves brush selection.");
            independent.SelectTool(Tool.Eraser); independent.SelectTool(Tool.Marker);
            Check(independent.Settings.EffectiveTool == Tool.Rectangle, "Leaving eraser restores the independent shape.");
            using var restarted = new AppController(Settings.ReadSettings(System.Text.Json.JsonSerializer.Serialize(independent.Settings)), false);
            Check(restarted.Settings.DrawingMode == DrawingMode.Rectangle && restarted.Settings.BrushTool == Tool.Marker, "Independent selections survive JSON loading and restart.");
            independent.Execute(ActionId.Freehand);
            Check(independent.Settings.EffectiveTool == Tool.Marker && independent.Settings.DrawingMode == DrawingMode.Freehand, "Freehand shortcut preserves current brush.");
        }
        foreach (var shapeTool in new[] { Tool.Line, Tool.Arrow, Tool.Rectangle, Tool.Ellipse })
        {
            byte[]? markerPixels = null;
            foreach (var brushTool in new[] { Tool.Marker, Tool.Ballpoint, Tool.Pencil })
            {
                var shapeMark = new Mark { Tool = shapeTool, BrushTool = brushTool, Color = Colors.Blue, Width = 10, Start = new Point(20, 20), End = new Point(160, 80), FlowColors = true };
                var shapePixels = Render(shapeMark);
                Check(shapeMark.Drawing.IsFrozen && !shapeMark.Drawing.Bounds.IsEmpty && !shapeMark.Hit(new Point(900, 900), 2), "Each brush-shape combination renders immutable ink with valid erase bounds.");
                if (markerPixels == null) markerPixels = shapePixels;
                else Check(!markerPixels.SequenceEqual(shapePixels), "Pen and pencil shape appearance differs from marker.");
            }
        }
        var oldFreehand = new Settings(); oldFreehand.Shortcuts.Remove(ActionId.Freehand);
        oldFreehand.Shortcuts[ActionId.Pencil] = Settings.Defaults()[ActionId.Freehand]; oldFreehand.AddNewShortcuts();
        Check(oldFreehand.Validate() == null && oldFreehand.Shortcuts[ActionId.Freehand] != oldFreehand.Shortcuts[ActionId.Pencil], "Freehand shortcut migration preserves existing assignments.");
        Check(Enum.GetValues<ActionId>().All(new Settings().IsShortcutEnabled), "Every action defaults enabled.");
        Check(Enum.GetValues<ActionId>().All(Settings.ReadSettings("{}").IsShortcutEnabled), "Legacy settings enable every action.");
        foreach (var disabledAction in Enum.GetValues<ActionId>())
        {
            var config = new Settings(); config.EnabledActions[disabledAction] = false;
            Check(!config.ActiveShortcuts().ContainsKey(disabledAction) && config.Clone().EnabledActions[disabledAction] == false, "Disabled action is omitted from bindings and survives serialization.");
            using var disabledController = new AppController(config, false);
            string before = System.Text.Json.JsonSerializer.Serialize(disabledController.Settings);
            disabledController.ExecuteShortcut(disabledAction);
            Check(before == System.Text.Json.JsonSerializer.Serialize(disabledController.Settings) && !disabledController.Disposing, "Disabled shortcut cannot change state.");
        }
        var disabledDuplicate = new Settings();
        disabledDuplicate.Shortcuts[ActionId.Pencil] = disabledDuplicate.Shortcuts[ActionId.Marker];
        disabledDuplicate.EnabledActions[ActionId.Pencil] = false;
        Check(disabledDuplicate.Validate() == null, "Inactive shortcut does not conflict with an active shortcut.");
        disabledDuplicate.EnabledActions[ActionId.Pencil] = true;
        Check(disabledDuplicate.Validate() != null, "Reenabling conflicting shortcut is rejected.");
        using (var boardResume = new AppController(new Settings { PreserveSessionOnEscape = true, CycleColors = false }, false))
        {
            boardResume.SelectBoard(BoardBackground.White);
            boardResume.ChangeColor("#FF123456");
            boardResume.EnterScreenMode();
            boardResume.SelectTool(Tool.Marker);
            Check(!boardResume.Settings.WhiteboardEnabled, "Drawing after preserved Esc never enables whiteboard.");
            boardResume.ToggleBoard();
            Check(boardResume.Settings.Color == "#FF123456", "Explicit board entry restores its remembered ink color.");
            var resumedSettings = Settings.ReadSettings(System.Text.Json.JsonSerializer.Serialize(boardResume.Settings));
            Check(!resumedSettings.WhiteboardEnabled && resumedSettings.BoardToolColors[Tool.Marker].Color == "#FF123456", "Reload ignores board activation but retains board ink preferences.");
        }
        Check(Settings.ReadSettings("{}").HoldToInteractEnabled, "Temporary screen interaction defaults on for existing settings.");
        var holdOff = new Settings { HoldToInteractEnabled = false, HoldKey = Key.RightCtrl };
        Check(!holdOff.Clone().HoldToInteractEnabled && holdOff.Clone().HoldKey == Key.RightCtrl, "Temporary interaction use preference persists independently of the chosen key.");
        using (var holdController = new AppController(holdOff, false))
        {
            holdController.ExecuteShortcut(ActionId.ToggleHoldInteraction);
            Check(!holdController.Settings.HoldToInteractEnabled, "Temporary interaction toggle shortcut is inactive outside drawing mode.");
            holdController.ToggleDrawing(); holdController.ExecuteShortcut(ActionId.ToggleHoldInteraction);
            Check(holdController.Settings.HoldToInteractEnabled, "Temporary interaction toggle shortcut works while drawing.");
        }
        var fadeDefaults = Settings.ReadSettings("{}");
        Check(!fadeDefaults.AutoFadeEnabled && fadeDefaults.AutoFadeHoldSeconds == 3 && fadeDefaults.AutoFadeDurationSeconds == 1.5, "Auto fade defaults off with three second hold and 1.5 second fade.");
        Check(fadeDefaults.Shortcuts.ContainsKey(ActionId.ToggleAutoFade), "Auto fade shortcut migrates into legacy settings.");
        var oldFadeKey = new Settings();
        oldFadeKey.Shortcuts[ActionId.ToggleAutoFade] = new Shortcut(Key.A, ModifierKeys.Control | ModifierKeys.Shift);
        oldFadeKey.EnabledActions[ActionId.ToggleAutoFade] = false;
        oldFadeKey.AddNewShortcuts();
        Check(oldFadeKey.Shortcuts[ActionId.ToggleAutoFade] == new Shortcut(Key.A, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift) && !oldFadeKey.IsShortcutEnabled(ActionId.ToggleAutoFade), "Old fade default migrates without enabling a disabled shortcut.");
        oldFadeKey.Shortcuts[ActionId.ToggleAutoFade] = new Shortcut(Key.F10, ModifierKeys.Control | ModifierKeys.Alt);
        oldFadeKey.AddNewShortcuts();
        Check(oldFadeKey.Shortcuts[ActionId.ToggleAutoFade].Key == Key.F10, "Custom auto-fade shortcut is preserved.");
        oldFadeKey.Shortcuts[ActionId.ToggleAutoFade] = new Shortcut(Key.A, ModifierKeys.Control | ModifierKeys.Shift);
        oldFadeKey.Shortcuts[ActionId.Pencil] = Settings.Defaults()[ActionId.ToggleAutoFade];
        oldFadeKey.AddNewShortcuts();
        Check(oldFadeKey.Shortcuts[ActionId.ToggleAutoFade] != oldFadeKey.Shortcuts[ActionId.Pencil] && oldFadeKey.Validate() == null, "Migration avoids a replacement key already assigned inside ScreenBrush.");
        var invalidFade = fadeDefaults.Clone(); invalidFade.AutoFadeDurationSeconds = 0;
        Check(invalidFade.Validate() != null, "Zero fade duration is rejected.");
        using (var toggleFade = new AppController(fadeDefaults, false))
        {
            toggleFade.ToggleDrawing();
            toggleFade.ExecuteShortcut(ActionId.ToggleAutoFade);
            Check(toggleFade.Settings.AutoFadeEnabled, "Auto fade shortcut toggles on.");
            toggleFade.Settings.EnabledActions[ActionId.ToggleAutoFade] = false;
            toggleFade.ExecuteShortcut(ActionId.ToggleAutoFade);
            Check(toggleFade.Settings.AutoFadeEnabled, "Disabled fade shortcut cannot toggle setting.");
            toggleFade.Execute(ActionId.ToggleAutoFade);
            Check(!toggleFade.Settings.AutoFadeEnabled, "Direct fade command remains available with shortcut off.");
        }
        var legacyStartup = Settings.ReadSettings("{\"StartInDrawingMode\":true,\"WhiteboardEnabled\":true,\"Width\":11}");
        Check(legacyStartup.RememberToolbarPosition && legacyStartup.ToolbarX == null && legacyStartup.ToolbarY == null, "Legacy settings remember toolbar position by default without inventing a saved location.");
        var positionSettings = Settings.ReadSettings(System.Text.Json.JsonSerializer.Serialize(new Settings { RememberToolbarPosition = false, ToolbarX = -1000, ToolbarY = 80 }));
        Check(!positionSettings.RememberToolbarPosition && positionSettings.ToolbarX == -1000 && positionSettings.ToolbarY == 80, "Position and opt-out survive serialization, including negative monitor coordinates.");
        var workArea = new System.Drawing.Rectangle(-1920, 0, 1920, 1080);
        Check(ToolbarWindow.ClampPosition(-1200, 100, 300, 800, workArea) == new System.Drawing.Point(-1200, 100), "Valid secondary monitor position stays unchanged.");
        Check(ToolbarWindow.ClampPosition(9000, -9000, 300, 800, workArea) == new System.Drawing.Point(-300, 0), "Off-screen toolbar is clamped into the available monitor.");
        Check(ToolbarWindow.ClampPosition(0, 500, 300, 1400, workArea).Y == 0, "Oversized toolbar retains an accessible header.");
        using (var passiveStartup = new AppController(legacyStartup, false))
        {
            Check(!passiveStartup.Drawing && !passiveStartup.Settings.WhiteboardEnabled && passiveStartup.Settings.Width == 11, "Old automatic drawing and board startup flags no longer activate the canvas, other preferences survive.");
            Check(!System.Text.Json.JsonSerializer.Serialize(passiveStartup.Settings).Contains("StartInDrawingMode"), "Removed startup option is no longer serialized.");
        }
        var oldToggle = new Settings();
        oldToggle.Shortcuts[(ActionId)0] = new Shortcut(Key.F2, ModifierKeys.Control | ModifierKeys.Shift);
        oldToggle.Shortcuts[ActionId.Quit] = new Shortcut(Key.Escape, ModifierKeys.None);
        oldToggle.AddNewShortcuts();
        Check(!oldToggle.Shortcuts.ContainsKey((ActionId)0) && oldToggle.Validate() == null, "Removed mode toggle migrates and legacy Esc is freed.");
        oldToggle.Shortcuts[ActionId.Clear] = new Shortcut(Key.Escape, ModifierKeys.None);
        Check(oldToggle.Validate() != null, "Bare Esc is reserved for leaving drawing.");
        using (var quickTools = new AppController(new Settings(), false))
        {
            quickTools.SelectTool(Tool.Rectangle);
            Check(!quickTools.Drawing, "Selecting a shape does not enable drawing.");
            quickTools.ExecuteShortcut(ActionId.ToggleDrawing);
            Check(quickTools.Drawing, "Explicit mode shortcut enables drawing.");
            quickTools.EnterScreenMode(); quickTools.EnterScreenMode();
            Check(!quickTools.Drawing, "Leaving drawing is idempotent, never toggles it back on.");
            quickTools.SelectTool(Tool.Marker);
            Check(!quickTools.Drawing && quickTools.Settings.Tool == Tool.Marker, "Selecting a pen leaves drawing disabled.");
            foreach (var action in Enum.GetValues<ActionId>().Where(a => a != ActionId.ToggleDrawing))
            {
                string before = System.Text.Json.JsonSerializer.Serialize(quickTools.Settings);
                quickTools.ExecuteShortcut(action);
                Check(!quickTools.Drawing && !quickTools.Disposing && before == System.Text.Json.JsonSerializer.Serialize(quickTools.Settings), "Inactive mode ignores every non-mode shortcut.");
            }
            Check(quickTools.Settings.ActiveShortcuts(false).Keys.SequenceEqual(new[] { ActionId.ToggleDrawing }), "Inactive mode only registers its mode switch.");
            quickTools.ToggleDrawing();
            Check(quickTools.Settings.ActiveShortcuts(true).Count == Enum.GetValues<ActionId>().Length, "Drawing mode restores all enabled bindings.");
        }
        var settings = new Settings();
        var retiredToolSettings = new Settings { Tool = (Tool)8, Width = 11, InkOpacity = .7 };
        retiredToolSettings.Shortcuts[(ActionId)33] = new Shortcut(Key.T, ModifierKeys.Control | ModifierKeys.Shift);
        retiredToolSettings.ToolColors[(Tool)8] = new ToolColor("#FFFF0000", false);
        retiredToolSettings.AddNewShortcuts();
        Check(retiredToolSettings.Validate() == null && retiredToolSettings.Tool == Tool.Marker, "Removed text tool settings migrate without resetting preferences.");
        Check(retiredToolSettings.Width == 11 && retiredToolSettings.InkOpacity == .7 && !retiredToolSettings.ToolColors.ContainsKey((Tool)8), "Removal preserves unrelated user settings.");

        using (var perTool = new AppController(new Settings { Color = "#FF123456", CycleColors = false }, false))
        {
            Check(Enum.GetValues<Tool>().All(tool => perTool.Settings.ToolColors[tool] == new ToolColor("#FF123456", false)),
                "Legacy shared color initializes all tool colors without changing the preference.");
            perTool.ChangeColor("#FFAA1020"); // Marker
            perTool.Execute(ActionId.Pencil); perTool.Execute(ActionId.ColorGreen);
            perTool.Execute(ActionId.Rectangle); perTool.ToggleCycleColor();
            perTool.Execute(ActionId.Marker);
            Check(perTool.Settings.Color == "#FFAA1020" && !perTool.Settings.CycleColors, "Marker restores its custom solid color.");
            perTool.Execute(ActionId.Pencil);
            Check(perTool.Settings.Color == "#FF22C55E" && !perTool.Settings.CycleColors, "Pencil restores its shortcut-selected color.");
            perTool.Execute(ActionId.Rectangle);
            Check(perTool.Settings.CycleColors, "Rectangle independently remembers cycling colors.");
            string colorFile = Path.Combine(Path.GetTempPath(), "ScreenBrush-tool-colors-" + Guid.NewGuid() + ".json");
            try
            {
                perTool.Settings.Save(colorFile);
                var savedColors = System.Text.Json.JsonSerializer.Deserialize<Settings>(File.ReadAllText(colorFile))!;
                using var restarted = new AppController(savedColors, false);
                Check(restarted.Settings.Tool == Tool.Rectangle && restarted.Settings.CycleColors, "Active tool color survives restart.");
                restarted.SelectTool(Tool.Marker);
                Check(restarted.Settings.Color == "#FFAA1020" && !restarted.Settings.CycleColors, "Other tool color survives restart.");
                Check(restarted.Settings.Width == 9 && restarted.Settings.InkOpacity == .9, "Tool color changes preserve width and opacity.");
            }
            finally { File.Delete(colorFile); }
        }
        using (var modes = new AppController(new Settings { PreserveSessionOnEscape = true, Color = "#FF123456", CycleColors = false }, false))
        {
            Check(modes.Settings.BoardToolColors.All(pair => pair.Value == modes.Settings.ToolColors[pair.Key]), "Existing tool colors seed both modes.");
            modes.ChangeColor("#FFFF0000");
            modes.SelectBoard(BoardBackground.White);
            Check(modes.Settings.Color == "#FF123456", "Entering board via background selection restores board color.");
            modes.ChangeColor("#FFFFFFFF");
            modes.SelectTool(Tool.Pencil); modes.ToggleCycleColor();
            modes.ToggleBoard();
            Check(!modes.Settings.CycleColors && modes.Settings.Color == "#FF123456", "Leaving board restores screen pencil color.");
            modes.ChangeColor("#FF0000FF");
            modes.SelectTool(Tool.Marker);
            Check(modes.Settings.Color == "#FFFF0000", "Screen marker remembers its own color.");
            modes.ToggleBoard();
            Check(modes.Settings.Color == "#FFFFFFFF" && !modes.Settings.CycleColors, "Board marker restores its distinct solid color.");
            modes.SelectTool(Tool.Pencil);
            Check(modes.Settings.CycleColors, "Board pencil restores its distinct cycle setting.");
            modes.SelectBoard(BoardBackground.Black);
            Check(modes.Settings.CycleColors, "Changing board background preserves board tool color.");
            if (modes.Drawing) modes.EnterScreenMode(); else modes.SelectTool(modes.Settings.Tool); if (modes.Drawing) modes.EnterScreenMode(); else modes.SelectTool(modes.Settings.Tool);
            Check(!modes.Settings.WhiteboardEnabled && !modes.Settings.CycleColors, "Esc returns to normal screen colors without reactivating board.");
            modes.ToggleBoard();
            var restoredSettings = System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(modes.Settings))!;
            using var restartedModes = new AppController(restoredSettings, false);
            Check(restartedModes.Settings.CycleColors, "Board preference survives settings serialization and restart.");
            restartedModes.ToggleBoard();
            Check(restartedModes.Settings.Color == "#FF0000FF" && !restartedModes.Settings.CycleColors, "Screen preference survives restart independently.");
            var invalidBoardColors = modes.Settings.Clone();
            invalidBoardColors.BoardToolColors[Tool.Marker] = new ToolColor("invalid", false);
            Check(invalidBoardColors.Validate() != null, "Invalid board color is rejected.");
        }
        var oldBoard = settings.Clone();
        foreach (var a in Enum.GetValues<ActionId>().Where(a => a >= ActionId.ToggleBoard)) oldBoard.Shortcuts.Remove(a);
        oldBoard.Shortcuts[ActionId.Pencil] = Settings.Defaults()[ActionId.ToggleBoard];
        oldBoard.AddNewShortcuts();
        Check(oldBoard.Validate() == null && oldBoard.Shortcuts[ActionId.Pencil] == Settings.Defaults()[ActionId.ToggleBoard], "Board shortcut migration preserves custom bindings.");
        foreach (var entry in Whiteboard.Palette.Where(p => p.Background != BoardBackground.CustomImage))
        {
            var board = new Settings { BoardBackground = entry.Background };
            Check(Whiteboard.TryLoad(board, out var brush, out _) && brush.IsFrozen, "Board assets load as immutable shared brushes.");
            if (entry.Background is BoardBackground.Chalkboard or BoardBackground.Notebook or BoardBackground.Dots)
                Check(brush is ImageBrush { ImageSource: BitmapSource bitmap } && bitmap.PixelWidth >= 3840 && bitmap.PixelHeight >= 2160,
                    "Built-in backgrounds retain at least 4K detail after decoding.");
        }
        Check(!Whiteboard.TryLoad(new Settings { BoardBackground = BoardBackground.CustomImage, BoardImagePath = "missing-file.png" }, out _, out _), "Missing custom image gives a recoverable error.");
        var oldToolbar = settings.Clone(); oldToolbar.Shortcuts.Remove(ActionId.ToggleToolbar);
        oldToolbar.Shortcuts[ActionId.Pencil] = new Shortcut(Key.F1, ModifierKeys.Control | ModifierKeys.Shift);
        oldToolbar.AddNewShortcuts();
        Check(oldToolbar.Validate() == null && oldToolbar.Shortcuts[ActionId.Pencil] == new Shortcut(Key.F1, ModifierKeys.Control | ModifierKeys.Shift) && oldToolbar.Shortcuts[ActionId.ToggleToolbar] != oldToolbar.Shortcuts[ActionId.Pencil], "Toolbar shortcut migration preserves existing assignments and avoids collisions.");
        var legacyColors = settings.Clone();
        foreach (var color in Settings.Palette) legacyColors.Shortcuts.Remove(color.Action);
        legacyColors.Shortcuts[ActionId.Pencil] = new Shortcut(Key.D1, ModifierKeys.Control | ModifierKeys.Alt);
        legacyColors.AddNewShortcuts();
        Check(legacyColors.Validate() == null && legacyColors.Shortcuts[ActionId.Pencil] == new Shortcut(Key.D1, ModifierKeys.Control | ModifierKeys.Alt), "Color shortcut migration preserves existing assignments and resolves collisions.");
        using (var colorsController = new AppController(new Settings { InkOpacity = 0.4 }, false))
        foreach (var color in Settings.Palette)
        {
            colorsController.Settings.CycleColors = true;
            colorsController.Execute(color.Action);
            Check(colorsController.Settings.Color == color.Hex && !colorsController.Settings.CycleColors && colorsController.Settings.InkOpacity == 0.4, "Color shortcut selects the matching palette color and preserves opacity.");
        }
        var oldUndo = settings.Clone(); oldUndo.Shortcuts.Remove(ActionId.Redo);
        oldUndo.Shortcuts[ActionId.Undo] = new Shortcut(Key.Z, ModifierKeys.Control | ModifierKeys.Shift);
        oldUndo.AddNewShortcuts();
        Check(oldUndo.Shortcuts[ActionId.Undo] == new Shortcut(Key.Z, ModifierKeys.Control) && oldUndo.Shortcuts[ActionId.Redo] == new Shortcut(Key.Y, ModifierKeys.Control), "Legacy default undo migrates and redo is added.");
        var customUndo = settings.Clone(); customUndo.Shortcuts.Remove(ActionId.Redo);
        customUndo.Shortcuts[ActionId.Undo] = new Shortcut(Key.Y, ModifierKeys.Control);
        customUndo.AddNewShortcuts();
        Check(customUndo.Validate() == null && customUndo.Shortcuts[ActionId.Undo] == new Shortcut(Key.Y, ModifierKeys.Control) && customUndo.Shortcuts[ActionId.Redo] != customUndo.Shortcuts[ActionId.Undo], "Custom undo is preserved and new redo resolves conflicts.");
        Check(settings.InkOpacity == 0.9 && !settings.ShowToolbarOnStartup, "Default opacity and startup behavior are preserved.");
        var preferences = settings.Clone(); preferences.InkOpacity = 0.35; preferences.ShowToolbarOnStartup = true;
        var restored = preferences.Clone();
        Check(restored.InkOpacity == 0.35 && restored.ShowToolbarOnStartup, "New preferences survive serialization.");
        foreach (double invalidOpacity in new[] { -0.1, 1.1, double.NaN }) { preferences.InkOpacity = invalidOpacity; Check(preferences.Validate() != null, "Invalid opacity is rejected."); }
        foreach (bool useSpectrum in new[] { false, true })
        foreach (Tool tool in new[] { Tool.Marker, Tool.Ballpoint, Tool.Pencil, Tool.Rectangle })
        {
            double[] alpha = new double[3];
            for (int i = 0; i < 3; i++)
            {
                var mark = new Mark { Tool = tool, Width = 10, Color = Colors.Blue, FlowColors = useSpectrum, InkOpacity = i * 0.5, Start = new Point(20, 20), End = new Point(90, 70), Finished = true };
                mark.Points.Add(new(new Point(20, 40), 0.5f)); mark.Points.Add(new(new Point(90, 40), 0.5f));
                byte[] bytes = Render(mark);
                for (int pixel = 3; pixel < bytes.Length; pixel += 4) alpha[i] += bytes[pixel];
            }
            Check(alpha[0] == 0 && alpha[1] > 0 && Math.Abs(alpha[1] / alpha[2] - 0.5) < 0.03, "Opacity applies once to the full stroke, including spectrum and graphite.");
        }
        foreach (Tool shapeTool in new[] { Tool.Line, Tool.Arrow, Tool.Rectangle, Tool.Ellipse })
        {
            var hitShape = new Mark { Tool = shapeTool, Width = 12, Color = Colors.Blue, Start = new Point(20, 20), End = new Point(120, 120) };
            Point onEdge = shapeTool == Tool.Ellipse ? new Point(70, 20) : new Point(20, 20);
            Check(hitShape.Hit(onEdge, 5), "Deferred shape geometry still hits its visible edge.");
            hitShape.End = new Point(220, 220); hitShape.Invalidate();
            Check(!hitShape.Hit(new Point(900, 900), 5), "Invalidated shape keeps hit testing correct.");
        }
        Check(settings.Validate() == null, "Defaults must be valid.");
        Check(settings.MaxZoomPercent == 300 && settings.ClampZoom(6) == 3, "Default zoom is capped at 300 percent.");
        var zoomLimit = settings.Clone(); zoomLimit.MaxZoomPercent = 600;
        Check(zoomLimit.Validate() == null && zoomLimit.ClampZoom(8) == 6 && zoomLimit.ClampZoom(0.5) == 1, "600 percent limit and 100 percent floor are enforced.");
        Check(zoomLimit.Clone().MaxZoomPercent == 600, "Maximum zoom survives serialization.");
        zoomLimit.MaxZoomPercent = 100;
        Check(zoomLimit.Validate() == null && zoomLimit.ClampZoom(2) == 1, "100 percent maximum disables magnification.");
        zoomLimit.MaxZoomPercent = 601; Check(zoomLimit.Validate() != null, "Maximum above 600 percent is rejected.");
        zoomLimit.MaxZoomPercent = 99; Check(zoomLimit.Validate() != null, "Maximum below 100 percent is rejected.");
        var zoomOptions = settings.Clone(); zoomOptions.ToggleZoomPercent = 150;
        Check(zoomOptions.Validate() == null && zoomOptions.Clone().ToggleZoomPercent == 150, "150 percent toggle target persists.");
        zoomOptions.ToggleZoomPercent = 500; Check(zoomOptions.Validate() == null, "500 percent toggle is accepted independently from step zoom maximum.");
        zoomOptions.ToggleZoomPercent = 149; Check(zoomOptions.Validate() != null, "Toggle below 150 percent is rejected.");
        zoomOptions.ToggleZoomPercent = 501; Check(zoomOptions.Validate() != null, "Toggle above 500 percent is rejected.");
        var obsoleteWheel = Settings.ReadSettings("{\"WheelZoomEnabled\":true,\"WheelZoomModifiers\":0,\"Width\":11}");
        Check(obsoleteWheel.Width == 11 && obsoleteWheel.ToggleZoomPercent == 300 && !System.Text.Json.JsonSerializer.Serialize(obsoleteWheel).Contains("WheelZoom"), "Retired wheel zoom is ignored without losing other settings.");
        Check(settings.ColorCycleSpeed == 2, "Default color speed matches the saved preference.");
        var oldSettings = System.Text.Json.JsonSerializer.Deserialize<Settings>("{\"Width\":3}")!;
        Check(oldSettings.MaxZoomPercent == 300, "Older settings receive the 300 percent maximum.");
        Check(oldSettings.ToggleZoomPercent == 300 && oldSettings.ZoomStepPercent == 10, "Missing toggle zoom setting receives its default.");
        Check(oldSettings.ColorCycleSpeed == 2, "Settings without the speed field receive the new default.");
        var badSpeed = settings.Clone(); badSpeed.ColorCycleSpeed = double.NaN;
        Check(badSpeed.Validate() != null, "Non-finite color speed is rejected.");
        badSpeed.ColorCycleSpeed = 0;
        Check(badSpeed.Validate() != null, "Out-of-range color speed is rejected.");
        var legacy = settings.Clone();
        legacy.Shortcuts.Remove(ActionId.Marker); legacy.Shortcuts.Remove(ActionId.CycleColor); legacy.Shortcuts.Remove(ActionId.Quit);
        legacy.Shortcuts[ActionId.Pencil] = new Shortcut(Key.D8, ModifierKeys.Control | ModifierKeys.Shift);
        legacy.Tool = Tool.Arrow; legacy.Color = "#FF123456";
        legacy.AddNewShortcuts();
        Check(legacy.Validate() == null && legacy.Shortcuts[ActionId.Pencil].Key == Key.D8 && legacy.Tool == Tool.Arrow && legacy.Color == "#FF123456", "Upgrade preserves existing settings and resolves new shortcut collisions.");
        Check(legacy.Shortcuts[ActionId.Quit] == new Shortcut(Key.F4, ModifierKeys.Alt), "Missing exit shortcut receives Alt F4.");
        var duplicate = settings.Clone(); duplicate.Shortcuts[ActionId.Pencil] = duplicate.Shortcuts[ActionId.Ballpoint];
        Check(duplicate.Validate() != null, "Duplicate shortcuts must be rejected.");
        foreach (var key in new[] { Key.A, Key.D1, Key.Delete, Key.Space, Key.Up, Key.F1 })
        {
            var plain = settings.Clone();
            plain.Shortcuts[ActionId.Pencil] = new Shortcut(key, ModifierKeys.None);
            Check(plain.Validate() == null, "Single-key shortcuts are accepted.");
            var loaded = Settings.ReadSettings(System.Text.Json.JsonSerializer.Serialize(plain));
            Check(loaded.Shortcuts[ActionId.Pencil] == plain.Shortcuts[ActionId.Pencil], "Single-key shortcuts survive settings loading.");
        }
        var invalid = settings.Clone(); invalid.Width = double.NaN;
        Check(invalid.Validate() != null, "Invalid widths must be rejected.");
        string path = Path.Combine(Path.GetTempPath(), "ScreenBrush-check-" + Guid.NewGuid() + ".json");
        try
        {
            settings.HoldKey = Key.RightAlt; settings.Shortcuts[ActionId.Pencil] = new(Key.F9, ModifierKeys.Alt);
            settings.CycleColors = true; settings.Tool = Tool.Marker;
            settings.ColorCycleSpeed = 0.3;
            settings.Shortcuts[ActionId.Quit] = new Shortcut(Key.Q, ModifierKeys.Control | ModifierKeys.Alt);
            settings.Save(path);
            var saved = System.Text.Json.JsonSerializer.Deserialize<Settings>(File.ReadAllText(path))!;
            Check(saved.HoldKey == settings.HoldKey && saved.Shortcuts[ActionId.Pencil] == settings.Shortcuts[ActionId.Pencil], "Shortcut round trip.");
            Check(saved.CycleColors && saved.Tool == Tool.Marker, "Color mode and marker round trip.");
            Check(saved.ColorCycleSpeed == 0.3, "Custom color speed survives saving.");
            Check(saved.Shortcuts[ActionId.Quit] == settings.Shortcuts[ActionId.Quit], "Custom exit shortcut survives saving.");
            Check(!File.Exists(path + ".tmp"), "Atomic save must leave no temporary file.");
        }
        finally { File.Delete(path); }
        var fullHistory = new InkSurface { Tool = Tool.Marker, InkColor = Colors.Blue };
        var beginStroke = typeof(InkSurface).GetMethod("Begin", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        for (int i = 0; i < 100; i++) { beginStroke.Invoke(fullHistory, new object?[] { new Point(i * 10, 20), null, false }); fullHistory.Finish(); }
        fullHistory.Tool = Tool.Eraser;
        beginStroke.Invoke(fullHistory, new object?[] { new Point(-1000, -1000), null, false }); fullHistory.Finish();
        for (int i = 0; i < 100; i++) fullHistory.Undo();
        Check(fullHistory.MarkCount == 0, "An empty eraser stroke does not evict the oldest undo entry when history is full.");
        var surface = new InkSurface(); surface.SetView(2, new Point(100, 100));
        Check(surface.ToDocument(new Point(180, 60)) == new Point(140, 80), "Zoom must map pointer to document coordinates.");
        Check(surface.ViewMatrix.Transform(new Point(140, 80)) == new Point(180, 60), "Zoom matrix must invert pointer mapping.");
        foreach (Tool tool in new[] { Tool.Ballpoint, Tool.Pencil, Tool.Marker, Tool.Line, Tool.Arrow, Tool.Rectangle, Tool.Ellipse })
        {
            var mark = new Mark { Tool = tool, Color = Colors.Blue, Width = 6, Start = new Point(20, 20), End = new Point(120, 120), Finished = true };
            mark.Points.Add(new(new Point(20, 20), 0.5f)); mark.Points.Add(new(new Point(70, 70), 0.8f)); mark.Points.Add(new(new Point(120, 120), 0.4f));
            Check(mark.Drawing.IsFrozen && !mark.Drawing.Bounds.IsEmpty, $"{tool} must render a cached frozen drawing.");
            Check(!mark.Hit(new Point(600, 600), 5), $"{tool} must not erase distant marks.");
        }
        var light = Marker(0.05f); var heavy = Marker(1f);
        Check(Render(light).SequenceEqual(Render(heavy)), "Marker pixels ignore pressure.");
        var beforeFinish = Render(light); light.Finished = true; light.Invalidate();
        Check(beforeFinish.SequenceEqual(Render(light)), "Marker does not taper on finish.");
        var flow = Marker(0.5f, true);
        byte[] spectrum = Render(flow);
        Check(Pixel(spectrum, 30).R > Pixel(spectrum, 30).G, "Cycle begins with red.");
        var yellow = Pixel(spectrum, 820); Check(yellow.R > 180 && yellow.G > 180 && yellow.B < 90, "Slower cycle passes yellow at 800 DIP.");
        var green = Pixel(spectrum, 1620); Check(green.G > 180 && green.R < 90 && green.B < 90, "Slower cycle passes green.");
        var blue = Pixel(spectrum, 3220); Check(blue.B > 180 && blue.R < 90 && blue.G < 90, "Slower cycle passes blue.");
        flow.Invalidate(); Check(spectrum.SequenceEqual(Render(flow)), "Repaint preserves cycle colors.");
        var partial = new Mark { Tool = Tool.Marker, Width = 8, Color = Colors.Blue, FlowColors = true };
        partial.Points.Add(new(new Point(20, 40), 0.1f)); partial.Points.Add(new(new Point(220, 40), 0.9f));
        Check(Math.Abs(FlowColorRendering.EndHue(partial) - 7.5) < 0.01, "Default hue now advances half as fast.");
        var faster = new Mark { Tool = Tool.Marker, Width = 8, Color = Colors.Blue, FlowColors = true, ColorCycleSpeed = 2 };
        faster.Points.AddRange(partial.Points);
        Check(Math.Abs(FlowColorRendering.EndHue(faster) - 30) < 0.01, "Custom speed scales hue progression.");
        var rectangle = new Mark { Tool = Tool.Rectangle, Width = 12, Color = Colors.Blue, FlowColors = true, StartingHue = 160, Start = new Point(30, 30), End = new Point(370, 80) };
        byte[] corners = Render(rectangle);
        var left = Pixel(corners, 30, 45); var top = Pixel(corners, 55, 30);
        Check(left.A > 240 && top.A > 240 && ColorDistance(left, top) < 20, "Rectangle start/end corner has no color seam.");
        var right = Pixel(corners, 370, 65); var bottom = Pixel(corners, 345, 80);
        Check(ColorDistance(right, bottom) < 20, "Opposite rectangle corner stays continuous.");
        var sampler = new InkSampler();
        for (int i = 0; i < 500; i++)
        {
            var sample = sampler.Add(new Point(i, Math.Sin(i / 10.0)), i / 120.0, i % 2 == 0 ? 0.2f : 0.9f);
            Check(float.IsFinite(sample.Pressure) && sample.Pressure > 0 && sample.Pressure <= 1, "Pressure must stay finite and bounded.");
        }
        ReviewChecks.CheckInk(Check);
        Console.WriteLine($"PASS: {count} checks (shortcuts, settings, zoom coordinates, rendering, pressure).");
    }
    private static Mark Marker(float pressure, bool flow = false)
    {
        var mark = new Mark { Tool = Tool.Marker, Width = 8, Color = Colors.Blue, FlowColors = flow, ColorCycleSpeed = 1 };
        for (int x = 20; x <= 4820; x += 10) mark.Points.Add(new(new Point(x, 40), pressure));
        return mark;
    }
    private static byte[] Render(Mark mark)
    {
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) dc.DrawDrawing(mark.Drawing);
        var bitmap = new RenderTargetBitmap(5000, 100, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var pixels = new byte[5000 * 100 * 4]; bitmap.CopyPixels(pixels, 5000 * 4, 0); return pixels;
    }
    private static int ColorDistance(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
    private static Color Pixel(byte[] pixels, int x, int y = 40)
    {
        int offset = (y * 5000 + x) * 4;
        return Color.FromArgb(pixels[offset + 3], pixels[offset + 2], pixels[offset + 1], pixels[offset]);
    }
}
