using System.Buffers.Binary;
using System.Numerics;
using ReLunacy.Core.Frames.Modals;
using ReLunacy.Engine.Packing;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// The 3D viewport's planet occlusion viewer (read-only): the camera cells of a region's collision.dat (see
/// <see cref="PlanetOcclusion"/>) as wireframe boxes near the camera, the cells that hide the selected Tie, UFrag or Moby
/// in colour, and a preview that hides in the viewport what one cell hides. Parsed on first use per region, dropped with
/// the level. Entities are matched to records by TUID for Ties, by the hash of their world sphere for UFrags and Mobys.
/// </summary>
internal sealed class OcclusionViewer
{
    public bool On;

    private const int MaxBoxes = 1200;
    private const uint NoPick = uint.MaxValue - 1;
    private static readonly Vector4 CellColour = new(0.55f, 0.6f, 0.7f, 0.45f);
    private static readonly Vector4 HidingColour = new(1f, 0.3f, 0.2f, 1f);
    private static readonly Vector4 CameraColour = new(1f, 0.9f, 0.2f, 1f);
    private static readonly Vector4 PreviewColour = new(0.2f, 0.9f, 1f, 1f);

    private sealed class RegionData
    {
        public required string Name;
        public required PlanetOcclusion.Objects Objects;
        public required PlanetOcclusion.Grid Grid;
        public required Vector3[] Centres;
        public required int[] Records;
        public required Dictionary<int, (int X, int Y, int Z)> Cells;
        public readonly Dictionary<ulong, List<byte[]>> TiesByTuid = [];
        public readonly Dictionary<uint, List<byte[]>> UFragsByHash = [];
        public readonly Dictionary<uint, List<byte[]>> MobysByHash = [];
        public readonly Dictionary<int, bool[]> Hiding = [];
    }

    private object? _level;
    private readonly Dictionary<string, RegionData?> _regions = new(StringComparer.OrdinalIgnoreCase);
    private RegionData? _active;
    private float _radius = 60f;

    // The boxes, rebuilt when what they depend on changes.
    private readonly List<(Matrix4x4 World, Vector4 Colour)> _edges = [];
    private (RegionData? Region, int Camera, float Radius, int Bit, int Preview, int Baked) _built;
    private int _shown;
    private int _cameraCell = -1;

    // Selection.
    private Entity? _selected;
    private int _selectedBit = -1;
    private int _hiddenCells;

    // The survey's word on the selection (a bake's cells replace the file's bits), re-read when the survey is remade.
    private OcclusionBakeSurvey? _survey;
    private OcclusionObjectStatus? _status;
    private bool[]? _bakedHiding;
    private int _bakedVersion;

    // Preview.
    private RegionData? _previewRegion;
    private int _previewCell = -1;
    private int _previewHidden;
    private readonly Dictionary<Entity, bool> _saved = [];

    /// <summary>Whether a region of the level has occlusion data; reads the regions' files the first time.</summary>
    public bool Available(EntityManager em, Vector3 camera)
    {
        var level = LunaWindow.Instance.Level;
        if (!ReferenceEquals(level, _level))
        {
            _level = level;
            _regions.Clear();
            _saved.Clear();
            _active = null;
            _previewRegion = null;
            _previewCell = -1;
            _selected = null;
            _built = default;
            _edges.Clear();
        }
        RegionData? first = null, here = null;
        foreach (var region in em.Regions)
        {
            if (!_regions.TryGetValue(region.RegionName, out var data))
                _regions[region.RegionName] = data = Load(region.RegionName);
            if (data == null) continue;
            first ??= data;
            if (here == null && data.Grid.CellAt(camera) >= 0) here = data;
        }
        _active = here ?? (first != null && _active != null && _regions.ContainsValue(_active) ? _active : first);
        return _active != null;
    }

