using System.Numerics;
using ReLunacy.Engine.Assets.Cubemaps;
using ReLunacy.Engine.Rendering.Resources;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>Looks around inside an environment cubemap, the way the reflections sample it.
///
/// This is deliberately not the Texture Explorer's cross of six faces. That view decodes
/// <see cref="Cubemap.Faces"/>, which is an A8R8G8B8 RE-ENCODING built for previews: it folds the
/// fourth half into rgb and stores an exponent in alpha, so it cannot show what the shaders read.
/// This one samples <see cref="Cubemap.HalfFloatFaces"/> - the exact bytes uploaded as
/// R16G16B16A16_Float - and reconstructs radiance as the shaders do, rgb * max(a + bias, 0), with
/// the bias left at 0 here since it is authored per material rather than per cube.
///
/// The projection is done on the CPU. A retail cube is 16x16 per face, so a few hundred thousand
/// samples per rebuild costs nothing, and it avoids a second Vulkan render target for a debug view.
/// The image is rebuilt only when something about it changes, not every frame.</summary>
public class CubemapViewer : DockedFrame, ILevelListener
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetWorkCenter(ImGui.GetMainViewport());
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.None;

    /// <summary>How a texel becomes a colour. The first is what the shaders do today; the others are
    /// here so a wrong reading can be ruled out by looking rather than by argument.</summary>
    private enum Reconstruction { LinearBias, Exp2, TintOnly, IntensityOnly }

    /// <summary>Which half goes to which channel. The reader takes the file order as r,g,b,intensity;
    /// the alternatives are the orders RSX formats are otherwise written in.</summary>
    private enum Swizzle { Rgba, Bgra, Argb, Abgr }

    private readonly List<Cubemap> cubes = [];
    private int selected;

    private float yaw = 0f, pitch = 0f;
    // Horizontal field of view. 90 is the width of one cube face, so it frames exactly one face
    // looking down an axis - useful for judging a single face, while a wide angle shows the seams
    // between several at once.
    private float fovDeg = 90f;
    private float exposureEv;
    private Reconstruction recon = Reconstruction.LinearBias;
    private Swizzle swizzle = Swizzle.Rgba;
    // The per-material +0x68 bias the shaders add to alpha before clamping. Authored per material,
    // so there is no single right value here - the slider is for finding what a surface wants.
    private float bias;
    // exp2((a * 255 + k1) / k2): the old-engine decode, whose constants differ per game
    // (-128 / 16 for the new engine's preview re-encode, -80 / 35 for the retail ToD Tie shaders).
    private float expK1 = -128f, expK2 = 16f;
    private float gamma = 2.2f;
    private bool reinhard = true;

    // Which stored face feeds each cube direction. Cubemap.FaceNames documents the GL/RSX order
    // (+X, -X, +Y, -Y, +Z, -Z) and the old engine matches it, but the new-engine cubes come from a
    // different authoring path and the arrangement does not look right, so it is adjustable rather
    // than asserted. Identity is the documented order.
    private readonly int[] faceMap = [0, 1, 2, 3, 4, 5];
    private bool flipU, flipV, swapUv;

    private float leftWidth = 320f;
    private const float SplitterWidth = 6f;

    private GpuTexture? viewTexture;
    private ImTextureRef viewPtr;
    private int viewW, viewH;
    // The aspect the last rebuild projected with. The render resolution is capped for CPU cost while
    // the image is stretched over the whole pane, so the rays have to be built for the pane's shape,
    // not the texture's, or a capped render would come out distorted.
    private float viewAspect = 1f;
    private bool dirty = true;
    // Halves the rebuild while the view is being dragged: the projection is CPU side, so a full
    // 1024x768 rebuild per drag frame is what would make looking around feel heavy.
    private bool dragging;

    // Whatever the last rebuild measured, so the numbers next to the image describe what is on it.
    private Vector3 meanRadiance, maxRadiance;

    public CubemapViewer() : base()
    {
        FrameName = LM.Get("GUI_Frame_CubemapViewer");
    }

    public void TransmitCubemaps(ReLunacy.Engine.Loading.Readers.LevelData? level)
    {
        cubes.Clear();
        selected = 0;
        if (level != null)
            foreach (var cube in level.Cubemaps)
                if (cube.HalfFloatFaces is { Count: 6 })
                    cubes.Add(cube);
        dirty = true;
    }

    public void OnLevelLoaded() => TransmitCubemaps(LunaWindow.Instance.Level);

    /// <summary>Selects the first cube of a cubemaps.dat RESOURCE - the identifier instances carry -
    /// so a jump from an instance lands on the cube that instance actually reflects.</summary>
    public bool SelectByResource(ulong resourceTuid)
    {
        int at = cubes.FindIndex(c => c.ResourceTuid == resourceTuid);
        if (at < 0) return false;
        selected = at;
        dirty = true;
        return true;
    }

    public void OnLevelUnloading()
    {
        cubes.Clear();
        DisposeView();
    }

    private void DisposeView()
    {
        viewTexture?.Dispose();
        viewTexture = null;
        viewW = viewH = 0;
    }

    protected override void Render(double deltaTime)
    {
        if (cubes.Count == 0)
        {
            ImGui.TextWrapped(LM.Get("GUI_Frame_CubemapViewer_None"));
            return;
        }

        selected = Math.Clamp(selected, 0, cubes.Count - 1);

        // Settings and stats on the left, the render on the right, with a draggable splitter. Kept
        // in a field rather than a fraction of the window so the split survives resizing the frame.
        float total = ImGui.GetContentRegionAvail().X;
        leftWidth = Math.Clamp(leftWidth, 240f, MathF.Max(240f, total - 200f));

        if (ImGui.BeginChild("cubemap_settings", new Vector2(leftWidth, 0f), ImGuiChildFlags.Borders))
            RenderSettings();
        ImGui.EndChild();

        ImGui.SameLine(0f, 0f);
        ImGui.InvisibleButton("##cubemap_splitter", new Vector2(SplitterWidth, MathF.Max(1f, ImGui.GetContentRegionAvail().Y)));
        if (ImGui.IsItemActive()) leftWidth += ImGui.GetIO().MouseDelta.X;
        if (ImGui.IsItemHovered() || ImGui.IsItemActive()) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);
        ImGui.SameLine(0f, 0f);

        if (ImGui.BeginChild("cubemap_preview", Vector2.Zero, ImGuiChildFlags.Borders))
        {
            if (ImGui.BeginTabBar("cubemap_tabs"))
            {
                if (ImGui.BeginTabItem(LM.Get("GUI_Frame_CubemapViewer_TabView")))
                {
                    RenderPreview();
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem(LM.Get("GUI_Frame_CubemapViewer_TabFaces")))
                {
                    RenderFaces();
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
        }
        ImGui.EndChild();
    }

    private void RenderSettings()
    {
        // Both identifiers, because they are different spaces and get mistaken for each other: the
        // RESOURCE tuid is what cubemaps.dat and every instance reference (EnvironmentCubeResources is
        // keyed by it), while the per-cube key is one of the four cubes inside that resource.
        var names = cubes.Select(c => $"{c.ResourceTuid:X16} [{c.IndexInResource}]  key {c.Id:X16}").ToArray();
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.Combo("##cube", ref selected, names, names.Length)) dirty = true;

        if (ImGui.Button(LM.Get("GUI_Frame_CubemapViewer_ResetView")))
        {
            yaw = pitch = 0f; fovDeg = 90f; exposureEv = 0f; bias = 0f; expK1 = -128f; expK2 = 16f; gamma = 2.2f;
            recon = Reconstruction.LinearBias; swizzle = Swizzle.Rgba; reinhard = true;
            for (int i = 0; i < 6; i++) faceMap[i] = i;
            flipU = flipV = swapUv = false;
            dirty = true;
        }

        ImGui.SeparatorText(LM.Get("GUI_Frame_CubemapViewer_Stats_Header"));
        ImGui.TextWrapped(LM.Get("GUI_Frame_CubemapViewer_Stats",
            meanRadiance.X, meanRadiance.Y, meanRadiance.Z,
            maxRadiance.X, maxRadiance.Y, maxRadiance.Z));

        ImGui.SeparatorText(LM.Get("GUI_Frame_CubemapViewer_Decode"));
        int reconIndex = (int)recon;
        string[] reconNames = [
            LM.Get("GUI_Frame_CubemapViewer_ReconLinear"),
            LM.Get("GUI_Frame_CubemapViewer_ReconExp2"),
            LM.Get("GUI_Frame_CubemapViewer_ReconTint"),
            LM.Get("GUI_Frame_CubemapViewer_ReconIntensity")];
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.Combo("##recon", ref reconIndex, reconNames, reconNames.Length))
        { recon = (Reconstruction)reconIndex; dirty = true; }

        if (recon == Reconstruction.LinearBias)
        {
            ImGui.SetNextItemWidth(-90f);
            if (ImGui.SliderFloat(LM.Get("GUI_Frame_CubemapViewer_Bias"), ref bias, -4f, 2f, "%.3f")) dirty = true;
        }
        else if (recon == Reconstruction.Exp2)
        {
            ImGui.SetNextItemWidth(-90f);
            if (ImGui.DragFloat(LM.Get("GUI_Frame_CubemapViewer_ExpK1"), ref expK1, 0.5f, -256f, 256f, "%.1f")) dirty = true;
            ImGui.SetNextItemWidth(-90f);
            if (ImGui.DragFloat(LM.Get("GUI_Frame_CubemapViewer_ExpK2"), ref expK2, 0.1f, 0.5f, 128f, "%.2f")) dirty = true;
        }

        int swizzleIndex = (int)swizzle;
        string[] swizzleNames = ["r,g,b,a (as read)", "b,g,r,a", "a,r,g,b", "a,b,g,r"];
        ImGui.SetNextItemWidth(-90f);
        if (ImGui.Combo(LM.Get("GUI_Frame_CubemapViewer_Swizzle"), ref swizzleIndex, swizzleNames, swizzleNames.Length))
        { swizzle = (Swizzle)swizzleIndex; dirty = true; }

        ImGui.SeparatorText(LM.Get("GUI_Frame_CubemapViewer_FaceOrder"));
        string[] slotNames = Enumerable.Range(0, 6).Select(i => LM.Get("GUI_Frame_CubemapViewer_FaceSlot", i, Cubemap.FaceNames[i])).ToArray();
        for (int dir = 0; dir < 6; dir++)
        {
            int src = faceMap[dir];
            ImGui.SetNextItemWidth(-90f);
            if (ImGui.Combo($"{Cubemap.FaceNames[dir]}##face{dir}", ref src, slotNames, slotNames.Length))
            { faceMap[dir] = src; dirty = true; }
        }
        if (ImGui.Checkbox(LM.Get("GUI_Frame_CubemapViewer_FlipU"), ref flipU)) dirty = true;
        ImGui.SameLine();
        if (ImGui.Checkbox(LM.Get("GUI_Frame_CubemapViewer_FlipV"), ref flipV)) dirty = true;
        if (ImGui.Checkbox(LM.Get("GUI_Frame_CubemapViewer_SwapUV"), ref swapUv)) dirty = true;

        ImGui.SeparatorText(LM.Get("GUI_Frame_CubemapViewer_Camera"));
        ImGui.SetNextItemWidth(-90f);
        if (ImGui.SliderFloat(LM.Get("GUI_Frame_CubemapViewer_Fov"), ref fovDeg, 10f, 170f, "%.0f deg")) dirty = true;

        ImGui.SeparatorText(LM.Get("GUI_Frame_CubemapViewer_Display"));
        ImGui.SetNextItemWidth(-90f);
        if (ImGui.SliderFloat(LM.Get("GUI_Frame_CubemapViewer_Exposure"), ref exposureEv, -8f, 8f, "%.2f EV")) dirty = true;
        ImGui.SetNextItemWidth(-90f);
        if (ImGui.SliderFloat(LM.Get("GUI_Frame_CubemapViewer_Gamma"), ref gamma, 1f, 3f, "%.2f")) dirty = true;
        if (ImGui.Checkbox(LM.Get("GUI_Frame_CubemapViewer_Tonemap"), ref reinhard)) dirty = true;
    }

    private void RenderPreview()
    {
        // The image fills the pane whatever it was rendered at: the projection is CPU side, so the
        // render is capped by a pixel budget and stretched, rather than letting a large frame cost
        // proportionally more. Halved again while dragging, which is invisible at this scale.
        var drawn = ImGui.GetContentRegionAvail();
        drawn = new Vector2(MathF.Max(drawn.X, 16f), MathF.Max(drawn.Y, 16f));
        float aspect = drawn.X / drawn.Y;

        const int PixelBudget = 1280 * 720;
        int w = (int)drawn.X, h = (int)drawn.Y;
        if (w * h > PixelBudget)
        {
            float k = MathF.Sqrt(PixelBudget / (float)(w * h));
            w = (int)(w * k); h = (int)(h * k);
        }
        if (dragging) { w /= 2; h /= 2; }
        // Rounded so a pixel of resize does not rebuild the image every frame.
        w = Math.Max(64, w / 8 * 8);
        h = Math.Max(64, h / 8 * 8);
        if (w != viewW || h != viewH || MathF.Abs(aspect - viewAspect) > 0.001f) dirty = true;

        if (dirty) Rebuild(w, h, aspect);
        if (viewTexture == null) return;

        var topLeft = ImGui.GetCursorScreenPos();
        ImGui.Image(viewPtr, drawn);

        if (!ImGui.IsItemHovered()) { if (dragging) { dragging = false; dirty = true; } return; }

        dragging = ImGui.IsMouseDragging(ImGuiMouseButton.Left);
        if (dragging)
        {
            var d = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left);
            ImGui.ResetMouseDragDelta(ImGuiMouseButton.Left);
            yaw -= d.X * 0.005f;
            pitch = Math.Clamp(pitch + d.Y * 0.005f, -1.55f, 1.55f);
            dirty = true;
        }

        var m = ImGui.GetMousePos() - topLeft;
        var dir2 = RayFor((int)(m.X / drawn.X * viewW), (int)(m.Y / drawn.Y * viewH), viewW, viewH);
        var raw = Sample(cubes[selected], dir2);
        var outv = Reconstruct(raw);
        ImGui.SetTooltip(LM.Get("GUI_Frame_CubemapViewer_Texel",
            raw.r, raw.g, raw.b, raw.a, outv.X, outv.Y, outv.Z));
    }

    /// <summary>The six stored faces of the selected cube, each with its own texture id and the range
    /// its half-float channels actually span. The ids here are the preview face textures the Texture
    /// Explorer knows; the radiance the renderer samples comes from the half-float faces measured
    /// alongside them.</summary>
    private void RenderFaces()
    {
        var cube = cubes[selected];
        ImGui.Text(LM.Get("GUI_Frame_CubemapViewer_FaceResourceLabel", cube.IndexInResource));
        ImGui.SameLine();
        TuidLink.Draw(cube.ResourceTuid);
        ImGui.Text(LM.Get("GUI_Frame_CubemapViewer_FaceKeyLabel"));
        ImGui.SameLine();
        TuidLink.Draw(cube.Id);
        ImGui.Separator();

        if (!ImGui.BeginTable("cubemap_faces", 6,
                ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders))
            return;
        ImGui.TableSetupColumn(LM.Get("GUI_Frame_CubemapViewer_ColFace"));
        ImGui.TableSetupColumn(LM.Get("GUI_Frame_CubemapViewer_ColTexture"));
        ImGui.TableSetupColumn(LM.Get("GUI_Frame_CubemapViewer_ColSize"));
        ImGui.TableSetupColumn(LM.Get("GUI_Frame_CubemapViewer_ColFormat"));
        ImGui.TableSetupColumn(LM.Get("GUI_Frame_CubemapViewer_ColTint"));
        ImGui.TableSetupColumn(LM.Get("GUI_Frame_CubemapViewer_ColIntensity"));
        ImGui.TableHeadersRow();

        for (int f = 0; f < 6 && f < cube.HalfFloatFaces!.Count; f++)
        {
            byte[] data = cube.HalfFloatFaces[f];
            float rMin = float.MaxValue, rMax = float.MinValue, aMin = float.MaxValue, aMax = float.MinValue;
            for (int i = 0; i + 8 <= data.Length; i += 8)
                for (int k = 0; k < 4; k++)
                {
                    float v = (float)BitConverter.ToHalf(data, i + k * 2);
                    if (k < 3) { rMin = MathF.Min(rMin, v); rMax = MathF.Max(rMax, v); }
                    else { aMin = MathF.Min(aMin, v); aMax = MathF.Max(aMax, v); }
                }

            var preview = f < cube.Faces.Count ? cube.Faces[f] : null;
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0); ImGui.Text(Cubemap.FaceNames[f]);
            ImGui.TableSetColumnIndex(1);
            if (preview != null) TuidLink.Draw(preview.Id); else ImGui.Text("-");
            ImGui.TableSetColumnIndex(2); ImGui.Text($"{cube.FaceSize}x{cube.FaceSize}");
            ImGui.TableSetColumnIndex(3); ImGui.Text(preview?.Format.ToString() ?? "-");
            ImGui.TableSetColumnIndex(4); ImGui.Text($"{rMin:0.###} .. {rMax:0.###}");
            ImGui.TableSetColumnIndex(5); ImGui.Text($"{aMin:0.###} .. {aMax:0.###}");
        }
        ImGui.EndTable();
    }

    /// <summary>The view ray for one output pixel, at the current horizontal field of view.</summary>
    private Vector3 RayFor(int px, int py, int w, int h)
    {
        float cp = MathF.Cos(pitch), sp = MathF.Sin(pitch);
        var forward = new Vector3(MathF.Sin(yaw) * cp, -sp, MathF.Cos(yaw) * cp);
        var right = new Vector3(MathF.Cos(yaw), 0f, -MathF.Sin(yaw));
        var up = Vector3.Cross(right, forward);
        // tan(fov / 2); at 90 degrees this is 1, which is the plain unit-plane projection.
        float t = MathF.Tan(Math.Clamp(fovDeg, 5f, 175f) * MathF.PI / 360f);
        float sx = (2f * (px + 0.5f) / w - 1f) * viewAspect * t;
        float sy = (1f - 2f * (py + 0.5f) / h) * t;
        return Vector3.Normalize(forward + right * sx + up * sy);
    }

    private void Rebuild(int w, int h, float aspect)
    {
        dirty = false;
        viewAspect = aspect;
        var cube = cubes[selected];
        var pixels = new byte[w * h * 4];
        float gain = MathF.Pow(2f, exposureEv);
        var sum = Vector3.Zero; var peak = Vector3.Zero;

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Vector3 v = Reconstruct(Sample(cube, RayFor(x, y, w, h)));
                sum += v;
                peak = Vector3.Max(peak, v);
                v *= gain;
                if (reinhard) v = new Vector3(v.X / (1 + v.X), v.Y / (1 + v.Y), v.Z / (1 + v.Z));
                int o = (y * w + x) * 4;
                pixels[o + 0] = Encode(v.X, gamma);
                pixels[o + 1] = Encode(v.Y, gamma);
                pixels[o + 2] = Encode(v.Z, gamma);
                pixels[o + 3] = 255;
            }

        meanRadiance = sum / (w * h);
        maxRadiance = peak;

        DisposeView();
        viewW = w; viewH = h;
        viewTexture = new GpuTexture(LunaWindow.Instance.GraphicsDevice, (uint)w, (uint)h, pixels, false);
        viewPtr = LunaWindow.Instance.imGuiController.GetOrCreateImGuiBinding(
            LunaWindow.Instance.GraphicsDevice.ResourceFactory, viewTexture.DeviceTexture);
    }

    private static byte Encode(float v, float gamma) =>
        (byte)Math.Clamp(MathF.Pow(MathF.Max(v, 0f), 1f / MathF.Max(gamma, 0.01f)) * 255f + 0.5f, 0f, 255f);

    /// <summary>Applies the chosen channel order, then the chosen reconstruction. Everything the
    /// image shows and everything the tooltip reports goes through here, so the numbers can never
    /// describe a different decode than the picture.</summary>
    private Vector3 Reconstruct((float r, float g, float b, float a) t)
    {
        var (c0, c1, c2, c3) = t;
        (float r, float g, float b, float a) = swizzle switch
        {
            Swizzle.Bgra => (c2, c1, c0, c3),
            Swizzle.Argb => (c1, c2, c3, c0),
            Swizzle.Abgr => (c3, c2, c1, c0),
            _ => (c0, c1, c2, c3),
        };
        return recon switch
        {
            Reconstruction.TintOnly => new Vector3(r, g, b),
            Reconstruction.IntensityOnly => new Vector3(MathF.Max(a, 0f)),
            Reconstruction.Exp2 => new Vector3(r, g, b) * MathF.Pow(2f, (a * 255f + expK1) / MathF.Max(expK2, 0.01f)),
            _ => new Vector3(r, g, b) * MathF.Max(a + bias, 0f),
        };
    }

    /// <summary>Bilinear sample of the half-float faces. The direction picks a cube face the usual
    /// way; which STORED face that reads from goes through <see cref="faceMap"/>, and the uv is then
    /// flipped or transposed per the orientation toggles - the whole point being that the new-engine
    /// arrangement is adjustable instead of assumed.</summary>
    private (float r, float g, float b, float a) Sample(Cubemap cube, Vector3 d)
    {
        float ax = MathF.Abs(d.X), ay = MathF.Abs(d.Y), az = MathF.Abs(d.Z);
        int face; float sc, tc, ma;
        if (ax >= ay && ax >= az)
        {
            ma = ax;
            if (d.X > 0) { face = 0; sc = -d.Z; tc = -d.Y; } else { face = 1; sc = d.Z; tc = -d.Y; }
        }
        else if (ay >= az)
        {
            ma = ay;
            if (d.Y > 0) { face = 2; sc = d.X; tc = d.Z; } else { face = 3; sc = d.X; tc = -d.Z; }
        }
        else
        {
            ma = az;
            if (d.Z > 0) { face = 4; sc = d.X; tc = -d.Y; } else { face = 5; sc = -d.X; tc = -d.Y; }
        }
        if (ma <= 0f) return (0f, 0f, 0f, 0f);

        int size = cube.FaceSize;
        byte[] data = cube.HalfFloatFaces![Math.Clamp(faceMap[face], 0, cube.HalfFloatFaces.Count - 1)];
        // GL/Vulkan convention, row 0 at the top - the same mapping the GPU's cube sampler uses.
        // This used to carry a V flip, which made the viewer disagree with the 3D view about what a
        // correctly oriented face looks like.
        float fu = (sc / ma + 1f) * 0.5f;
        float fv = (tc / ma + 1f) * 0.5f;
        if (swapUv) (fu, fv) = (fv, fu);
        if (flipU) fu = 1f - fu;
        if (flipV) fv = 1f - fv;
        float u = fu * size - 0.5f;
        float v = fv * size - 0.5f;
        int x0 = (int)MathF.Floor(u), y0 = (int)MathF.Floor(v);
        float fx = u - x0, fy = v - y0;

        (float r, float g, float b, float a) Texel(int x, int y)
        {
            x = Math.Clamp(x, 0, size - 1); y = Math.Clamp(y, 0, size - 1);
            int o = (y * size + x) * 8;
            if (o + 8 > data.Length) return (0f, 0f, 0f, 0f);
            return ((float)BitConverter.ToHalf(data, o), (float)BitConverter.ToHalf(data, o + 2),
                    (float)BitConverter.ToHalf(data, o + 4), (float)BitConverter.ToHalf(data, o + 6));
        }

        var t00 = Texel(x0, y0); var t10 = Texel(x0 + 1, y0);
        var t01 = Texel(x0, y0 + 1); var t11 = Texel(x0 + 1, y0 + 1);
        static float Mix(float a, float b, float t) => a + (b - a) * t;
        return (Mix(Mix(t00.r, t10.r, fx), Mix(t01.r, t11.r, fx), fy),
                Mix(Mix(t00.g, t10.g, fx), Mix(t01.g, t11.g, fx), fy),
                Mix(Mix(t00.b, t10.b, fx), Mix(t01.b, t11.b, fx), fy),
                Mix(Mix(t00.a, t10.a, fx), Mix(t01.a, t11.a, fx), fy));
    }
}
