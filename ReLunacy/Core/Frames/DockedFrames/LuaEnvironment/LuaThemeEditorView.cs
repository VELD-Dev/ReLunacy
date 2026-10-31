using System.Numerics;
using ReLunacy.Engine.Scripting;
using ReLunacy.Utility;
using ReLunacy.Utility.CodeEditor;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames.LuaEnvironment;

/// <summary>
/// Live colour editing for the code editor's theme, with the real editor as the preview.
///
/// The preview is an actual <see cref="CodeEditorControl"/> over a sample script rather than a
/// painted mock-up, so what it shows is what the editor will do - including the chrome colours
/// (caret, selection, current line, gutter) that a swatch list cannot convey. Judging a theme is
/// the whole point, and those are the colours hardest to pick blind.
///
/// Edits apply immediately because every view shares one theme instance through
/// <see cref="LuaEditorThemeStore"/>: writing a slot and re-resolving is visible next frame.
/// Nothing is written to disk until Save, so experimenting cannot damage a theme file.
/// </summary>
public sealed class LuaThemeEditorView
{
    private readonly CodeEditorControl _preview = new();

    /// <summary>The colour list's width. Dragged rather than a fraction of the frame: the rows are
    /// as wide as their labels, and the preview wants whatever is left.</summary>
    private readonly ResizablePane _slotsPane = new(340f);
    private readonly LuaSyntaxHighlighter _highlighter;
    private LuaApiDatabase _api = new();
    private ReLunacy.Engine.Games.GameId? _apiGame;

    /// <summary>The theme as it was when loaded or last saved, so Revert has somewhere to go.</summary>
    private LuaEditorTheme? _baseline;
    private LuaEditorTheme? _liveAtBaseline;
    private bool _dirty;

    private string _saveAsName = "";
    private string? _status;

    /// <summary>
    /// A sample that exercises every role. The semantic ones - function, class, field, method -
    /// only colour names the loaded game's documentation knows, so with no level open those show as
    /// undocumented globals. That is the highlighter being honest, not the preview misbehaving.
    /// </summary>
    private const string Sample = """
        -- A sample of every role the highlighter paints.
        local player, count = GetPlayer(), 3
        
        function OnTick(handle, delta)
        	if handle:IsValid() and count > 0 then
        		for i = 1, count do
        			player.position.x = player.position.x + delta * 0.5
        			Print("moved " .. i .. " of " .. count)
        		end
        	elseif SomeUndocumentedGlobal then
        		return nil
        	end
        
        	while count > 0 do
        		count = count - 1
        	end
        end
        """;

    public LuaThemeEditorView()
    {
        _highlighter = new LuaSyntaxHighlighter(_preview.Document);
        _preview.Highlighter = _highlighter.ForLine;
        _preview.Document.Load(Sample.ReplaceLineEndings("\n"));
        _preview.OpensScope = caret => LuaIndentRules.OpensScope(_preview.Document.GetText(), caret);
    }