    private static RegionData? Load(string name)
    {
        try
        {
            if (LunaWindow.Instance.fileManager?.ReadOriginal($"{name}/collision.dat") is not { } bytes) return null;
            if (PlanetOcclusion.Read(bytes, out _) is not var (objects, grid)) return null;
            var cells = grid.Cells();
            var records = new int[cells.Count];
            var centres = new Vector3[cells.Count];
            int n = 0;
            foreach (var (record, (x, y, z)) in cells)
            {
                records[n] = record;
                centres[n++] = new Vector3((x + grid.Origin[0] + 0.5f) * grid.CellSize, (y + grid.Origin[1] + 0.5f) * grid.CellSize,
                    (z + grid.Origin[2] + 0.5f) * grid.CellSize);
            }
            var data = new RegionData { Name = name, Objects = objects, Grid = grid, Records = records, Centres = centres, Cells = cells };
            foreach (var r in objects.Tables[PlanetOcclusion.TableB])
                (data.TiesByTuid.TryGetValue(PlanetOcclusion.Objects.Tuid(r), out var l) ? l : data.TiesByTuid[PlanetOcclusion.Objects.Tuid(r)] = []).Add(r);
            foreach (var r in objects.Tables[PlanetOcclusion.TableA])
                (data.UFragsByHash.TryGetValue(PlanetOcclusion.Objects.HashOf(r), out var l) ? l : data.UFragsByHash[PlanetOcclusion.Objects.HashOf(r)] = []).Add(r);
            foreach (var r in objects.Tables[PlanetOcclusion.TableE])
                (data.MobysByHash.TryGetValue(PlanetOcclusion.Objects.HashOf(r), out var l) ? l : data.MobysByHash[PlanetOcclusion.Objects.HashOf(r)] = []).Add(r);
            return data;
        }
        catch (Exception e)
        {
            LunaLog.LogError($"[Occlusion] {name}: {e.Message}");
            return null;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Entity -> record

    private static uint SphereHash(Vector3 centre, float radius)
    {
        Span<byte> b = stackalloc byte[16];
        BinaryPrimitives.WriteSingleBigEndian(b, centre.X);
        BinaryPrimitives.WriteSingleBigEndian(b[4..], centre.Y);
        BinaryPrimitives.WriteSingleBigEndian(b[8..], centre.Z);
        BinaryPrimitives.WriteSingleBigEndian(b[12..], radius);
        return PlanetOcclusion.Hash(b);
    }

    /// <summary>The entity's world sphere without the renderer's culling margin.</summary>
    private static (Vector3 Centre, float Radius) WorldSphere(Entity e)
    {
        var t = e.Transform;
        var local = e.BoundingSphere;
        float scale = MathF.Max(MathF.Abs(t.Scale.X), MathF.Max(MathF.Abs(t.Scale.Y), MathF.Abs(t.Scale.Z)));
        return (Vector3.Transform(new Vector3(local.X, local.Y, local.Z), t.GetMatrix()), local.W * scale);
    }

    /// <summary>The bit the region's records give the entity, or -1 (a Tie, UFrag or Moby no record matches is never hidden).</summary>
    private static int BitOf(RegionData r, Entity e)
    {
        if (e.IsNew) return -1;
        byte[]? record = null;
        switch (e)
        {
            case EntityTie tie when r.TiesByTuid.TryGetValue(tie.Tuid, out var list):
                var (c, rad) = WorldSphere(tie);
                uint hash = SphereHash(c, rad);
                record = list.Find(x => PlanetOcclusion.Objects.HashOf(x) == hash) ?? list[0];
                break;
            case EntityUFrag ufrag:
                if (r.UFragsByHash.TryGetValue(SphereHash(ufrag.UFrag.GetBoundingCenter(), ufrag.UFrag.GetBoundingRadius()), out var us))
                    record = us[0];
                break;
            case EntityMoby moby:
                var (mc, mr) = WorldSphere(moby);
                if (r.MobysByHash.TryGetValue(SphereHash(mc, mr), out var ms)) record = ms[0];
                break;
        }
        if (record == null) return -1;
        int bit = PlanetOcclusion.Objects.Index(record);
        return bit < r.Grid.Bits ? bit : -1;
    }

    private static IEnumerable<Entity> Entities(EntityRegion region)
    {
        foreach (var e in region.MobyInstances.Entities) yield return e;
        foreach (var zone in region.Zones)
        {
            foreach (var e in zone.TieInstances.Entities) yield return e;
            foreach (var e in zone.UFrags.Entities) yield return e;
        }
    }

    /// <summary>For every cell record, whether the object with bit <paramref name="bit"/> is hidden from it.</summary>
    private static bool[] HidingCells(RegionData r, int bit)
    {
        if (r.Hiding.TryGetValue(bit, out var cached)) return cached;
        var g = r.Grid;
        var done = new bool[g.Records];
        var hidden = new bool[g.Records];
        var chain = new Stack<int>();
        for (int i = 0; i < g.Records; i++)
        {
            int at = i;
            while (at < g.Records && !done[at]) { chain.Push(at); at = g.Parent(at); }
            bool value = at < g.Records && hidden[at];
            while (chain.Count > 0)
            {
                int k = chain.Pop();
                if (g.IsWhole(k)) value = (g.Payload[g.StreamOffset + g.DataOffset(k) + (bit >> 3)] >> (bit & 7) & 1) != 0;
                else if (g.Flips(k).Contains(bit)) value = !value;
                hidden[k] = value;
                done[k] = true;
            }
        }
        return r.Hiding[bit] = hidden;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Preview

    private void StopPreview(EntityManager em)
    {
        foreach (var (e, was) in _saved) e.allowRender = was;
        _saved.Clear();
        _previewRegion = null;
        _previewCell = -1;
        _previewHidden = 0;
        em.VisibilityVersion++;
    }

    private void StartPreview(EntityManager em, RegionData r, int cell)
    {
        if (_previewCell >= 0) StopPreview(em);
        var set = r.Grid.Set(cell);
        foreach (var region in em.Regions)
        {
            if (!region.RegionName.Equals(r.Name, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var e in Entities(region))
                if (BitOf(r, e) is var bit and >= 0 && set[bit] && !_saved.ContainsKey(e))
                {
                    _saved[e] = e.allowRender;
                    e.allowRender = false;
                }
        }
        _previewRegion = r;
        _previewCell = cell;
        _previewHidden = _saved.Count;
        em.VisibilityVersion++;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Drawing

    public void DrawPanel(ViewportOverlay overlay, float anchorLeft, Vector3 camera, Entity? selected)
    {
        var em = EntityManager.Singleton;
        if (!On)
        {
            if (_previewCell >= 0) StopPreview(em);
            return;
        }
        var r = _active;
        if (r == null || !overlay.BeginPanel("occlusion", new Vector2(300f, 0f), anchorLeft)) return;

        _cameraCell = r.Grid.CellAt(camera);
        var cells = r.Cells;
        if (_cameraCell >= 0 && cells.TryGetValue(_cameraCell, out var c))
            ImGui.TextUnformatted(LM.Get("GUI_Occlusion_Cell", c.X + r.Grid.Origin[0], c.Y + r.Grid.Origin[1], c.Z + r.Grid.Origin[2]));
        else ImGui.TextDisabled(LM.Get("GUI_Occlusion_NoCell"));

        ImGui.SetNextItemWidth(-1f);
        ImGui.SliderFloat("##occradius", ref _radius, 10f, 400f, LM.Get("GUI_Occlusion_Radius") + " %.0f", ImGuiSliderFlags.Logarithmic);
        ImGui.TextDisabled(LM.Get("GUI_Occlusion_Shown", _shown, r.Records.Length));

        var survey = selected is EntityTie or EntityUFrag ? OcclusionSurveys.Current : null;
        if (!ReferenceEquals(selected, _selected) || !ReferenceEquals(survey, _survey))
        {
            _selected = selected;
            _survey = survey;
            _selectedBit = selected is EntityTie or EntityUFrag or EntityMoby ? BitOf(r, selected) : -1;
            _status = survey != null && selected != null ? survey.Status(selected) : null;
            _bakedHiding = null;
            if (_status is { State: OcclusionState.Baked, HiddenCells: { } baked } && r.Name.Equals(_status.Region, StringComparison.OrdinalIgnoreCase))
            {
                _bakedHiding = new bool[r.Grid.Records];
                foreach (int cell in baked)
                    if (cell >= 0 && cell < _bakedHiding.Length) _bakedHiding[cell] = true;
            }
            _bakedVersion++;
            _hiddenCells = _bakedHiding != null ? _bakedHiding.Count(x => x) : _selectedBit >= 0 ? HidingCells(r, _selectedBit).Count(x => x) : 0;
        }
        if (_selected is EntityTie or EntityUFrag or EntityMoby)
        {
            ImGui.Separator();
            if (_status is { State: not OcclusionState.None } status)
            {
                ImGui.TextUnformatted(LM.Get("GUI_Occlusion_State", LM.Get($"GUI_Occlusion_State{status.State}")));
                if (status.Stale) ImGui.TextColored(new Vector4(1f, 0.7f, 0.3f, 1f), LM.Get("GUI_Occlusion_Stale"));
            }
            if (_bakedHiding != null) ImGui.TextUnformatted(LM.Get("GUI_Occlusion_HiddenIn", _hiddenCells, r.Records.Length));
            else if (_selectedBit >= 0)
            {
                ImGui.TextUnformatted(LM.Get("GUI_Occlusion_Bit", _selectedBit));
                ImGui.TextUnformatted(LM.Get("GUI_Occlusion_HiddenIn", _hiddenCells, r.Records.Length));
            }
            else ImGui.TextDisabled(LM.Get("GUI_Occlusion_NoRecord"));
        }

        ImGui.Separator();
        if (_previewCell >= 0)
        {
            if (cells.TryGetValue(_previewCell, out var p))
                ImGui.TextUnformatted(LM.Get("GUI_Occlusion_Previewing", p.X + r.Grid.Origin[0], p.Y + r.Grid.Origin[1], p.Z + r.Grid.Origin[2], _previewHidden));
            if (ImGui.Button(LM.Get("GUI_Occlusion_StopPreview"), new Vector2(-1f, 0f))) StopPreview(em);
        }
        else
        {
            ImGui.BeginDisabled(_cameraCell < 0);
            if (ImGui.Button(LM.Get("GUI_Occlusion_Preview"), new Vector2(-1f, 0f))) StartPreview(em, r, _cameraCell);
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(LM.Get("GUI_Occlusion_PreviewTip"));
        }
        overlay.EndPanel();
    }

    /// <summary>Adds the boxes to the volumes' wireframe list.</summary>
    public void AppendEdges(List<(Matrix4x4 world, Vector4 color, uint pickId)> list, Vector3 camera)
    {
        var r = _active;
        if (!On || r == null) return;
        int cameraCell = r.Grid.CellAt(camera);
        var key = (r, cameraCell, _radius, _selectedBit, _previewCell, _bakedVersion);
        if (key != _built)
        {
            _built = key;
            Rebuild(r, camera, cameraCell);
        }
        foreach (var (world, colour) in _edges) list.Add((world, colour, NoPick));
    }

    private void Rebuild(RegionData r, Vector3 camera, int cameraCell)
    {
        _edges.Clear();
        var near = new List<(float Distance, int Index)>();
        float limit = _radius * _radius;
        for (int i = 0; i < r.Centres.Length; i++)
        {
            float d = Vector3.DistanceSquared(r.Centres[i], camera);
            if (d <= limit) near.Add((d, i));
        }
        near.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        if (near.Count > MaxBoxes) near.RemoveRange(MaxBoxes, near.Count - MaxBoxes);
        _shown = near.Count;
        var hiding = _bakedHiding ?? (_selectedBit >= 0 ? HidingCells(r, _selectedBit) : null);
        float size = r.Grid.CellSize, half = size * 0.5f;
        void Box(Vector3 c, Vector4 colour)
        {
            for (int a = -1; a <= 1; a += 2)
                for (int b = -1; b <= 1; b += 2)
                {
                    _edges.Add((Matrix4x4.CreateScale(size, 1f, 1f) * Matrix4x4.CreateTranslation(c + new Vector3(0, a * half, b * half)), colour));
                    _edges.Add((Matrix4x4.CreateScale(size, 1f, 1f) * Matrix4x4.CreateRotationZ(MathF.PI / 2f)
                                * Matrix4x4.CreateTranslation(c + new Vector3(a * half, 0, b * half)), colour));
                    _edges.Add((Matrix4x4.CreateScale(size, 1f, 1f) * Matrix4x4.CreateRotationY(MathF.PI / 2f)
                                * Matrix4x4.CreateTranslation(c + new Vector3(a * half, b * half, 0)), colour));
                }
        }
        foreach (var (_, i) in near)
        {
            int record = r.Records[i];
            if (record == cameraCell || record == _previewCell) continue;
            Box(r.Centres[i], hiding != null && hiding[record] ? HidingColour : CellColour);
        }
        // The highlighted cells are drawn even past the cap.
        for (int i = 0; i < r.Records.Length; i++)
        {
            if (r.Records[i] == _previewCell) Box(r.Centres[i], PreviewColour);
            else if (r.Records[i] == cameraCell) Box(r.Centres[i], CameraColour);
        }
    }
}
