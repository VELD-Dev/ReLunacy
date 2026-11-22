using System.Numerics;
using ReLunacy.Core.Frames.Modals;
using ReLunacy.Core.Placement;
using ReLunacy.Core.Project;
using ReLunacy.Engine.Assets.Audio;
using ReLunacy.Engine.Assets.Browser;
using ReLunacy.Engine.Assets.Effects;
using ReLunacy.Engine.Config;
using ReLunacy.Engine.Importing;
using ReLunacy.Engine.Loading.Readers;
using ReLunacy.Engine.Scene;
using ReLunacy.Engine.Text;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// View > Assets Browser: every asset of the loaded level (<see cref="AssetIndex"/>) in folders made from their name paths,
/// as Unreal's Content Browser shows them. The tree on the left holds folders only; the grid on the right shows the open
/// folder's subfolders, then its assets, each with a thumbnail made when its tile is first shown (<see cref="ThumbnailService"/>)
/// or the kind's icon. Search looks through everything below the open folder. Double-click opens an
/// asset through <see cref="AssetOpener"/>; a Moby or Tie tile dragged into the 3D view places an instance there.
/// </summary>
public sealed class AssetsBrowserFrame : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetWorkCenter(ImGui.GetMainViewport());
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.None;

    private const string FavouritesPath = ":favourites";
    private const float Pad = 4f, Strip = 3f;

    private static readonly AssetKind[] Kinds = Enum.GetValues<AssetKind>();
    private static readonly Vector4 Gold = new(1f, 0.8f, 0.2f, 1f);

    private static AssetKey? _reveal;

    /// <summary>Opens the browser on an asset and selects it (after a model import, the new class).</summary>
    public static void Reveal(AssetKey key)
    {
        _reveal = key;
        var window = LunaWindow.Instance;
        if (window.GetFirstFrame<AssetsBrowserFrame>() is { } frame) frame.Focus();
        else window.AddFrame(new AssetsBrowserFrame());
    }

    private static AssetIndex? _index;
    private static object? _builtFor;
    private AssetIndex? _seenIndex;

    /// <summary>The index as last built, shared with the Reference Viewer.</summary>
    public static AssetIndex? Shared => _index;

    private string _path = "";
    private readonly List<string> _back = [], _forward = [];
    private string? _expandTo;
    private string _search = "";
    private readonly HashSet<AssetKind> _hidden = [];
    private float _tileSize = 96f;
    private bool _listView;
    private AssetKey? _selected;
    private bool _scrollToSelected;

    private (AssetIndex Index, string Path, string Search, int Hidden, int Favourites)? _viewFor;
    private List<AssetFolder> _viewFolders = [];
    private List<AssetEntry> _viewEntries = [];
    private int _favouritesVersion;

    private AssetEntry? _referencesOf;
    private bool _openReferences;

    public AssetsBrowserFrame()
    {
        FrameName = LM.Get("GUI_Frame_AssetsBrowser");
    }

    protected override void Render(double deltaTime)
    {
        var window = LunaWindow.Instance;
        if (window.Level == null || window.AssetManager == null)
        {
            ImGui.TextDisabled(LM.Get("GUI_AssetsBrowser_NoLevel"));
            return;
        }
        EnsureIndex();
        if (!ReferenceEquals(_seenIndex, _index))
        {
            _seenIndex = _index;
            _viewFor = null;
            if (_index != null && _path != FavouritesPath && _index.Folder(_path) == null) _path = "";
        }
        if (_index is not { } index) return;
        AssetThumbnails.Singleton.Pump();
        HandleReveal(index);

        DrawToolbar(index);
        if (!ImGui.BeginTable("##assets_layout", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV, ImGui.GetContentRegionAvail()))
            return;
        ImGui.TableSetupColumn("##tree", ImGuiTableColumnFlags.WidthFixed, 220f);
        ImGui.TableSetupColumn("##content", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        if (ImGui.BeginChild("##assets_tree", ImGui.GetContentRegionAvail())) DrawTree(index);
        ImGui.EndChild();
        ImGui.TableNextColumn();
        if (ImGui.BeginChild("##assets_content", ImGui.GetContentRegionAvail())) DrawContent(index);
        ImGui.EndChild();
        ImGui.EndTable();

        DrawReferencesPopup(index);
    }

    // --- Index ---------------------------------------------------------------------------------------

    private static readonly Dictionary<string, Task<List<TextTableRef>>> TextTableScans = [];
    private static string? _gameFolderFor, _gameFolder;

    /// <summary>Builds the index again when the level, its classes, the scene's structure or a late source (effects,
    /// sounds, text tables) changed.</summary>
    public static AssetIndex? EnsureIndex()
    {
        var window = LunaWindow.Instance;
        if (window.Level is not { } level || window.AssetManager == null) return null;
        var assets = window.AssetManager!;
        string? source = window.fileManager?.SourcePath;
        // Looked up once per level: it asks the file system.
        if (_gameFolderFor != source)
        {
            _gameFolderFor = source;
            _gameFolder = string.IsNullOrEmpty(source) ? null : GameConfigs.GameFolderFor(source);
        }
        string? gameFolder = _gameFolder;
        var effects = LevelEffectPlayer.Current;
        var effectNames = effects != null ? EffectTags.For(gameFolder) : null;
        var sounds = window.fileManager is { } files ? SoundReader.For(files) : null;
        List<TextTableRef>? tables = null;
        if (gameFolder != null)
        {
            if (!TextTableScans.TryGetValue(gameFolder, out var scan))
                TextTableScans[gameFolder] = scan = Task.Run(() => GameText.List(gameFolder));
            if (scan.IsCompletedSuccessfully) tables = scan.Result;
        }

        var key = (level, EntityManager.StructureVersion, level.Mobys.Count, level.Ties.Count, assets.Mobys.Count, assets.Ties.Count,
            effects, effectNames, sounds, tables);
        if (Equals(_builtFor, key)) return _index;
        _builtFor = key;
        _index = AssetIndex.Build(new AssetIndexSources
        {
            Level = level,
            LevelPath = source,
            Regions = EntityManager.Singleton.Regions,
            SceneEntities = EntityManager.Singleton.AllEntities(),
            Effects = effects,
            EffectNames = effectNames,
            Sounds = sounds,
            TextTables = tables,
        });
        return _index;
    }

    private void HandleReveal(AssetIndex index)
    {
        if (_reveal is not { } key || index.Get(key) is not { } entry) return;
        _reveal = null;
        _search = "";
        _hidden.Remove(entry.Kind);
        Navigate(entry.Folder);
        _selected = key;
        _scrollToSelected = true;
    }

    // --- Navigation ----------------------------------------------------------------------------------

    /// <summary>Opens a folder; <paramref name="reveal"/> opens the tree down to it and scrolls it into view.</summary>
    private void Navigate(string path, bool reveal = true)
    {
        if (reveal) _expandTo = path;
        if (path == _path) return;
        _back.Add(_path);
        _forward.Clear();
        _path = path;
    }

    private void Back()
    {
        if (_back.Count == 0) return;
        _forward.Add(_path);
        _path = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        _expandTo = _path;
    }

    private void Forward()
    {
        if (_forward.Count == 0) return;
        _back.Add(_path);
        _path = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        _expandTo = _path;
    }

    private void Up()
    {
        if (_path.Length == 0) return;
        int cut = _path.LastIndexOf('/');
        Navigate(_path == FavouritesPath || cut < 0 ? "" : _path[..cut]);
    }

    // --- Toolbar -------------------------------------------------------------------------------------

    private void DrawToolbar(AssetIndex index)
    {
        ImGui.BeginDisabled(_back.Count == 0);
        if (ImGui.Button(Icons.ArrowLeft)) Back();
        ImGui.EndDisabled();
        Tip(LM.Get("GUI_AssetsBrowser_Back"));
        ImGui.SameLine();
        ImGui.BeginDisabled(_forward.Count == 0);
        if (ImGui.Button(Icons.ArrowRight)) Forward();
        ImGui.EndDisabled();
        Tip(LM.Get("GUI_AssetsBrowser_Forward"));
        ImGui.SameLine();
        ImGui.BeginDisabled(_path.Length == 0);
        if (ImGui.Button(Icons.ArrowUp)) Up();
        ImGui.EndDisabled();
        Tip(LM.Get("GUI_AssetsBrowser_Up"));

        // Right-hand controls are measured first so the search box takes what is left.
        const float kindsWidth = 120f, sliderWidth = 90f;
        float right = kindsWidth + sliderWidth + ImGui.GetFrameHeight() * 2f + ImGui.GetStyle().ItemSpacing.X * 4f;
        ImGui.SameLine();
        ImGui.SetNextItemWidth(MathF.Max(80f, MathF.Min(260f, ImGui.GetContentRegionAvail().X - right)));
        if (ImGui.InputTextWithHint("##assets_search", $"{Icons.Search}  {LM.Get("GUI_AssetsBrowser_Search")}", ref _search, 128))
            _viewFor = null;
        Tip(LM.Get("GUI_AssetsBrowser_SearchTip"));

        ImGui.SameLine();
        ImGui.SetNextItemWidth(kindsWidth);
        string preview = _hidden.Count == 0 ? LM.Get("GUI_AssetsBrowser_AllKinds") : LM.Get("GUI_AssetsBrowser_SomeKinds", Kinds.Length - _hidden.Count);
        if (ImGui.BeginCombo("##assets_kinds", preview))
        {
            var counts = index.CountByKind();
            if (ImGui.Selectable(LM.Get("GUI_AssetsBrowser_AllKinds"), false, ImGuiSelectableFlags.NoAutoClosePopups)) { _hidden.Clear(); _viewFor = null; }
            ImGui.Separator();
            foreach (var kind in Kinds)
            {
                if (!counts.TryGetValue(kind, out int count)) continue;
                bool shown = !_hidden.Contains(kind);
                if (ImGui.Checkbox($"{KindIcon(kind)} {KindName(kind)}  ({count})##kind_{kind}", ref shown))
                {
                    if (shown) _hidden.Remove(kind); else _hidden.Add(kind);
                    _viewFor = null;
                }
            }
            ImGui.EndCombo();
        }
        Tip(LM.Get("GUI_AssetsBrowser_KindsTip"));

        ImGui.SameLine();
        ImGui.BeginDisabled(_listView);
        ImGui.SetNextItemWidth(sliderWidth);
        ImGui.SliderFloat("##assets_tile", ref _tileSize, 48f, 192f, "%.0f");
        ImGui.EndDisabled();
        Tip(LM.Get("GUI_AssetsBrowser_TileSize"));
        ImGui.SameLine();
        if (ImGui.Button(_listView ? Icons.Grid : Icons.List)) _listView = !_listView;
        Tip(LM.Get(_listView ? "GUI_AssetsBrowser_GridView" : "GUI_AssetsBrowser_ListView"));
        ImGui.SameLine();
        ImGui.BeginDisabled(!ImportModelModal.Available);
        if (ImGui.Button(Icons.Plus)) ImportModelModal.Choose();
        ImGui.EndDisabled();
        Tip(LM.Get(ImportModelModal.Available ? "GUI_AssetsBrowser_ImportModel" : "GUI_ImportModel_ACiTOnly"));

        DrawBreadcrumb();
    }

    private void DrawBreadcrumb()
    {
        var dim = ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled];
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        if (ImGui.SmallButton($"{Icons.FolderOpen} {LM.Get("GUI_AssetsBrowser_Root")}##crumb_root")) Navigate("");
        if (_path == FavouritesPath)
        {
            ImGui.SameLine();
            ImGui.TextColored(dim, ">");
            ImGui.SameLine();
            ImGui.SmallButton($"{Icons.Star} {LM.Get("GUI_AssetsBrowser_Favourites")}##crumb_fav");
        }
        else if (_path.Length > 0)
        {
            string[] parts = _path.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                ImGui.SameLine();
                ImGui.TextColored(dim, ">");
                ImGui.SameLine();
                if (ImGui.SmallButton($"{parts[i]}##crumb_{i}")) Navigate(string.Join('/', parts[..(i + 1)]));
            }
        }
        ImGui.PopStyleColor();
    }

    // --- Tree ----------------------------------------------------------------------------------------

    private void DrawTree(AssetIndex index)
    {
        var flags = ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (_path == FavouritesPath) flags |= ImGuiTreeNodeFlags.Selected;
        ImGui.PushStyleColor(ImGuiCol.Text, Gold);
        ImGui.TreeNodeEx($"{Icons.Star} {LM.Get("GUI_AssetsBrowser_Favourites")}  ({FavouriteSet().Count})##fav_node", flags);
        ImGui.PopStyleColor();
        if (ImGui.IsItemClicked()) Navigate(FavouritesPath, reveal: false);

        var rootFlags = ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (_path.Length == 0) rootFlags |= ImGuiTreeNodeFlags.Selected;
        bool open = ImGui.TreeNodeEx($"{Icons.FolderOpen} {LM.Get("GUI_AssetsBrowser_Root")}  ({index.Root.Total})##root_node", rootFlags);
        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen()) Navigate("", reveal: false);
        if (open)
        {
            foreach (var child in index.Root.Children) DrawFolderNode(child);
            ImGui.TreePop();
        }
        _expandTo = null;
    }

    private void DrawFolderNode(AssetFolder folder)
    {
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (folder.Children.Count == 0) flags |= ImGuiTreeNodeFlags.Leaf;
        bool current = string.Equals(folder.Path, _path, StringComparison.OrdinalIgnoreCase);
        if (current) flags |= ImGuiTreeNodeFlags.Selected;
        if (_expandTo != null && _expandTo.StartsWith(folder.Path + "/", StringComparison.OrdinalIgnoreCase)) ImGui.SetNextItemOpen(true);
        bool open = ImGui.TreeNodeEx($"{Icons.Folder} {folder.Name}  ({folder.Total})##{folder.Path}", flags);
        if (current && _expandTo != null) ImGui.SetScrollHereY(0.3f);
        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen()) Navigate(folder.Path, reveal: false);
        if (!open) return;
        foreach (var child in folder.Children) DrawFolderNode(child);
        ImGui.TreePop();
    }

    // --- Content -------------------------------------------------------------------------------------

    private void UpdateView(AssetIndex index)
    {
        var key = (index, _path, _search, _hidden.Aggregate(0, (h, k) => h | (1 << (int)k)), _favouritesVersion);
        if (_viewFor == key) return;
        _viewFor = key;
        IReadOnlySet<AssetKind>? kinds = _hidden.Count == 0 ? null : Kinds.Where(k => !_hidden.Contains(k)).ToHashSet();

        if (_path == FavouritesPath)
        {
            var favourites = FavouriteSet();
            _viewFolders = [];
            _viewEntries = [.. index.Entries.Where(e => favourites.Contains(FavouriteKey(e.Key)) && (kinds == null || kinds.Contains(e.Kind))
                && (_search.Length == 0 || e.Name.Contains(_search, StringComparison.OrdinalIgnoreCase)))];
            return;
        }

        var folder = index.Folder(_path) ?? index.Root;
        if (_search.Trim().Length > 0)
        {
            _viewFolders = [];
            _viewEntries = index.Search(folder, _search, kinds);
            return;
        }
        _viewFolders = folder.Children;
        _viewEntries = kinds == null ? folder.Entries : [.. folder.Entries.Where(e => kinds.Contains(e.Kind))];
    }

    private void DrawContent(AssetIndex index)
    {
        UpdateView(index);
        int count = _viewFolders.Count + _viewEntries.Count;
        if (count == 0)
        {
            ImGui.TextDisabled(LM.Get(_search.Length > 0 ? "GUI_AssetsBrowser_NoMatch" : "GUI_AssetsBrowser_Empty"));
            return;
        }
        if (_listView) DrawList(index);
        else DrawGrid(index, count);
        if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.IsAnyItemHovered()) _selected = null;
    }

    private void DrawGrid(AssetIndex index, int count)
    {
        var spacing = ImGui.GetStyle().ItemSpacing;
        var tile = new Vector2(_tileSize + Pad * 2f, _tileSize + Pad * 2f + Strip + ImGui.GetTextLineHeight() * 2f);
        int columns = Math.Max(1, (int)((ImGui.GetContentRegionAvail().X + spacing.X) / (tile.X + spacing.X)));
        int rows = (count + columns - 1) / columns;

        if (_scrollToSelected && _selected is { } wanted)
        {
            int at = _viewEntries.FindIndex(e => e.Key == wanted);
            if (at >= 0) ImGui.SetScrollY((_viewFolders.Count + at) / columns * (tile.Y + spacing.Y));
            _scrollToSelected = false;
        }

        bool placeable = InstancePlacer.Available;
        var clipper = new ImGuiListClipper();
        clipper.Begin(rows, tile.Y + spacing.Y);
        while (clipper.Step())
            for (int row = clipper.DisplayStart; row < clipper.DisplayEnd; row++)
                for (int column = 0; column < columns; column++)
                {
                    int i = row * columns + column;
                    if (i >= count) break;
                    if (column > 0) ImGui.SameLine();
                    if (i < _viewFolders.Count) DrawFolderTile(_viewFolders[i], tile);
                    else DrawAssetTile(index, _viewEntries[i - _viewFolders.Count], tile, placeable);
                }
        clipper.End();
    }

    private void DrawFolderTile(AssetFolder folder, Vector2 size)
    {
        ImGui.PushID(folder.Path);
        var origin = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##folder", size);
        bool hovered = ImGui.IsItemHovered();
        if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) Navigate(folder.Path);
        if (hovered) ImGui.SetTooltip(LM.Get("GUI_AssetsBrowser_FolderTip", folder.Path, folder.Total));

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, origin + size, ImGui.GetColorU32(hovered ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg), 4f);
        var imageMin = origin + new Vector2(Pad);
        var imageMax = imageMin + new Vector2(_tileSize);
        DrawGlyph(dl, Icons.Folder, imageMin, imageMax, ImGui.ColorConvertFloat4ToU32(new Vector4(0.85f, 0.7f, 0.35f, 1f)));
        float textY = imageMax.Y + Strip + 2f;
        dl.AddText(new Vector2(origin.X + Pad, textY), ImGui.GetColorU32(ImGuiCol.Text), Fit(folder.Name, _tileSize));
        dl.AddText(new Vector2(origin.X + Pad, textY + ImGui.GetTextLineHeight()), ImGui.GetColorU32(ImGuiCol.TextDisabled),
            Fit(LM.Get("GUI_AssetsBrowser_FolderCount", folder.Total), _tileSize));
        ImGui.PopID();
    }

    private void DrawAssetTile(AssetIndex index, AssetEntry entry, Vector2 size, bool placeable)
    {
        ImGui.PushID($"{entry.Key}");
        var origin = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##tile", size);
        bool hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        HandleItem(index, entry, placeable);

        var dl = ImGui.GetWindowDrawList();
        bool selected = _selected == entry.Key;
        dl.AddRectFilled(origin, origin + size, ImGui.GetColorU32(selected ? ImGuiCol.Header : hovered ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg), 4f);
        var imageMin = origin + new Vector2(Pad);
        var imageMax = imageMin + new Vector2(_tileSize);
        DrawThumbnail(dl, entry, imageMin, imageMax);
        // The kind's colour under the image, as Unreal marks its asset types.
        dl.AddRectFilled(new Vector2(imageMin.X, imageMax.Y), new Vector2(imageMax.X, imageMax.Y + Strip), ImGui.ColorConvertFloat4ToU32(KindColour(entry.Kind)));
        float textY = imageMax.Y + Strip + 2f;
        dl.AddText(new Vector2(origin.X + Pad, textY), ImGui.GetColorU32(ImGuiCol.Text), Fit(entry.Name, _tileSize));
        dl.AddText(new Vector2(origin.X + Pad, textY + ImGui.GetTextLineHeight()), ImGui.GetColorU32(ImGuiCol.TextDisabled), Fit(KindName(entry.Kind), _tileSize));
        if (IsFavourite(entry.Key)) dl.AddText(new Vector2(imageMax.X - 14f, imageMin.Y + 2f), ImGui.ColorConvertFloat4ToU32(Gold), Icons.Star);
        if (entry.Imported) dl.AddText(imageMin + new Vector2(2f), ImGui.GetColorU32(Gold), "*");
        if (selected) dl.AddRect(origin, origin + size, ImGui.ColorConvertFloat4ToU32(KindColour(entry.Kind)), 4f, ImDrawFlags.None, 2f);
        ImGui.PopID();
    }

    private void DrawList(AssetIndex index)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerV;
        if (!ImGui.BeginTable("##assets_list", 5, flags, ImGui.GetContentRegionAvail())) return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn(LM.Get("GUI_AssetsBrowser_ColName"), ImGuiTableColumnFlags.WidthStretch, 2f);
        ImGui.TableSetupColumn(LM.Get("GUI_AssetsBrowser_ColKind"), ImGuiTableColumnFlags.WidthFixed, 90f);
        ImGui.TableSetupColumn(LM.Get("GUI_AssetsBrowser_ColPath"), ImGuiTableColumnFlags.WidthStretch, 2f);
        ImGui.TableSetupColumn(LM.Get("GUI_AssetsBrowser_ColInfo"), ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn(LM.Get("GUI_AssetsBrowser_ColPlaced"), ImGuiTableColumnFlags.WidthFixed, 60f);
        ImGui.TableHeadersRow();

        float rowHeight = ImGui.GetTextLineHeightWithSpacing();
        if (_scrollToSelected && _selected is { } wanted)
        {
            int at = _viewEntries.FindIndex(e => e.Key == wanted);
            if (at >= 0) ImGui.SetScrollY((_viewFolders.Count + at) * rowHeight);
            _scrollToSelected = false;
        }

        bool placeable = InstancePlacer.Available;
        int count = _viewFolders.Count + _viewEntries.Count;
        var clipper = new ImGuiListClipper();
        clipper.Begin(count, rowHeight);
        while (clipper.Step())
            for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (i < _viewFolders.Count)
                {
                    var folder = _viewFolders[i];
                    ImGui.Selectable($"{Icons.Folder} {folder.Name}##{folder.Path}", false, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick);
                    if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) Navigate(folder.Path);
                    ImGui.TableNextColumn();
                    ImGui.TextDisabled(LM.Get("GUI_AssetsBrowser_Folder"));
                    ImGui.TableNextColumn();
                    ImGui.TextDisabled(folder.Parent?.Path ?? "");
                    ImGui.TableNextColumn();
                    ImGui.TextDisabled(LM.Get("GUI_AssetsBrowser_FolderCount", folder.Total));
                    continue;
                }
                var entry = _viewEntries[i - _viewFolders.Count];
                var iconAt = ImGui.GetCursorScreenPos();
                float iconSize = ImGui.GetTextLineHeight();
                DrawThumbnail(ImGui.GetWindowDrawList(), entry, iconAt, iconAt + new Vector2(iconSize));
                ImGui.Dummy(new Vector2(iconSize));
                ImGui.SameLine();
                string star = IsFavourite(entry.Key) ? $" {Icons.Star}" : "";
                ImGui.Selectable($"{entry.Name}{(entry.Imported ? " *" : "")}{star}##{entry.Key}", _selected == entry.Key,
                    ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick);
                HandleItem(index, entry, placeable);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(KindName(entry.Kind));
                ImGui.TableNextColumn();
                ImGui.TextDisabled(entry.Folder);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.Detail);
                ImGui.TableNextColumn();
                int placed = PlacedCount(index, entry);
                if (placed > 0) ImGui.TextUnformatted(placed.ToString());
            }
        clipper.End();
        ImGui.EndTable();
    }

    private static int PlacedCount(AssetIndex index, AssetEntry entry) =>
        entry.Kind is AssetKind.Moby or AssetKind.Tie or AssetKind.Shrub or AssetKind.Foliage or AssetKind.Effect ? index.References.Placements(entry) : 0;

    /// <summary>Selection, double-click, drag, tooltip and context menu of the item just drawn.</summary>
    private void HandleItem(AssetIndex index, AssetEntry entry, bool placeable)
    {
        bool hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left) || ImGui.IsItemClicked(ImGuiMouseButton.Right)) _selected = entry.Key;
        if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) Open(entry);

        if (placeable && entry.Kind is AssetKind.Moby or AssetKind.Tie && ImGui.BeginDragDropSource(ImGuiDragDropFlags.None))
        {
            var payload = new AssetExplorerFrame.Payload { Kind = (int)(entry.Kind == AssetKind.Tie ? PlaceableKind.Tie : PlaceableKind.Moby), Id = entry.Id };
            unsafe { ImGui.SetDragDropPayload(InstancePlacer.PayloadType, &payload, (nuint)sizeof(AssetExplorerFrame.Payload)); }
            ImGui.TextUnformatted(entry.Name);
            ImGui.EndDragDropSource();
        }

        if (hovered && !ImGui.IsMouseDragging(ImGuiMouseButton.Left) && !ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId))
        {
            string tip = $"{entry.Name}\n{KindName(entry.Kind)}  {entry.Folder}";
            if (entry.Detail.Length > 0) tip += $"\n{entry.Detail}";
            int placed = PlacedCount(index, entry);
            if (placed > 0) tip += "\n" + LM.Get("GUI_AssetsBrowser_Placed", placed);
            if (entry.Imported) tip += "\n" + LM.Get("GUI_AssetsBrowser_Imported");
            ImGui.SetTooltip(tip);
        }

        if (!ImGui.BeginPopupContextItem("##asset_menu")) return;
        ImGui.TextDisabled(entry.Name);
        ImGui.Separator();
        if (ImGui.MenuItem(LM.Get("GUI_AssetsBrowser_Open"), "", false, AssetOpener.CanOpen(entry))) Open(entry);
        if (ImGui.MenuItem(LM.Get("GUI_AssetsBrowser_OpenFolder"))) _reveal = entry.Key;
        if (ImGui.MenuItem(LM.Get("GUI_AssetsBrowser_References"))) ReferenceViewerFrame.Show(entry.Key);
        bool hasInstances = entry.Kind is AssetKind.Moby or AssetKind.Tie or AssetKind.Foliage or AssetKind.UFrag;
        if (hasInstances && ImGui.MenuItem(LM.Get("GUI_AssetsBrowser_SelectInstances"))) AssetOpener.SelectInstances(entry);
        ImGui.Separator();
        if (ImGui.MenuItem(LM.Get("GUI_AssetsBrowser_CopyName"))) ImGui.SetClipboardText(entry.FullName.Length > 0 ? entry.FullName : entry.Name);
        if (ImGui.MenuItem(LM.Get("GUI_AssetsBrowser_CopyId"))) ImGui.SetClipboardText($"{entry.Id:X16}");
        if (ImGui.MenuItem(LM.Get(IsFavourite(entry.Key) ? "GUI_AssetsBrowser_Unfavourite" : "GUI_AssetsBrowser_Favourite"))) ToggleFavourite(entry.Key);
        if (entry.Kind == AssetKind.Tie && ModelImportSession.PendingRegion(EntityManager.Singleton.Regions, entry.Id) is { } pendingRegion)
        {
            ImGui.Separator();
            bool used = ModelImportSession.IsPlaced(pendingRegion, entry.Id);
            if (ImGui.MenuItem(LM.Get("GUI_AssetsBrowser_RemoveImported"), "", false, !used)) RemoveImported(pendingRegion, entry.Id);
            if (used && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(LM.Get("GUI_AssetsBrowser_RemoveUsed"));
        }
        ImGui.EndPopup();
    }

    private void Open(AssetEntry entry)
    {
        _selected = entry.Key;
        if (!AssetOpener.Open(entry)) ShowReferences(entry);
    }

    private static void RemoveImported(EntityRegion region, ulong tuid)
    {
        var window = LunaWindow.Instance;
        if (window.Reader is not { } reader || !ModelImportSession.Remove(region, tuid, reader, window.AssetManager)) return;
        ImportRecords.Forget(ProjectSession.Current, tuid);
        AssetThumbnails.Singleton.Forget(true, tuid);
        EntityManager.StructureVersion++;
    }

    // --- References ----------------------------------------------------------------------------------

    private void ShowReferences(AssetEntry entry)
    {
        _referencesOf = entry;
        _openReferences = true;
    }

    private void DrawReferencesPopup(AssetIndex index)
    {
        if (_openReferences) { ImGui.OpenPopup("##asset_references"); _openReferences = false; }
        if (_referencesOf is not { } entry) return;
        ImGui.SetNextWindowSizeConstraints(new Vector2(320f, 120f), new Vector2(640f, 520f));
        if (!ImGui.BeginPopup("##asset_references")) return;
        ImGui.TextUnformatted($"{KindIcon(entry.Kind)} {entry.Name}");
        ImGui.TextDisabled($"{KindName(entry.Kind)}  {entry.Folder}");
        var refs = index.References;
        int placed = PlacedCount(index, entry);
        if (placed > 0) ImGui.TextDisabled(LM.Get("GUI_AssetsBrowser_Placed", placed));
        if (!entry.Loaded) ImGui.TextDisabled(LM.Get("GUI_AssetsBrowser_NotRead"));
        DrawReferenceList(index, LM.Get("GUI_AssetsBrowser_Uses"), refs.Uses(entry));
        DrawReferenceList(index, LM.Get("GUI_AssetsBrowser_UsedBy"), refs.UsedBy(entry));
        ImGui.EndPopup();
    }

    private void DrawReferenceList(AssetIndex index, string label, IReadOnlyList<AssetKey> keys)
    {
        ImGui.SeparatorText($"{label}  ({keys.Count})");
        if (keys.Count == 0) { ImGui.TextDisabled(LM.Get("GUI_AssetsBrowser_NoReferences")); return; }
        foreach (var key in keys.Take(200))
        {
            var target = index.Get(key);
            ImGui.PushStyleColor(ImGuiCol.Text, KindColour(key.Kind));
            ImGui.TextUnformatted(KindIcon(key.Kind));
            ImGui.PopStyleColor();
            ImGui.SameLine();
            if (ImGui.Selectable($"{target?.Path ?? $"{key.Id:X16}"}##ref_{label}_{key}", false, ImGuiSelectableFlags.AllowDoubleClick) && target != null)
            {
                // A click shows it in the browser; a double-click opens it.
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) { ImGui.CloseCurrentPopup(); AssetOpener.Open(target); }
                else { Navigate(target.Folder); _search = ""; _selected = key; _scrollToSelected = true; _viewFor = null; }
            }
        }
        if (keys.Count > 200) ImGui.TextDisabled($"+{keys.Count - 200}");
    }

    // --- Thumbnails and kinds ------------------------------------------------------------------------

    internal static void DrawThumbnail(ImDrawListPtr dl, AssetEntry entry, Vector2 min, Vector2 max)
    {
        var window = LunaWindow.Instance;
        var assets = window.AssetManager;
        var thumbnails = ThumbnailService.Singleton;
        switch (entry.Kind)
        {
            // Already on the GPU: drawn as they are.
            case AssetKind.Texture when assets != null:
                foreach (ulong id in AssetOpener.TextureIds(entry.Id))
                    if (assets.BuiltTextures.TryGetValue(id, out var gpu))
                    {
                        var image = window.imGuiController.GetOrCreateImGuiBinding(window.GraphicsDevice.ResourceFactory, gpu.DeviceTexture);
                        dl.AddImage(image, min, max, Vector2.UnitY, Vector2.UnitX);
                        return;
                    }
                break;
            case AssetKind.EffectTexture when EffectTextureImage((int)entry.Id) is { } effectTexture:
                dl.AddImage(effectTexture, min, max);
                return;
            // An effect shows the first texture it draws with.
            case AssetKind.Effect when Shared is { } index && thumbnails.EffectTexture(index, entry) is >= 0 and var used
                && EffectTextureImage(used) is { } effectImage:
                dl.AddImage(effectImage, min, max);
                return;
            case AssetKind.Sound when thumbnails.Waveform(entry.Key, entry.Tag) is { } waveform:
                DrawWaveform(dl, waveform, min, max, KindColour(entry.Kind));
                return;
        }
        if (ThumbnailService.Supports(entry.Kind))
        {
            if (thumbnails.Get(entry) is { } thumbnail) { dl.AddImage(thumbnail, min, max); return; }
            if (thumbnails.StateOf(entry.Key) == ThumbnailService.State.Pending)
            {
                DrawGlyph(dl, KindIcon(entry.Kind), min, max, ImGui.ColorConvertFloat4ToU32(KindColour(entry.Kind) * new Vector4(1f, 1f, 1f, 0.35f)));
                DrawSpinner(dl, min, max);
                return;
            }
        }
        DrawGlyph(dl, KindIcon(entry.Kind), min, max, ImGui.ColorConvertFloat4ToU32(KindColour(entry.Kind) * new Vector4(1f, 1f, 1f, 0.8f)));
    }

    private static ImTextureRef? EffectTextureImage(int index)
    {
        var textures = LevelEffectPlayer.Textures;
        if ((uint)index >= (uint)textures.Count || textures[index] is not { } gpu) return null;
        var window = LunaWindow.Instance;
        return window.imGuiController.GetOrCreateImGuiBinding(window.GraphicsDevice.ResourceFactory, gpu.DeviceTexture);
    }

    /// <summary>A sound's waveform squeezed into the box: filled and mirrored about the middle, on a dark ground.</summary>
    internal static void DrawWaveform(ImDrawListPtr dl, SoundWaveform waveform, Vector2 min, Vector2 max, Vector4 colour)
    {
        dl.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(0.06f, 0.07f, 0.09f, 1f)), 2f);
        float mid = (min.Y + max.Y) * 0.5f, half = (max.Y - min.Y) * 0.5f - 1f;
        int columns = Math.Max(1, (int)(max.X - min.X));
        uint fill = ImGui.ColorConvertFloat4ToU32(colour);
        for (int x = 0; x < columns; x++)
        {
            var (low, high) = waveform.Range(x / (float)columns, (x + 1) / (float)columns);
            float peak = MathF.Min(1f, MathF.Max(-low, high)) * half;
            dl.AddRectFilled(new Vector2(min.X + x, mid - peak), new Vector2(min.X + x + 1f, mid + MathF.Max(peak, 0.5f)), fill);
        }
    }

    /// <summary>A small turning arc in the corner: the thumbnail is on its way.</summary>
    private static void DrawSpinner(ImDrawListPtr dl, Vector2 min, Vector2 max)
    {
        float radius = Math.Clamp((max.X - min.X) * 0.06f, 3f, 7f);
        var centre = new Vector2(max.X - radius - 3f, max.Y - radius - 3f);
        float start = (float)ImGui.GetTime() * 6f;
        dl.PathArcTo(centre, radius, start, start + MathF.PI * 1.4f, 12);
        dl.PathStroke(ImGui.GetColorU32(ImGuiCol.TextDisabled), ImDrawFlags.None, 1.5f);
    }

    private static void DrawGlyph(ImDrawListPtr dl, string glyph, Vector2 min, Vector2 max, uint colour)
    {
        float scale = Math.Clamp((max.X - min.X) / 48f, 1f, 3f);
        var font = ImGui.GetFont();
        float size = ImGui.GetFontSize() * scale;
        var glyphSize = ImGui.CalcTextSize(glyph) * scale;
        unsafe { dl.AddText(font, size, (min + max - glyphSize) * 0.5f, colour, glyph); }
    }

    public static string KindIcon(AssetKind kind) => kind switch
    {
        AssetKind.Moby => Icons.Cube,
        AssetKind.Tie => Icons.Cubes,
        AssetKind.Shrub => Icons.Seedling,
        AssetKind.Foliage => Icons.Tree,
        AssetKind.UFrag => Icons.MountainSun,
        AssetKind.Effect => Icons.Fire,
        AssetKind.EffectTexture or AssetKind.Texture => Icons.Image,
        AssetKind.Shader => Icons.Palette,
        AssetKind.Sound => Icons.VolumeHigh,
        AssetKind.TextTable => Icons.Font,
        _ => Icons.File,
    };

    public static Vector4 KindColour(AssetKind kind) => kind switch
    {
        AssetKind.Moby => new(0.35f, 0.6f, 1f, 1f),
        AssetKind.Tie => new(0.2f, 0.8f, 0.85f, 1f),
        AssetKind.Shrub => new(0.5f, 0.85f, 0.3f, 1f),
        AssetKind.Foliage => new(0.3f, 0.65f, 0.25f, 1f),
        AssetKind.UFrag => new(0.75f, 0.58f, 0.38f, 1f),
        AssetKind.Effect => new(1f, 0.55f, 0.15f, 1f),
        AssetKind.EffectTexture => new(1f, 0.45f, 0.55f, 1f),
        AssetKind.Texture => new(0.9f, 0.3f, 0.3f, 1f),
        AssetKind.Shader => new(0.35f, 0.8f, 0.45f, 1f),
        AssetKind.Sound => new(0.65f, 0.45f, 0.95f, 1f),
        AssetKind.TextTable => new(0.75f, 0.75f, 0.75f, 1f),
        _ => Vector4.One,
    };

    public static string KindName(AssetKind kind) => LM.Get($"GUI_AssetsBrowser_Kind_{kind}");

    // --- Favourites ----------------------------------------------------------------------------------

    private static string GameKey => LunaWindow.Instance.Level?.GameDefinition?.Id.ToString() ?? "unknown";

    /// <summary>The Asset Explorer's keys for Mobys and Ties, so favourites carry over; the kind's name for the rest.</summary>
    private static string FavouriteKey(AssetKey key) => key.Kind switch
    {
        AssetKind.Moby => $"M:{key.Id:X}",
        AssetKind.Tie => $"T:{key.Id:X}",
        _ => $"{key.Kind}:{key.Id:X}",
    };

    private HashSet<string>? _favourites;
    private string? _favouritesGame;

    private HashSet<string> FavouriteSet()
    {
        if (_favourites == null || _favouritesGame != GameKey)
        {
            _favouritesGame = GameKey;
            _favourites = Program.Settings.AssetFavourites.TryGetValue(GameKey, out var list) ? [.. list] : [];
        }
        return _favourites;
    }

    private bool IsFavourite(AssetKey key) => FavouriteSet().Contains(FavouriteKey(key));

    private void ToggleFavourite(AssetKey key)
    {
        var favourites = Program.Settings.AssetFavourites;
        if (!favourites.TryGetValue(GameKey, out var list)) favourites[GameKey] = list = [];
        string id = FavouriteKey(key);
        if (!list.Remove(id)) list.Add(id);
        Program.Settings.SaveSettingsToFile();
        _favourites = null;
        _favouritesVersion++;
    }

    // --- Helpers -------------------------------------------------------------------------------------

    private static void Tip(string text)
    {
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(text);
    }

    /// <summary>The name cut to fit <paramref name="width"/>, with an ellipsis.</summary>
    internal static string Fit(string name, float width)
    {
        if (ImGui.CalcTextSize(name).X <= width) return name;
        int low = 1, high = name.Length;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (ImGui.CalcTextSize(name[..mid] + "...").X <= width) low = mid; else high = mid - 1;
        }
        return name[..low] + "...";
    }
}