    public void Draw(double deltaTime)
    {
        var theme = LuaEditorThemeStore.Current;
        CaptureBaseline(theme);

        DrawToolbar(theme);
        if (_status is not null) ImGui.TextDisabled(_status);
        if (theme.Problems.Count > 0)
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), string.Join("; ", theme.Problems));

        ImGui.Separator();

        // Colours left, preview right: a colour has to be judged against the code, so changing one
        // and looking at the result should not need scrolling or a tab switch.
        if (_slotsPane.Begin("theme_slots"))
        {
            DrawGroup(theme, LuaEditorThemeGroup.Lexical, LM.Get("GUI_LuaEnv_ThemeLexical"));
            DrawGroup(theme, LuaEditorThemeGroup.Semantic, LM.Get("GUI_LuaEnv_ThemeSemantic"));
            DrawGroup(theme, LuaEditorThemeGroup.Chrome, LM.Get("GUI_LuaEnv_ThemeChrome"));
            DrawGroup(theme, LuaEditorThemeGroup.Decoration, LM.Get("GUI_LuaEnv_ThemeDecoration"));
        }
        ResizablePane.End();

        ImGui.SameLine();
        if (ImGui.BeginChild("theme_preview", new Vector2(0f, 0f), ImGuiChildFlags.Borders))
        {
            ImGui.TextDisabled(LM.Get("GUI_LuaEnv_ThemePreviewHint"));
            _preview.Theme = theme;
            _highlighter.Configure(theme, Api());
            _preview.Draw("theme_preview_editor", ImGui.GetContentRegionAvail(), deltaTime);
        }
        ImGui.EndChild();
    }

    /// <summary>
    /// Remembers the theme to revert to. Re-taken when the instance changes - a theme switch, a
    /// reload, a reset - rather than every frame, which would make Revert a no-op.
    /// </summary>
    private void CaptureBaseline(LuaEditorTheme theme)
    {
        if (ReferenceEquals(_liveAtBaseline, theme)) return;
        _liveAtBaseline = theme;
        _baseline = theme.Clone();
        _dirty = false;
    }

    private void DrawToolbar(LuaEditorTheme theme)
    {
        var names = LuaEditorThemeLoader.Available();
        int current = Math.Max(0, names.FindIndex(n =>
            string.Equals(n, Program.Settings.CodeEditorTheme, StringComparison.OrdinalIgnoreCase)));

        ImGui.SetNextItemWidth(180f);
        if (ImGui.Combo(LM.Get("GUI_LuaEnv_ThemeTheme"), ref current, [.. names], names.Count))
        {
            Program.Settings.CodeEditorTheme = names[current];
            LuaEditorThemeStore.Invalidate();
            _status = null;
        }

        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_LuaEnv_ThemeSave")))
        {
            string name = Program.Settings.CodeEditorTheme;
            LuaEditorThemeLoader.Save(theme, name);
            // The baseline moves to what is now on disk, so Revert goes back to the saved state.
            _baseline = theme.Clone();
            _dirty = false;
            _status = LM.Get("GUI_LuaEnv_ThemeSaved", name);
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(!_dirty || _baseline is null);
        if (ImGui.Button(LM.Get("GUI_LuaEnv_ThemeRevert")) && _baseline is not null)
        {
            LuaEditorThemeStore.Replace(_baseline.Clone());
            _liveAtBaseline = null;
            _status = LM.Get("GUI_LuaEnv_ThemeReverted");
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_LuaEnv_ThemeDefaults")))
        {
            LuaEditorThemeStore.Replace(new LuaEditorTheme { Name = theme.Name });
            _liveAtBaseline = null;
            _dirty = true;
            _status = LM.Get("GUI_LuaEnv_ThemeDefaultsApplied");
        }

        ImGui.SetNextItemWidth(180f);
        ImGui.InputText(LM.Get("GUI_LuaEnv_ThemeSaveAsName"), ref _saveAsName, 64);
        ImGui.SameLine();
        string wanted = _saveAsName.Trim();
        ImGui.BeginDisabled(wanted.Length == 0 || wanted.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0);
        if (ImGui.Button(LM.Get("GUI_LuaEnv_ThemeSaveAs")))
        {
            var copy = theme.Clone();
            copy.Name = wanted;
            LuaEditorThemeLoader.Save(copy, wanted);
            // Switch to it, then reload from the file so what is edited from here is the new theme.
            Program.Settings.CodeEditorTheme = wanted;
            LuaEditorThemeStore.Invalidate();
            _status = LM.Get("GUI_LuaEnv_ThemeSaved", wanted);
            _saveAsName = "";
        }
        ImGui.EndDisabled();

        if (_dirty)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(LM.Get("GUI_LuaEnv_ThemeUnsaved"));
        }
    }

    private void DrawGroup(LuaEditorTheme theme, LuaEditorThemeGroup group, string label)
    {
        if (!ImGui.CollapsingHeader(label, ImGuiTreeNodeFlags.DefaultOpen)) return;

        foreach (var slot in LuaEditorTheme.Slots)
        {
            if (slot.Group != group) continue;
            DrawSlot(theme, slot);
        }
    }

    private void DrawSlot(LuaEditorTheme theme, LuaEditorThemeSlot slot)
    {
        // The theme stores hex, ImGui works in floats, so each row converts both ways. An
        // unparseable value shows as magenta here for the same reason it does in the editor.
        if (!LuaEditorTheme.TryParseHex(slot.Get(theme), out byte a, out byte r, out byte g, out byte b))
            (a, r, g, b) = ((byte)0xFF, (byte)0xFF, (byte)0x00, (byte)0xFF);

        var colour = new Vector4(r / 255f, g / 255f, b / 255f, a / 255f);
        if (ImGui.ColorEdit4(slot.Label, ref colour,
                ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf |
                ImGuiColorEditFlags.DisplayHex))
        {
            slot.Set(theme, LuaEditorTheme.ToHex(
                Channel(colour.W), Channel(colour.X), Channel(colour.Y), Channel(colour.Z)));
            // Re-resolve the shared instance: that is the whole live-edit mechanism.
            theme.Resolve();
            _dirty = true;
            _status = null;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"{slot.Key}\n{slot.Description}");
    }

    private static byte Channel(float v) => (byte)Math.Clamp(MathF.Round(v * 255f), 0, 255);

    /// <summary>The documented API for the loaded game, so the preview's semantic roles are real.</summary>
    private LuaApiDatabase Api()
    {
        var game = Core.LunaWindow.Instance?.Level?.GameDefinition?.Id;
        if (_api.IsEmpty || game != _apiGame)
        {
            _api = LuaApiLoader.Load(Path.Combine(Program.EditorPath, "LuaApi"), game);
            _apiGame = game;
        }
        return _api;
    }
}
