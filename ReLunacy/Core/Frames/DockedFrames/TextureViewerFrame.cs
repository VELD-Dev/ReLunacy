using System.Numerics;
using NeoVeldrid;
using ReLunacy.Engine.Assets.Browser;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Rendering;
using ReLunacy.Engine.Rendering.Resources;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// One window per texture (an AssetKind.Texture or EffectTexture entry): the image with zoom, pan, mip and channel views, and
/// beside it the details and what uses it. The channel labels follow the shader slot that uses the texture.
/// </summary>
public sealed class TextureViewerFrame : DockedFrame, ILevelListener
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetWorkCenter(ImGui.GetMainViewport());
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoScrollbar;

    private enum Role { Plain, Albedo, Normal, Expensive, Detail }

    private const float PanelWidth = 300f;
    private const float MinZoom = 0.02f, MaxZoom = 64f;

    private readonly AssetKey _key;
    private ITexture? _texture;
    private Role _role;
    private string[] _usageSlots = [];
    private long _memory = -1;

    private int _mip;
    private readonly bool[] _channels = [true, true, true, false];
    private bool _alphaAsMask;
    private bool _reconstructed;

    // The decoded texels of the shown mip (raw channels, for the readout) and the image built from them.
    private byte[]? _source;
    private int _sourceMip = -1;
    private int _width, _height;
    private bool _dirty = true;
    private GpuTexture? _gpu;
    private ImTextureRef _image;

    private float _zoom = 1f;
    private Vector2 _pan;
    private bool _fit = true;
    private string _readout = "";

    public AssetKey Key => _key;

    private TextureViewerFrame(AssetEntry entry)
    {
        _key = entry.Key;
        FrameName = entry.Name;
        Resolve();
    }

    /// <summary>Opens the texture's viewer, or focuses the one already open for it; false when the texture cannot be shown.</summary>
    public static bool Open(AssetEntry entry)
    {
        var window = LunaWindow.Instance;
        foreach (var frame in window.openFrames)
            if (frame is TextureViewerFrame { isOpen: true } viewer && viewer._key == entry.Key)
            {
                viewer.Focus();
                return true;
            }
        var created = new TextureViewerFrame(entry);
        if (created._texture == null) return false;
        window.AddFrame(created);
        return true;
    }

    // --- Source ----------------------------------------------------------------------------------------

    private void Resolve()
    {
        var level = LunaWindow.Instance.Level;
        if (level == null) return;
        _texture = _key.Kind switch
        {
            AssetKind.Texture => level.AllTextures.TryGetValue(_key.Id, out var texture) ? texture : null,
            AssetKind.EffectTexture => LevelEffectPlayer.Current?.Textures[(int)_key.Id]?.Texture,
            _ => null,
        };
        if (_texture == null) return;

        var index = AssetsBrowserFrame.EnsureIndex();
        var slots = new List<string>();
        var roles = new HashSet<Role>();
        if (index != null && _key.Kind == AssetKind.Texture)
            foreach (var user in index.References.UsedBy(_key))
            {
                if (user.Kind != AssetKind.Shader || !level.Shaders.TryGetValue(user.Id, out var shader)) continue;
                void Slot(Engine.Loading.Textures.Texture? texture, Role role)
                {
                    if (texture == null || texture.id != _key.Id) return;
                    roles.Add(role);
                    slots.Add($"{user.Id:X}:{(int)role}");
                }
                Slot(shader.Albedo, Role.Albedo);
                Slot(shader.Normal, Role.Normal);
                Slot(shader.Expensive, Role.Expensive);
                Slot(shader.DetailMap, Role.Detail);
            }
        _usageSlots = [.. slots];
        _role = roles.Contains(Role.Expensive) ? Role.Expensive
            : roles.Contains(Role.Normal) ? Role.Normal
            : roles.Contains(Role.Detail) ? Role.Detail
            : roles.Contains(Role.Albedo) ? Role.Albedo : Role.Plain;
        _reconstructed = false;
    }

    private Role SlotOf(ulong shaderId)
    {
        foreach (var slot in _usageSlots)
        {
            int cut = slot.IndexOf(':');
            if (ulong.Parse(slot[..cut], System.Globalization.NumberStyles.HexNumber) == shaderId) return (Role)int.Parse(slot[(cut + 1)..]);
        }
        return Role.Plain;
    }

    public void OnLevelUnloading()
    {
        Release();
        _texture = null;
        _source = null;
        isOpen = false;
    }

    public void OnLevelLoaded() { }

    public override void RenderAsWindow(double deltaTime)
    {
        base.RenderAsWindow(deltaTime);
        if (!isOpen) Release();
    }

    private void Release()
    {
        if (_gpu == null) return;
        LunaWindow.Instance.imGuiController.ReleaseImGuiBinding(_gpu.DeviceTexture);
        _gpu.Dispose();
        _gpu = null;
    }

    // --- Image -----------------------------------------------------------------------------------------

    private int MipCount => Math.Max(1, _texture!.MipmapCount);

    private (int W, int H) MipSize(int mip) =>
        (Math.Max(1, (int)_texture!.Width >> mip), Math.Max(1, (int)_texture.Height >> mip));

    private bool IsSingleChannel(out int channel)
    {
        channel = -1;
        int count = 0;
        for (int i = 0; i < 4; i++)
            if (_channels[i]) { count++; channel = i; }
        return count == 1;
    }

    private void RebuildImage()
    {
        _dirty = false;
        var texture = _texture!;
        if (_sourceMip != _mip || _source == null)
        {
            _source = _mip == 0
                ? TextureUtils.DecodeToRgba8888(texture, out _width, out _height)
                : TextureUtils.DecodeMipmapToRgba8888(texture, _mip, out _width, out _height);
            _sourceMip = _mip;
        }
        if (_source == null) return;

        byte[]? pixels;
        if (_reconstructed)
        {
            pixels = _mip == 0
                ? TextureUtils.ReconstructNormalMap(texture, out _, out _)
                : TextureUtils.ReconstructNormalMap(texture, _mip, out _, out _);
            if (pixels == null) return;
        }
        else
        {
            pixels = new byte[_source.Length];
            bool single = IsSingleChannel(out int channel);
            bool anyColour = _channels[0] || _channels[1] || _channels[2];
            bool transparent = _alphaAsMask || (_channels[3] && anyColour);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (single)
                {
                    byte v = _source[i + channel];
                    pixels[i] = pixels[i + 1] = pixels[i + 2] = v;
                    pixels[i + 3] = _alphaAsMask ? _source[i + 3] : (byte)255;
                    continue;
                }
                pixels[i] = _channels[0] ? _source[i] : (byte)0;
                pixels[i + 1] = _channels[1] ? _source[i + 1] : (byte)0;
                pixels[i + 2] = _channels[2] ? _source[i + 2] : (byte)0;
                pixels[i + 3] = transparent ? _source[i + 3] : (byte)255;
            }
        }

        var window = LunaWindow.Instance;
        Release();
        _gpu = new GpuTexture(window.GraphicsDevice, (uint)_width, (uint)_height, pixels, mipmap: false);
        _image = window.imGuiController.GetOrCreateImGuiBinding(window.GraphicsDevice.ResourceFactory, _gpu.DeviceTexture);
    }

    // --- Layout ----------------------------------------------------------------------------------------

    protected override void Render(double deltaTime)
    {
        if (_texture == null)
        {
            ImGui.TextDisabled(LM.Get("GUI_TextureViewer_Missing"));
            return;
        }
        if (_dirty) RebuildImage();

        var avail = ImGui.GetContentRegionAvail();
        float viewWidth = Math.Max(120f, avail.X - PanelWidth - ImGui.GetStyle().ItemSpacing.X);
        if (ImGui.BeginChild("##texture_view", new Vector2(viewWidth, avail.Y), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar))
            DrawView();
        ImGui.EndChild();
        ImGui.SameLine();
        if (ImGui.BeginChild("##texture_panel", new Vector2(0, avail.Y), ImGuiChildFlags.Borders))
            DrawPanel();
        ImGui.EndChild();
    }

    private void DrawView()
    {
        if (ImGui.Button(LM.Get("GUI_TextureViewer_Fit"))) _fit = true;
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_TextureViewer_Actual")))
        {
            _zoom = 1f;
            _fit = false;
            _pan = Vector2.Zero;
            _centre = true;
        }
        ImGui.SameLine();
        ImGui.TextDisabled($"{_zoom * 100f:0}%");
        if (MipCount > 1)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(130);
            var (mw, mh) = MipSize(_mip);
            if (ImGui.BeginCombo("##mip", LM.Get("GUI_TextureViewer_Mip", _mip, mw, mh)))
            {
                for (int mip = 0; mip < MipCount; mip++)
                {
                    var (w, h) = MipSize(mip);
                    if (ImGui.Selectable(LM.Get("GUI_TextureViewer_Mip", mip, w, h), mip == _mip))
                    {
                        _mip = mip;
                        _dirty = true;
                    }
                }
                ImGui.EndCombo();
            }
        }

        var size = ImGui.GetContentRegionAvail();
        size.Y -= ImGui.GetFrameHeightWithSpacing();
        if (size.X < 8 || size.Y < 8) return;
        DrawCanvas(size);
        ImGui.TextDisabled(_readout.Length > 0 ? _readout : " ");
    }

    private bool _centre;

    private void DrawCanvas(Vector2 size)
    {
        var origin = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##canvas", size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonMiddle);
        bool hovered = ImGui.IsItemHovered();
        var io = ImGui.GetIO();
        float w0 = _texture!.Width, h0 = _texture.Height;

        if (_fit)
        {
            _zoom = Math.Clamp(Math.Min(size.X / w0, size.Y / h0) * 0.95f, MinZoom, MaxZoom);
            _centre = true;
            _fit = false;
        }
        if (_centre)
        {
            _pan = (size - new Vector2(w0, h0) * _zoom) * 0.5f;
            _centre = false;
        }

        var mouse = io.MousePos - origin;
        if (hovered && io.MouseWheel != 0f)
        {
            float zoom = Math.Clamp(_zoom * MathF.Pow(1.15f, io.MouseWheel), MinZoom, MaxZoom);
            // Keep the pixel under the cursor where it is.
            _pan = mouse - (mouse - _pan) / _zoom * zoom;
            _zoom = zoom;
        }
        if (ImGui.IsItemActive() && (ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0f) || ImGui.IsMouseDragging(ImGuiMouseButton.Middle, 0f)))
            _pan += io.MouseDelta;

        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(origin, origin + size, true);
        dl.AddRectFilled(origin, origin + size, ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.1f, 1f)));

        var min = origin + _pan;
        var max = min + new Vector2(w0, h0) * _zoom;
        DrawChecker(dl, Vector2.Max(min, origin), Vector2.Min(max, origin + size), min);
        if (_gpu != null)
            dl.AddImage(_image, min, max, Vector2.UnitY, Vector2.UnitX);
        dl.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.25f)));
        dl.PopClipRect();

        _readout = "";
        var at = io.MousePos;
        if (hovered && _source != null && at.X >= min.X && at.Y >= min.Y && at.X < max.X && at.Y < max.Y)
        {
            int x = Math.Clamp((int)((at.X - min.X) / (max.X - min.X) * _width), 0, _width - 1);
            int row = Math.Clamp((int)((at.Y - min.Y) / (max.Y - min.Y) * _height), 0, _height - 1);
            int y = _height - 1 - row; // the image is drawn with its v axis flipped
            int i = (y * _width + x) * 4;
            _readout = LM.Get("GUI_TextureViewer_Readout", x, row, _source[i], _source[i + 1], _source[i + 2], _source[i + 3]);
        }
    }

    private static void DrawChecker(ImDrawListPtr dl, Vector2 min, Vector2 max, Vector2 anchor)
    {
        if (max.X <= min.X || max.Y <= min.Y) return;
        const float cell = 8f;
        uint light = ImGui.ColorConvertFloat4ToU32(new Vector4(0.45f, 0.45f, 0.45f, 1f));
        uint dark = ImGui.ColorConvertFloat4ToU32(new Vector4(0.32f, 0.32f, 0.32f, 1f));
        dl.AddRectFilled(min, max, light);
        int x0 = (int)MathF.Floor((min.X - anchor.X) / cell), x1 = (int)MathF.Ceiling((max.X - anchor.X) / cell);
        int y0 = (int)MathF.Floor((min.Y - anchor.Y) / cell), y1 = (int)MathF.Ceiling((max.Y - anchor.Y) / cell);
        if ((long)(x1 - x0) * (y1 - y0) > 40000) return;
        for (int cy = y0; cy < y1; cy++)
            for (int cx = x0; cx < x1; cx++)
            {
                if (((cx + cy) & 1) == 0) continue;
                var a = new Vector2(Math.Max(min.X, anchor.X + cx * cell), Math.Max(min.Y, anchor.Y + cy * cell));
                var b = new Vector2(Math.Min(max.X, anchor.X + (cx + 1) * cell), Math.Min(max.Y, anchor.Y + (cy + 1) * cell));
                if (b.X > a.X && b.Y > a.Y) dl.AddRectFilled(a, b, dark);
            }
    }

    // --- Panel -----------------------------------------------------------------------------------------

    private void DrawPanel()
    {
        DrawChannels();
        DrawDetails();
        DrawUsages();
    }

    private string ChannelLabel(int channel) => _role switch
    {
        Role.Expensive => LM.Get($"GUI_TextureViewer_Expensive{"RGBA"[channel]}"),
        Role.Normal => channel switch { 1 => LM.Get("GUI_TextureViewer_NormalG"), 3 => LM.Get("GUI_TextureViewer_NormalA"), _ => "" },
        Role.Detail => LM.Get($"GUI_TextureViewer_Detail{"RGBA"[channel]}"),
        _ => "",
    };

    private bool HasAlpha => _texture!.Format is not (TextureFormat.DXT1 or TextureFormat.R5G6B5 or TextureFormat.R8 or TextureFormat.BC4 or TextureFormat.BC5 or TextureFormat.G8B8);

    private void DrawChannels()
    {
        ImGui.SeparatorText(LM.Get("GUI_TextureViewer_Channels"));
        ImGui.TextDisabled(LM.Get($"GUI_TextureViewer_Role{_role}"));
        for (int c = 0; c < 4; c++)
        {
            if (c == 3 && !HasAlpha) break;
            bool on = _channels[c] && !_reconstructed;
            if (on) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
            if (ImGui.Button($"{"RGBA"[c]}##channel{c}", new Vector2(28, 0)))
            {
                _channels[c] = !_channels[c];
                _reconstructed = false;
                _dirty = true;
            }
            if (on) ImGui.PopStyleColor();
            string label = ChannelLabel(c);
            if (label.Length > 0)
            {
                ImGui.SameLine();
                ImGui.TextUnformatted(label);
            }
        }
        if (ImGui.Button(LM.Get("GUI_TextureViewer_AllChannels")))
        {
            _channels[0] = _channels[1] = _channels[2] = true;
            _channels[3] = false;
            _reconstructed = false;
            _dirty = true;
        }
        if (HasAlpha && ImGui.Checkbox(LM.Get("GUI_TextureViewer_AlphaMask"), ref _alphaAsMask)) _dirty = true;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_TextureViewer_AlphaMaskTip"));
        if (_role == Role.Normal && ImGui.Checkbox(LM.Get("GUI_TextureViewer_Reconstructed"), ref _reconstructed)) _dirty = true;
    }

    private void DrawDetails()
    {
        var texture = _texture!;
        var entry = AssetsBrowserFrame.EnsureIndex()?.Get(_key);
        ImGui.SeparatorText(LM.Get("GUI_TextureViewer_Details"));
        Row("GUI_TextureViewer_Name", entry?.FullName is { Length: > 0 } full ? full : (texture.Name ?? FrameName));
        Row("GUI_TextureViewer_Tuid", _key.Kind == AssetKind.Texture ? $"{_key.Id:X16}" : $"#{_key.Id}");
        if (entry != null) Row("GUI_TextureViewer_Path", entry.Path);
        Row("GUI_TextureViewer_Format", texture.Format.ToString());
        Row("GUI_TextureViewer_Size", $"{texture.Width}x{texture.Height}");
        Row("GUI_TextureViewer_Mips", MipCount.ToString());
        if (_memory < 0) _memory = TextureTools.MemorySize(texture);
        Row("GUI_TextureViewer_Memory", TextureTools.FormatBytes(_memory));
        if (entry != null) Row("GUI_TextureViewer_Source", LM.Get($"GUI_TextureViewer_Source{entry.Source}"));

        if (ImGui.Button(LM.Get("GUI_TextureViewer_ExportPng")))
            TextureTools.ExportPng(texture, TextureTools.ExportFileName(texture.Name, (int)_key.Id));
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_TextureViewer_ExportRaw")))
            TextureTools.ExportRaw(texture, TextureTools.ExportFileName(texture.Name, (int)_key.Id));
        if (ImGui.Button(LM.Get("GUI_TextureViewer_ShowInBrowser")))
            AssetsBrowserFrame.Reveal(_key);
    }

    private static void Row(string label, string value)
    {
        ImGui.TextDisabled(LM.Get(label));
        ImGui.SameLine(90f);
        ImGui.TextWrapped(value);
    }

    private void DrawUsages()
    {
        ImGui.SeparatorText(LM.Get("GUI_TextureViewer_Usages"));
        var index = AssetsBrowserFrame.EnsureIndex();
        var users = index?.References.UsedBy(_key);
        if (index == null || users == null || users.Count == 0)
        {
            ImGui.TextDisabled(LM.Get("GUI_TextureViewer_NoUsages"));
            return;
        }
        foreach (var user in users)
        {
            if (index.Get(user) is not { } entry) continue;
            string slot = entry.Kind == AssetKind.Shader && SlotOf(entry.Id) is var role and not Role.Plain
                ? $"  ({LM.Get($"GUI_TextureViewer_Slot{role}")})" : "";
            if (ImGui.Selectable($"{entry.Name}{slot}##use_{user}")) AssetOpener.Open(entry);
            if (entry.Kind != AssetKind.Shader) continue;
            ImGui.Indent();
            foreach (var cls in index.References.UsedBy(user))
                if (index.Get(cls) is { } classEntry
                    && ImGui.Selectable($"{classEntry.Kind}: {classEntry.Name}##use_{user}_{cls}"))
                    AssetOpener.Open(classEntry);
            ImGui.Unindent();
        }
    }
}
