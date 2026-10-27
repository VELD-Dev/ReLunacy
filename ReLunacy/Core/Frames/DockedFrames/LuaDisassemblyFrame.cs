using System.Numerics;
using ReLunacy.Engine.Scripting;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// Reverse-engineering view over a precompiled Lua chunk (<c>.lc</c>): its header, its prototype
/// tree, and the decoded instruction listing for the selected prototype with constants resolved.
///
/// This is the surface the compiler gets debugged against. When step 5 compiles one of the shipped
/// sources and the bytes differ, a byte offset says nothing useful; seeing both sides as named
/// instructions is what makes the difference legible.
/// </summary>
public class LuaDisassemblyFrame : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetWorkCenter(ImGui.GetMainViewport());
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.None;

    private string _path = "";
    private string? _error;
    private LuaChunk? _chunk;

    /// <summary>Flattened prototype tree: the listing is per prototype, and a chunk nests them.</summary>
    private readonly List<(string Path, LuaPrototype Proto, int Depth)> _protos = [];
    private int _selected;
    private List<LuaDisassembler.Line> _lines = [];

    public LuaDisassemblyFrame()
    {
        FrameName = LM.Get("GUI_Frame_LuaDisassembly");
    }

    /// <summary>Loads a chunk already in memory - how the PSARC browser and, later, the editor hand
    /// one over without going through the filesystem.</summary>
    public void Load(byte[] bytes, string label)
    {
        _path = label;
        try
        {
            _chunk = LuaChunkReader.Read(bytes);
            _error = null;
            _protos.Clear();
            Flatten(_chunk.Main, "main", 0);
            Select(0);
        }
        catch (Exception e)
        {
            _chunk = null;
            _protos.Clear();
            _lines = [];
            _error = e.Message;
        }
    }

    private void Flatten(LuaPrototype p, string path, int depth)
    {
        _protos.Add((path, p, depth));
        for (int i = 0; i < p.Prototypes.Length; i++)
            Flatten(p.Prototypes[i], $"{path}.{i}", depth + 1);
    }

    private void Select(int index)
    {
        _selected = index;
        _lines = index >= 0 && index < _protos.Count
            ? LuaDisassembler.Disassemble(_protos[index].Proto)
            : [];
    }

    protected override void Render(double deltaTime)
    {
        ImGui.SetNextItemWidth(-140f);
        ImGui.InputText(LM.Get("GUI_Frame_LuaDisassembly_Path"), ref _path, 1024);
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Frame_LuaDisassembly_Load")))
        {
            if (File.Exists(_path)) Load(File.ReadAllBytes(_path), _path);
            else { _error = $"no such file: {_path}"; _chunk = null; _protos.Clear(); _lines = []; }
        }

        if (_error != null)
        {
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), _error);
            return;
        }
        if (_chunk is not { } chunk) { ImGui.TextDisabled(LM.Get("GUI_Frame_LuaDisassembly_Empty")); return; }

        ImGui.Text(chunk.Header.ToString());
        if (!chunk.Header.IsB4404)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), LM.Get("GUI_Frame_LuaDisassembly_NotB4404"));
        }
        ImGui.Separator();

        // Left: the prototype tree. Right: the selected prototype's code. A chunk's prototypes are
        // what the compiler emits one at a time, so this split matches how it will be debugged.
        if (ImGui.BeginChild("lua_protos", new Vector2(260f, 0f), ImGuiChildFlags.Borders))
        {
            for (int i = 0; i < _protos.Count; i++)
            {
                var (path, proto, depth) = _protos[i];
                ImGui.Indent(depth * 10f);
                if (ImGui.Selectable($"{path}  ({proto.Code.Length})###proto{i}", i == _selected))
                    Select(i);
                ImGui.Unindent(depth * 10f);
            }
        }
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("lua_code", new Vector2(0f, 0f), ImGuiChildFlags.Borders))
            RenderSelected();
        ImGui.EndChild();
    }

    private void RenderSelected()
    {
        if (_selected < 0 || _selected >= _protos.Count) return;
        var proto = _protos[_selected].Proto;

        ImGui.Text($"params={proto.ParameterCount}  vararg={proto.IsVararg}  stack={proto.MaxStackSize}  " +
                   $"upvals={proto.UpvalueCount}  consts={proto.Constants.Length}  " +
                   $"protos={proto.Prototypes.Length}  code={proto.Code.Length}");

        if (ImGui.BeginTabBar("lua_code_tabs"))
        {
            if (ImGui.BeginTabItem(LM.Get("GUI_Frame_LuaDisassembly_Code")))
            {
                RenderCode();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem(LM.Get("GUI_Frame_LuaDisassembly_Constants")))
            {
                RenderConstants(proto);
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    private unsafe void RenderCode()
    {
        if (!ImGui.BeginTable("lua_dis", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders
                                          | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit))
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("pc");
        ImGui.TableSetupColumn("raw");
        ImGui.TableSetupColumn("instruction", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("notes", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        // Clipped: instances.lc runs to tens of thousands of instructions, and drawing rows that are
        // scrolled out of view costs as much as the visible ones.
        var clipper = new ImGuiListClipper();
        clipper.Begin(_lines.Count, ImGui.GetTextLineHeightWithSpacing());
        while (clipper.Step())
        {
            for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                var line = _lines[i];
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.Text(line.Pc.ToString());
                ImGui.TableNextColumn(); ImGui.TextDisabled(line.Raw.ToString("X8"));
                ImGui.TableNextColumn(); ImGui.Text(line.Text);
                ImGui.TableNextColumn();
                if (line.Comment is { } c) ImGui.TextDisabled(c);
            }
        }
        clipper.End();
        ImGui.EndTable();
    }

    private static void RenderConstants(LuaPrototype proto)
    {
        if (!ImGui.BeginTable("lua_consts", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders
                                             | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit))
            return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("#");
        ImGui.TableSetupColumn("type");
        ImGui.TableSetupColumn("value", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        for (int i = 0; i < proto.Constants.Length; i++)
        {
            var k = proto.Constants[i];
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.Text($"K{i}");
            ImGui.TableNextColumn();
            ImGui.TextDisabled(k switch
            {
                LuaConstant.Nil => "nil",
                LuaConstant.Number => "number",
                LuaConstant.Str => "string",
                _ => "?",
            });
            ImGui.TableNextColumn(); ImGui.Text(k.ToString() ?? "");
        }
        ImGui.EndTable();
    }
}
