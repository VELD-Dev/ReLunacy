using System.Numerics;
using ReLunacy.Core;
using ReLunacy.Core.Frames.DockedFrames;
using ReLunacy.Core.Frames;
using ReLunacy.MenuBar;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Utility;

/// <summary>Turns a TUID printed anywhere in the UI into a link to whatever it names.
///
/// Every asset family in a level is keyed by TUID, and the same 64-bit value can only belong to one
/// of them, so "what is this id" is answerable by lookup rather than by context. Cubemaps are the
/// one family with two identifier spaces - the cubemaps.dat RESOURCE tuid that instances reference,
/// and the per-cube 0x5A00 key - and both resolve here, because confusing the two is otherwise easy
/// and silent.
///
/// Frames already expose the selection calls; this only has to find the right one and focus it.</summary>
public static class TuidLink
{
    public enum Kind { Unknown, Moby, Tie, Shrub, Texture, Shader, CubemapResource, CubemapKey, Zone }

    public static Kind Resolve(ulong tuid)
    {
        var level = LunaWindow.Instance.Level;
        if (level == null || tuid == 0) return Kind.Unknown;

        // The libraries, not the classes read: a class the loaded region does not place is still one of
        // the level's, and the asset viewer reads it when it is opened.
        if (level.MobyLibrary.Contains(tuid)) return Kind.Moby;
        if (level.TieLibrary.Contains(tuid)) return Kind.Tie;
        if (level.Shrubs.ContainsKey(tuid)) return Kind.Shrub;
        if (level.Zones.ContainsKey(tuid)) return Kind.Zone;
        if (level.AllTextures.ContainsKey(tuid)) return Kind.Texture;
        if (level.Shaders.ContainsKey(tuid)) return Kind.Shader;
        foreach (var cube in level.Cubemaps)
        {
            if (cube.ResourceTuid == tuid) return Kind.CubemapResource;
            if (cube.Id == tuid) return Kind.CubemapKey;
        }
        return Kind.Unknown;
    }

    public static string Describe(Kind kind) => kind switch
    {
        Kind.Moby => LM.Get("GUI_Tuid_Moby"),
        Kind.Tie => LM.Get("GUI_Tuid_Tie"),
        Kind.Shrub => LM.Get("GUI_Tuid_Shrub"),
        Kind.Texture => LM.Get("GUI_Tuid_Texture"),
        Kind.Shader => LM.Get("GUI_Tuid_Shader"),
        Kind.CubemapResource => LM.Get("GUI_Tuid_CubemapResource"),
        Kind.CubemapKey => LM.Get("GUI_Tuid_CubemapKey"),
        Kind.Zone => LM.Get("GUI_Tuid_Zone"),
        _ => LM.Get("GUI_Tuid_Unknown"),
    };

    /// <summary>Opens whatever the TUID names in the frame that shows that kind of asset.</summary>
    public static bool Open(ulong tuid)
    {
        var level = LunaWindow.Instance.Level;
        if (level == null) return false;

        switch (Resolve(tuid))
        {
            case Kind.Moby:
                return Select<AssetViewer>(v => v.SelectMobyById(tuid));
            case Kind.Tie:
                return Select<AssetViewer>(v => v.SelectTieById(tuid));
            case Kind.Texture:
                return Select<TexturesExplorer>(v => v.SelectTexture(tuid));
            case Kind.Shader:
                return Select<ShaderBrowser>(v => v.SelectShader(tuid));
            case Kind.CubemapResource:
                return Select<CubemapViewer>(v => v.SelectByResource(tuid));
            case Kind.CubemapKey:
            {
                // A key names one cube inside a resource; the viewer selects by resource, so map it.
                ulong resource = 0;
                foreach (var cube in level.Cubemaps)
                    if (cube.Id == tuid) { resource = cube.ResourceTuid; break; }
                return resource != 0 && Select<CubemapViewer>(v => v.SelectByResource(resource));
            }
            case Kind.Zone:
                // Zones have no selection of their own; the Level Data frame is where they are listed.
                return Select<LevelDataFrame>(_ => true);
            default:
                return false;
        }
    }

    private static bool Select<T>(Func<T, bool> select) where T : Frame
    {
        if (ViewMenuDraw.EnsureFrameTypeOpen(typeof(T).Name) is not T frame) return false;
        bool ok = select(frame);
        frame.Focus();
        return ok;
    }

    /// <summary>Draws a TUID as a link: click to jump, right-click to copy. An id that names nothing
    /// loaded is drawn plainly, so a dead reference reads as dead rather than as a broken link.</summary>
    public static void Draw(ulong tuid, string? format = null)
    {
        string text = format ?? $"{tuid:X16}";
        var kind = Resolve(tuid);

        if (kind == Kind.Unknown)
        {
            ImGui.TextDisabled(text);
            Context(tuid, text, kind);
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.45f, 0.72f, 1f, 1f));
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();

        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(LM.Get("GUI_Tuid_Tooltip", Describe(kind)));
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) Open(tuid);
        }
        Context(tuid, text, kind);
    }

    private static void Context(ulong tuid, string text, Kind kind)
    {
        if (!ImGui.BeginPopupContextItem($"tuid_{tuid:X16}")) return;
        ImGui.TextDisabled(Describe(kind));
        ImGui.Separator();
        if (ImGui.MenuItem(LM.Get("GUI_Tuid_Copy"))) ImGui.SetClipboardText(text);
        if (kind != Kind.Unknown && ImGui.MenuItem(LM.Get("GUI_Tuid_Open"))) Open(tuid);
        ImGui.EndPopup();
    }

    /// <summary>Whether a search box's text matches this asset: by name as before, or by TUID, so a
    /// pasted id finds its asset in any list that filters through here. Hex is matched as a substring
    /// of the full 16-digit form, which lets a partial id work too.</summary>
    public static bool Matches(string? query, string? name, ulong tuid)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        if ((name ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)) return true;

        string q = query.Trim();
        if (q.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) q = q[2..];
        return q.Length > 0 && IsHex(q) && $"{tuid:X16}".Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHex(string s)
    {
        foreach (char c in s) if (!Uri.IsHexDigit(c)) return false;
        return true;
    }

    /// <summary>Parses a search box's text as a TUID, so a pasted id filters as well as a name.
    /// Accepts an optional 0x prefix and ignores surrounding space.</summary>
    public static bool TryParse(string? text, out ulong tuid)
    {
        tuid = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string s = text.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return s.Length >= 4 && ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out tuid);
    }
}
