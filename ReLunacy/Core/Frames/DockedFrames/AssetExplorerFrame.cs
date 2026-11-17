using System.Numerics;
using System.Runtime.InteropServices;
using ReLunacy.Core.Frames.Modals;
using ReLunacy.Core.Placement;
using ReLunacy.Core.Project;
using ReLunacy.Engine.Importing;
using ReLunacy.Engine.Scene;
using ReLunacy.Engine.Rendering;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// View > Asset Explorer: the level's loaded Moby and Tie classes as a grid of 3D thumbnails (AssetThumbnails),
/// searched by name, with favourites kept per game in the editor settings. A tile is dragged into the 3D view to
/// place a new instance there (InstancePlacer.PlaceNew); that needs a new-engine level. UFrags are not offered.
/// </summary>
public sealed class AssetExplorerFrame : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetWorkCenter(ImGui.GetMainViewport());
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.None;

    /// <summary>What the payload of a drag carries (see <see cref="InstancePlacer.PayloadType"/>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Payload
    {
        public int Kind;
        public ulong Id;
    }

    private sealed record Item(ulong Id, string Name);

    private const float ThumbSize = 96f;
    private const float Pad = 4f;

    private static string? _reveal;
    private bool _selectTies;

    /// <summary>Shows a class just imported: the explorer opens on the Ties tab, searched for its name.</summary>
    public static void Reveal(ulong id, string name)
    {
        _reveal = name;
        if (!LunaWindow.Instance.IsAnyFrameOpened<AssetExplorerFrame>()) LunaWindow.Instance.AddFrame(new AssetExplorerFrame());
    }

    private string _search = "";
    private bool _favouritesOnly;
    private List<Item> _mobys = [], _ties = [];
    private (object? Level, int Mobys, int Ties) _built;

    public AssetExplorerFrame()
    {
        FrameName = LM.Get("GUI_Frame_AssetExplorer");
    }

    protected override void Render(double deltaTime)
    {
        var window = LunaWindow.Instance;
        var level = window.Level;
        var assets = window.AssetManager;
        if (level == null || assets == null)
        {
            ImGui.TextDisabled(LM.Get("GUI_AssetExplorer_NoLevel"));
            return;
        }
        Rebuild(level, assets);
        AssetThumbnails.Singleton.Pump();

        ImGui.SetNextItemWidth(MathF.Max(80f, ImGui.GetContentRegionAvail().X - 36f));
        ImGui.InputTextWithHint("##asset_search", $"{Icons.Search}  {LM.Get("GUI_AssetExplorer_Search")}", ref _search, 128);
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, _favouritesOnly ? Gold : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        if (ImGui.Button($"{Icons.Star}##favourites_only")) _favouritesOnly = !_favouritesOnly;
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_AssetExplorer_FavouritesTip"));

        if (_reveal != null) { _search = _reveal; _favouritesOnly = false; _selectTies = true; _reveal = null; }

        bool placeable = InstancePlacer.Available;
        if (!placeable) ImGui.TextDisabled(LM.Get("GUI_AssetExplorer_NewEngineOnly"));

        if (!ImGui.BeginTabBar("##asset_kinds")) return;
        if (ImGui.BeginTabItem($"{Icons.Cube} {LM.Get("GUI_AssetExplorer_Mobys")}"))
        {
            DrawGrid(PlaceableKind.Moby, _mobys, placeable);
            ImGui.EndTabItem();
        }
        bool selectTies = _selectTies;
        _selectTies = false;
        if (ImGui.BeginTabItem($"{Icons.Cubes} {LM.Get("GUI_AssetExplorer_Ties")}", selectTies ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
        {
            ImGui.BeginDisabled(!ImportModelModal.Available);
            if (ImGui.Button(LM.Get("GUI_AssetExplorer_ImportModel"))) ImportModelModal.Choose();
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(LM.Get(ImportModelModal.Available ? "GUI_AssetExplorer_ImportModelTip" : "GUI_ImportModel_ACiTOnly"));
            DrawGrid(PlaceableKind.Tie, _ties, placeable);
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }

    private static readonly Vector4 Gold = new(1f, 0.8f, 0.2f, 1f);

    /// <summary>The loaded classes, by name; built again when the level or the number of loaded classes changes.</summary>
    private void Rebuild(object level, AssetManager assets)
    {
        var data = LunaWindow.Instance.Level!;
        var key = (level, assets.Mobys.Count, assets.Ties.Count);
        if (_built == key) return;
        _built = key;
        _mobys = [.. assets.Mobys.Keys.Where(data.Mobys.ContainsKey)
            .Select(id => new Item(id, ShortName(data.Mobys[id].Name, id))).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)];
        _ties = [.. assets.Ties.Keys.Where(data.Ties.ContainsKey)
            .Select(id => new Item(id, ShortName(data.Ties[id].Name, id))).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private static string ShortName(string? name, ulong id) => !string.IsNullOrEmpty(name) ? name.Split('/')[^1] : $"{id:X}";

    // --- Favourites ---------------------------------------------------------------------------------

    private static string GameKey => LunaWindow.Instance.Level?.GameDefinition?.Id.ToString() ?? "unknown";
    private static string FavouriteKey(PlaceableKind kind, ulong id) => $"{(kind == PlaceableKind.Moby ? 'M' : 'T')}:{id:X}";

    private static bool IsFavourite(PlaceableKind kind, ulong id) =>
        Program.Settings.AssetFavourites.TryGetValue(GameKey, out var list) && list.Contains(FavouriteKey(kind, id));

    private static void ToggleFavourite(PlaceableKind kind, ulong id)
    {
        var favourites = Program.Settings.AssetFavourites;
        if (!favourites.TryGetValue(GameKey, out var list)) favourites[GameKey] = list = [];
        string key = FavouriteKey(kind, id);
        if (!list.Remove(key)) list.Add(key);
        Program.Settings.SaveSettingsToFile();
    }

    // --- The grid -----------------------------------------------------------------------------------

    private void DrawGrid(PlaceableKind kind, List<Item> items, bool placeable)
    {
        var shown = items.Where(i => (_search.Length == 0 || i.Name.Contains(_search, StringComparison.OrdinalIgnoreCase))
                                     && (!_favouritesOnly || IsFavourite(kind, i.Id))).ToList();
        
        // Fast sorting
        var sorted = shown.Where(i => IsFavourite(kind, i.Id)).Concat(shown.Where(i => !IsFavourite(kind, i.Id))).ToList();
        
        // Favorites sorted first
        // Slow sorting
        /*
        shown.Sort((a, b) =>
            IsFavourite(kind, a.Id) && !IsFavourite(kind, b.Id)
            ? 1
            : !IsFavourite(kind, a.Id) && IsFavourite(kind, b.Id)
            ? -1
            : 0
        );*/
        
        if (!ImGui.BeginChild($"##grid_{kind}", ImGui.GetContentRegionAvail(), ImGuiChildFlags.None)) { ImGui.EndChild(); return; }
        if (sorted.Count == 0)
        {
            ImGui.TextDisabled(LM.Get("GUI_AssetExplorer_None"));
            ImGui.EndChild();
            return;
        }

        var spacing = ImGui.GetStyle().ItemSpacing;
        float tileWidth = ThumbSize + Pad * 2f;
        float tileHeight = ThumbSize + Pad * 2f + ImGui.GetTextLineHeight();
        int columns = Math.Max(1, (int)((ImGui.GetContentRegionAvail().X + spacing.X) / (tileWidth + spacing.X)));
        int rows = (sorted.Count + columns - 1) / columns;
        // Only the rows on screen are drawn, so only their thumbnails are asked for.
        var clipper = new ImGuiListClipper();
        clipper.Begin(rows, tileHeight + spacing.Y);
        while (clipper.Step())
            for (int row = clipper.DisplayStart; row < clipper.DisplayEnd; row++)
                for (int column = 0; column < columns && row * columns + column < sorted.Count; column++)
                {
                    if (column > 0) ImGui.SameLine();
                    DrawTile(kind, sorted[row * columns + column], new Vector2(tileWidth, tileHeight), placeable);
                }
        clipper.End();
        ImGui.EndChild();
    }

    private static void DrawTile(PlaceableKind kind, Item item, Vector2 size, bool placeable)
    {
        ImGui.PushID(unchecked((int)item.Id));
        var origin = ImGui.GetCursorScreenPos();
        ImGui.SetNextItemAllowOverlap();
        ImGui.InvisibleButton("##tile", size);
        bool hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        var pendingRegion = kind == PlaceableKind.Tie ? ModelImportSession.PendingRegion(EntityManager.Singleton.Regions, item.Id) : null;
        if (pendingRegion != null && ImGui.BeginPopupContextItem("##tile_menu"))
        {
            bool used = ModelImportSession.IsPlaced(pendingRegion, item.Id);
            if (ImGui.MenuItem(LM.Get("GUI_AssetExplorer_Remove"), "", false, !used)) RemoveImported(pendingRegion, item.Id);
            if (used && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(LM.Get("GUI_AssetExplorer_RemoveUsed"));
            ImGui.EndPopup();
        }
        // The drag source is the tile itself: the payload says what it is, the tip under the cursor its name.
        if (placeable && ImGui.BeginDragDropSource(ImGuiDragDropFlags.None))
        {
            var payload = new Payload { Kind = (int)kind, Id = item.Id };
            unsafe { ImGui.SetDragDropPayload(InstancePlacer.PayloadType, &payload, (nuint)sizeof(Payload)); }
            ImGui.TextUnformatted(item.Name);
            ImGui.EndDragDropSource();
        }

        // The star sits over the tile's corner and is its own button.
        var starSize = new Vector2(20f);
        ImGui.SetCursorScreenPos(origin + new Vector2(size.X - starSize.X - 2f, 2f));
        if (ImGui.InvisibleButton("##star", starSize)) ToggleFavourite(kind, item.Id);
        bool starHovered = ImGui.IsItemHovered();
        if (starHovered) ImGui.SetTooltip(LM.Get("GUI_AssetExplorer_FavouriteTip"));
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);

        var dl = ImGui.GetWindowDrawList();
        var min = origin;
        var max = origin + size;
        dl.AddRectFilled(min, max, ImGui.GetColorU32(hovered && !starHovered ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg), 4f);
        var imageMin = origin + new Vector2(Pad);
        var imageMax = imageMin + new Vector2(ThumbSize);
        if (AssetThumbnails.Singleton.Get(kind == PlaceableKind.Tie, item.Id) is { } texture)
            dl.AddImage(texture, imageMin, imageMax);
        else
        {
            string glyph = AssetThumbnails.Singleton.Failed(kind == PlaceableKind.Tie, item.Id)
                ? (kind == PlaceableKind.Tie ? Icons.Cubes : Icons.Cube) : Icons.Refresh;
            var glyphSize = ImGui.CalcTextSize(glyph);
            dl.AddText((imageMin + imageMax - glyphSize) * 0.5f, ImGui.GetColorU32(ImGuiCol.TextDisabled), glyph);
        }
        dl.AddText(origin + new Vector2(Pad, Pad * 2f + ThumbSize), ImGui.GetColorU32(ImGuiCol.Text), Fit(item.Name, ThumbSize));
        bool favourite = IsFavourite(kind, item.Id);
        if (favourite || hovered)
            dl.AddText(origin + new Vector2(size.X - starSize.X + 2f, 4f),
                ImGui.ColorConvertFloat4ToU32(favourite ? Gold : new Vector4(1f, 1f, 1f, starHovered ? 0.9f : 0.4f)), Icons.Star);

        if (pendingRegion != null)
            dl.AddText(imageMin + new Vector2(2f), ImGui.GetColorU32(Gold), "*");
        if (hovered && !starHovered && !ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            ImGui.SetTooltip(pendingRegion != null ? $"{item.Name}\n{LM.Get("GUI_AssetExplorer_Imported")}" : item.Name);
        ImGui.PopID();
    }

    private static void RemoveImported(EntityRegion region, ulong tuid)
    {
        var window = LunaWindow.Instance;
        if (window.Reader is not { } reader || !ModelImportSession.Remove(region, tuid, reader, window.AssetManager)) return;
        ImportRecords.Forget(ProjectSession.Current, tuid);
        AssetThumbnails.Singleton.Forget(true, tuid);
        EntityManager.StructureVersion++;
    }

    /// <summary>The name cut to fit <paramref name="width"/>, with an ellipsis.</summary>
    private static string Fit(string name, float width)
    {
        if (ImGui.CalcTextSize(name).X <= width) return name;
        int length = name.Length;
        while (length > 1 && ImGui.CalcTextSize(name[..length] + "...").X > width) length--;
        return name[..length] + "...";
    }
}
