using System.Buffers.Binary;
using System.Numerics;
using ReLunacy.Core.Selection;
using ReLunacy.Engine.Assets.LevelElements;
using ReLunacy.Engine.Scene;
using ReLunacy.Engine.Scripting;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// The script list with the level's variables, and the inspector: the selected node's fields as the .fields file
/// types them, its undocumented words as hex. Instance indexes show the instance they name in the region the setup
/// runs, and take the one selected in the 3D view.
/// </summary>
public sealed partial class ScriptGraphFrame
{
    private string _filter = "";
    private string _rename = "";
    private ScriptRecord? _deleting;

    // ---------------------------------------------------------------- script list

    private void RenderScriptList()
    {
        if (Setup is not { } setup) return;
        if (ImGuiPlus.IconButton(Icons.Plus, "add_script")) AddScript();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_ScriptGraph_AddScript"));
        ImGui.SameLine();
        ImGui.BeginDisabled(_script == null);
        if (ImGuiPlus.IconButton(Icons.Trash, "delete_script"))
        {
            _deleting = _script;
            ImGui.OpenPopup("##delete_script");
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(LM.Get("GUI_ScriptGraph_DeleteScript"));
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##script_filter", LM.Get("GUI_ScriptGraph_Filter"), ref _filter, 64);
        if (ImGui.BeginPopup("##delete_script"))
        {
            ImGui.TextUnformatted(LM.Get("GUI_ScriptGraph_DeleteScriptConfirm", _deleting?.Name ?? ""));
            if (ImGui.Button(LM.Get("GUI_ScriptGraph_Delete")) && _deleting != null)
            {
                DeleteScript(_deleting);
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button(LM.Get("GUI_ScriptGraph_Cancel"))) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }

        if (_script != null)
        {
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputText("##rename_script", ref _rename, 63, ImGuiInputTextFlags.EnterReturnsTrue) && !RenameScript(_script, _rename.Trim()))
                _rename = _script.Name;
            if (ImGui.IsItemDeactivated() && _rename.Trim() != _script.Name && !RenameScript(_script, _rename.Trim())) _rename = _script.Name;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_ScriptGraph_Rename"));
        }

        float variables = MathF.Min(14f * Unit, ImGui.GetContentRegionAvail().Y * 0.4f);
        if (ImGui.BeginChild("##script_names", new Vector2(0f, -variables)))
            foreach (var script in setup.Scripts)
            {
                if (_filter.Length > 0 && !script.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)) continue;
                if (ImGui.Selectable($"{script.Name}##{setup.Scripts.IndexOf(script)}", script == _script)) SelectScript(script);
            }
        ImGui.EndChild();
        RenderVariables();
    }

    private void RenderVariables()
    {
        ImGui.SeparatorText(LM.Get("GUI_ScriptGraph_Variables"));
        ImGui.SameLine();
        if (ImGuiPlus.IconButton(Icons.Plus, "add_variable"))
        {
            AddVariable();
            Commit("GUI_ScriptGraph_UndoVariable");
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_ScriptGraph_AddVariable"));
        var uses = new Dictionary<(ScriptVariableBlock, int), int>();
        foreach (var pointer in _scripts!.Setups.SelectMany(s => s.Elements).SelectMany(e => e.Pointers.Values).OfType<ScriptVariablePointer>())
            uses[(pointer.Block, pointer.Offset)] = uses.GetValueOrDefault((pointer.Block, pointer.Offset)) + 1;
        if (!ImGui.BeginChild("##variables")) { ImGui.EndChild(); return; }
        foreach (var block in _scripts.Variables)
            for (int offset = 0; offset + 4 <= block.Data.Length; offset += 4)
            {
                ImGui.PushID($"{_scripts.Variables.IndexOf(block)}_{offset}");
                int count = uses.GetValueOrDefault((block, offset));
                if (count == 0) ImGui.TextDisabled(VariableName(block, offset));
                else ImGui.TextUnformatted(VariableName(block, offset));
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_ScriptGraph_Uses", count));
                ImGui.SameLine(5f * Unit);
                float value = block[offset];
                ImGui.SetNextItemWidth(-1f);
                if (ImGui.DragFloat("##value", ref value, 0.1f)) BinaryPrimitives.WriteSingleBigEndian(block.Data.AsSpan(offset), value);
                if (ImGui.IsItemDeactivatedAfterEdit()) Commit("GUI_ScriptGraph_UndoVariable");
                ImGui.PopID();
            }
        ImGui.EndChild();
    }

    // ---------------------------------------------------------------- inspector

    private void RenderInspector()
    {
        if (_script is not { } script) return;
        if (_selected is not { } element || !script.Elements.Contains(element))
        {
            _selected = null;
            ImGui.TextUnformatted(script.Name);
            int dead = script.Elements.Count - Reachable(script).Count;
            ImGui.TextDisabled(LM.Get("GUI_ScriptGraph_ScriptInfo", script.Elements.Count, dead));
            ImGui.Spacing();
            ImGui.TextWrapped(LM.Get("GUI_ScriptGraph_CanvasHint"));
            return;
        }
        uint cls = element.ClassId;
        ImGui.TextUnformatted(Title(cls));
        ImGui.TextDisabled($"{ClassFields.ClassName(Game, cls) ?? "?"} (0x{cls:X})");
        if (!Reachable(script).Contains(element)) ImGui.TextColored(ElseColour, LM.Get("GUI_ScriptGraph_Unreachable"));
        if (element != script.Start && element != script.End)
        {
            if (ImGuiPlus.IconButton(Icons.Trash, "delete_node")) RemoveNode(element);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_ScriptGraph_DeleteNode") + " (Delete)");
            if (_selected == null) return;
        }
        ImGui.Separator();

        // Edit fields: the same documentation as instances' settings, kept in the same table.
        ImGui.Checkbox(LM.Get("GUI_Frame_InstanceInspector_SettingsEditFields"), ref _editFields);
        if (_editFields && Game is { } tableOf)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton(LM.Get("GUI_Frame_InstanceInspector_SettingsEnums"))) PropertyInspectorFrame.EditEnums();
            ImGui.SameLine();
            if (ImGui.SmallButton(LM.Get("GUI_Frame_InstanceInspector_SettingsReload"))) ClassFields.Reload();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Frame_InstanceInspector_FieldsFileHint", ClassFields.FilePath(tableOf)));
        }
        if (Game is { } game) PropertyInspectorFrame.RenderFieldEditors(game, cls, ClassFields.Chain(game, cls));

        var fields = ClassFields.Of(Game, cls);
        if (!ImGui.BeginTable("##fields", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp)) return;
        ImGui.TableSetupColumn("name", ImGuiTableColumnFlags.WidthStretch, 0.4f);
        ImGui.TableSetupColumn("value", ImGuiTableColumnFlags.WidthStretch, 0.6f);
        // Next and Else are the node's pins, so the rows start after them.
        foreach (var (offset, size, field) in ClassFields.Layout(fields, element.Data.Length, 0xC))
        {
            ImGui.PushID(offset);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            if (_editFields && Game != null)
            {
                if (ImGui.SmallButton(field != null ? "..." : "+")) DocumentField(element, offset, size, field);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get(field != null ? "GUI_Frame_InstanceInspector_FieldEdit" : "GUI_Frame_InstanceInspector_FieldDocument"));
                ImGui.SameLine();
            }
            if (field == null) ImGui.TextDisabled($"+0x{offset:X2}");
            else ImGui.TextUnformatted(field.Name.Length > 0 ? field.Name : $"+0x{offset:X2}");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(field is { Description.Length: > 0 } ? $"+0x{offset:X2} {field.TypeText}\n{field.Description}" : $"+0x{offset:X2} {field?.TypeText}");
            ImGui.TableNextColumn();
            ImGui.BeginDisabled(field?.RunTime == true);
            FieldEditor(element, offset, size, field, ImGui.GetContentRegionAvail().X, false);
            ImGui.EndDisabled();
            ImGui.PopID();
        }
        ImGui.EndTable();
    }

    private static bool _editFields;

    /// <summary>Opens the field popup on a row: its field, or a new one typed from what the bytes hold.</summary>
    private static void DocumentField(ScriptElement element, int offset, int size, ClassField? field)
    {
        uint owner = Engine.Loading.Readers.ClassRegistry.Find(Game)?.Owner(element.ClassId, offset) ?? element.ClassId;
        string type = element.Pointers.GetValueOrDefault(offset) switch
        {
            ScriptVariablePointer => "ptr Variable",
            ScriptStringPointer => "ptr String",
            ScriptElementPointer => "ptr Element",
            _ => size == 1 ? "u8" : size == 2 ? "u16" : "u32",
        };
        PropertyInspectorFrame.EditField(field, owner, offset, type);
    }

    /// <summary>One field's editor, <paramref name="width"/> wide; <paramref name="compact"/> keeps it to one
    /// line, as a node shows it.</summary>
    private void FieldEditor(ScriptElement element, int offset, int size, ClassField? field, float width, bool compact)
    {
        var bytes = element.Data.AsSpan(offset, size);
        ImGui.SetNextItemWidth(width);
        if (element.Pointers.ContainsKey(offset) || field?.Kind == ClassFieldKind.Pointer)
        {
            PointerEditor(element, offset, field?.TypeName);
            return;
        }
        switch (field?.Kind)
        {
            case ClassFieldKind.F32 when size == 4:
            {
                float v = BinaryPrimitives.ReadSingleBigEndian(bytes);
                if (ImGui.DragFloat("##v", ref v, 0.05f)) BinaryPrimitives.WriteSingleBigEndian(bytes, v);
                break;
            }
            case ClassFieldKind.S32 when size == 4:
            {
                int v = BinaryPrimitives.ReadInt32BigEndian(bytes);
                string? kindType = ClassFields.At(Game, element.ClassId, offset - 4)?.TypeName;
                if (kindType is "ScriptTarget" or "ScriptArea")
                {
                    PairEditor(element, offset, kindType == "ScriptTarget", width, compact);
                    return;
                }
                if (ImGui.InputInt("##v", ref v)) BinaryPrimitives.WriteInt32BigEndian(bytes, v);
                break;
            }
            case ClassFieldKind.Reference when size == 4:
            {
                string kind = field.TypeName!;
                InstanceEditor(kind, OldClassReferences.Decode(kind, element.Word(offset)), width, compact, (to, at) =>
                {
                    if (to != kind) return false;
                    element.SetWord(offset, OldClassReferences.Encode(kind, at));
                    return true;
                });
                return;
            }
            case ClassFieldKind.Sound or ClassFieldKind.Effect or ClassFieldKind.TextId or ClassFieldKind.MobyClass when size == 4:
            {
                // The number, then what it names, as instances' settings show it.
                int v = BinaryPrimitives.ReadInt32BigEndian(bytes);
                float number = MathF.Min(64f * ImGui.GetFontSize() / 13f, width * 0.35f);
                ImGui.SetNextItemWidth(number);
                if (ImGui.InputInt("##v", ref v, 0)) BinaryPrimitives.WriteInt32BigEndian(bytes, Math.Max(0, v));
                if (ImGui.IsItemDeactivatedAfterEdit()) Commit("GUI_ScriptGraph_UndoEdit");
                float rest = MathF.Max(0f, width - number - ImGui.GetStyle().ItemSpacing.X);
                void Pick(uint value)
                {
                    element.SetWord(offset, value);
                    Commit("GUI_ScriptGraph_UndoEdit");
                }
                if (field.Kind == ClassFieldKind.Sound) PropertyInspectorFrame.SoundEditor(v, false, Pick, rest);
                else if (field.Kind == ClassFieldKind.Effect) PropertyInspectorFrame.EffectEditor((uint)v, false, Pick, rest);
                else if (field.Kind == ClassFieldKind.MobyClass) PropertyInspectorFrame.MobyClassEditor((uint)v, false, Pick, rest);
                else if (v != 0 && Program.ProvidedPath is { Length: > 0 } path
                         && Engine.Config.GameConfigs.GameFolderFor(path) is { } folder && Engine.Text.GameText.Reference(folder) is { } text)
                {
                    ImGui.SameLine();
                    PropertyInspectorFrame.RenderTextFieldLink(v, v > 0 && v < text.Entries.Count ? text.Entries[v].Text : null,
                        Engine.Text.GameText.TagName(Game, v), rest);
                }
                return;
            }
            case ClassFieldKind.U32 or ClassFieldKind.U16 or ClassFieldKind.U8 or ClassFieldKind.TextId or ClassFieldKind.Sound or ClassFieldKind.Effect:
            {
                long v = Read(bytes);
                int shown = (int)v;
                if (ImGui.InputInt("##v", ref shown)) Write(bytes, Math.Max(0, shown));
                break;
            }
            case ClassFieldKind.Bool:
            {
                bool on = bytes.IndexOfAnyExcept((byte)0) >= 0;
                if (ImGui.Checkbox("##v", ref on))
                {
                    bytes.Clear();
                    if (on) bytes[^1] = 1;
                    Commit("GUI_ScriptGraph_UndoEdit");
                }
                return;
            }
            case ClassFieldKind.Enum:
                EnumEditor(element, offset, size, field);
                return;
            default:
            {
                // An undocumented word, as the bytes hold it.
                string hex = Convert.ToHexString(bytes);
                if (ImGui.InputText("##v", ref hex, (uint)(size * 2), ImGuiInputTextFlags.CharsHexadecimal | ImGuiInputTextFlags.CharsUppercase)
                    && hex.Length == size * 2)
                    Convert.FromHexString(hex).CopyTo(element.Data.AsSpan(offset));
                break;
            }
        }
        if (ImGui.IsItemDeactivatedAfterEdit()) Commit("GUI_ScriptGraph_UndoEdit");
    }

    private static long Read(ReadOnlySpan<byte> bytes) => bytes.Length switch
    {
        1 => bytes[0],
        2 => BinaryPrimitives.ReadUInt16BigEndian(bytes),
        _ => BinaryPrimitives.ReadUInt32BigEndian(bytes),
    };

    private static void Write(Span<byte> bytes, long value)
    {
        if (bytes.Length == 1) bytes[0] = (byte)value;
        else if (bytes.Length == 2) BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value);
        else BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)value);
    }

    private void EnumEditor(ScriptElement element, int offset, int size, ClassField field)
    {
        var bytes = element.Data.AsSpan(offset, size);
        long value = Read(bytes);
        var list = ClassFields.Enum(Game, field.TypeName);
        if (!ImGui.BeginCombo("##v", EnumText(list, value))) return;
        long before = value;
        if (list != null)
            foreach (var (name, entry) in list.Entries)
            {
                if (list.Flags)
                {
                    bool set = entry != 0 && (value & entry) == entry;
                    if (ImGui.Checkbox($"{name}##{entry}", ref set)) value = set ? value | entry : value & ~entry;
                }
                else if (ImGui.Selectable($"{name}  ({entry})", entry == value)) value = entry;
            }
        ImGui.EndCombo();
        if (value == before) return;
        // A target's index keeps naming the same number when its kind changes, in the new kind's encoding.
        if (field.TypeName is "ScriptTarget" or "ScriptArea" && size == 4 && offset + 8 <= element.Data.Length)
        {
            bool target = field.TypeName == "ScriptTarget";
            int index = IndexOf(target, (int)before, element.Word(offset + 4));
            element.SetWord(offset + 4, WordOf(target, (int)value, index));
        }
        Write(element.Data.AsSpan(offset, size), value);
        Commit("GUI_ScriptGraph_UndoEdit");
    }

    private static string EnumText(ClassEnum? list, long value) =>
        list == null ? value.ToString()
        : list.Flags ? (value == 0 ? "-" : string.Join(" | ", list.Entries.Where(e => e.Value != 0 && (value & e.Value) == e.Value).Select(e => e.Name).DefaultIfEmpty($"0x{value:X}")))
        : list.Entries.FirstOrDefault(e => e.Value == value) is { Name: { } name } ? name : $"{value} ?";

    // ---------------------------------------------------------------- pointers

    private void PointerEditor(ScriptElement element, int offset, string? kind)
    {
        var pointer = element.Pointers.GetValueOrDefault(offset);
        kind = pointer switch
        {
            ScriptVariablePointer => "Variable",
            ScriptStringPointer when kind == "Script" => "Script",
            ScriptStringPointer => "String",
            ScriptElementPointer => "Element",
            _ => kind,
        };
        switch (kind)
        {
            case "Variable":
            {
                var current = pointer as ScriptVariablePointer;
                if (!ImGui.BeginCombo("##v", current != null ? VariableName(current.Block, current.Offset) : LM.Get("GUI_ScriptGraph_None"))) return;
                ScriptPointer? chosen = pointer;
                if (ImGui.Selectable(LM.Get("GUI_ScriptGraph_None"), current == null)) chosen = null;
                foreach (var block in _scripts!.Variables)
                    for (int slot = 0; slot + 4 <= block.Data.Length; slot += 4)
                        if (ImGui.Selectable($"{VariableName(block, slot)}  = {block[slot]:0.###}##{_scripts.Variables.IndexOf(block)}_{slot}",
                                current?.Block == block && current.Offset == slot))
                            chosen = new ScriptVariablePointer(block, slot);
                if (ImGui.Selectable(LM.Get("GUI_ScriptGraph_AddVariable"))) chosen = AddVariable();
                ImGui.EndCombo();
                if (chosen == pointer) return;
                element.SetPointer(offset, chosen);
                Commit("GUI_ScriptGraph_UndoEdit");
                return;
            }
            case "Script":
            {
                string? name = (pointer as ScriptStringPointer)?.Text;
                bool known = name != null && Setup!.Scripts.Any(s => s.Name == name);
                if (!ImGui.BeginCombo("##v", name == null ? LM.Get("GUI_ScriptGraph_None") : known ? name : $"{name} ?")) return;
                string? chosen = name;
                if (ImGui.Selectable(LM.Get("GUI_ScriptGraph_None"), name == null)) chosen = null;
                foreach (var script in Setup!.Scripts)
                    if (ImGui.Selectable($"{script.Name}##{Setup.Scripts.IndexOf(script)}", script.Name == name)) chosen = script.Name;
                ImGui.EndCombo();
                if (chosen == name) return;
                element.SetPointer(offset, chosen == null ? null : new ScriptStringPointer(chosen));
                Commit("GUI_ScriptGraph_UndoEdit");
                return;
            }
            case "String":
            {
                string text = (pointer as ScriptStringPointer)?.Text ?? "";
                if (ImGui.InputText("##v", ref text, 255))
                    element.SetPointer(offset, text.Length == 0 ? null : new ScriptStringPointer(text));
                if (ImGui.IsItemDeactivatedAfterEdit()) Commit("GUI_ScriptGraph_UndoEdit");
                return;
            }
            default:
                ImGui.TextDisabled(pointer is ScriptElementPointer e ? Title(e.Element.ClassId) : LM.Get("GUI_ScriptGraph_None"));
                return;
        }
    }

    /// <summary>The text a node shows for a field, or null when it says nothing (a zero, an unset pointer).</summary>
    private string? ValueText(ScriptElement element, ClassField field)
    {
        if (element.Pointers.TryGetValue(field.Offset, out var pointer))
            return pointer switch
            {
                ScriptVariablePointer v => VariableName(v.Block, v.Offset),
                ScriptStringPointer s => s.Text,
                ScriptElementPointer => null,
                _ => null,
            };
        if (field.Kind == ClassFieldKind.Pointer) return null;
        var bytes = element.Data.AsSpan(field.Offset, field.Length);
        long raw = Read(bytes);
        return field.Kind switch
        {
            ClassFieldKind.F32 when field.Length == 4 => raw == 0 ? null : BinaryPrimitives.ReadSingleBigEndian(bytes).ToString("0.###"),
            ClassFieldKind.S32 when field.Length == 4 => raw == 0 ? null : ((int)raw).ToString(),
            ClassFieldKind.Bool => raw == 0 ? null : "yes",
            ClassFieldKind.Enum when field.TypeName is "ScriptTarget" or "ScriptArea" => InstanceText(element, field.Offset, field.TypeName == "ScriptTarget"),
            ClassFieldKind.Reference when field.Length == 4 && OldClassReferences.Decode(field.TypeName!, (uint)raw) is var at && at >= 0
                => Instance(field.TypeName!, at).Text,
            ClassFieldKind.Enum => raw == 0 && ClassFields.Enum(Game, field.TypeName) is { Flags: true } ? null : EnumText(ClassFields.Enum(Game, field.TypeName), raw),
            _ => raw == 0 ? null : raw.ToString(),
        };
    }

    // ---------------------------------------------------------------- instances

    /// <summary>The region the setup runs: the gameplay group of the same name (the only one when there is one).</summary>
    private EntityRegion? Region()
    {
        var regions = EntityManager.Singleton.Regions;
        return regions.FirstOrDefault(r => r.RegionName == Setup?.Name) ?? (regions.Count == 1 ? regions[0] : null);
    }

    /// <summary>The kind a target or area pair's kind word stands for: a Moby or a Pod, a Volume or an Area.</summary>
    private static string KindName(bool target, int kind) =>
        (target, kind) switch { (true, 0) => "Moby", (true, 1) => "Pod", (false, 0) => "Volume", (false, 1) => "Area", _ => $"{kind}?" };

    private static int KindWord(bool target, string kind) =>
        (target, kind) switch { (true, "Moby") or (false, "Volume") => 0, (true, "Pod") or (false, "Area") => 1, _ => -1 };

    private readonly Dictionary<string, List<(int Index, string Name, Entity? Entity)>?> _choices = [];
    private int _choicesFrame = -1;

    /// <summary>The instances of a kind the region has, by index, as the picker lists them (built once a frame);
    /// null when the level has no table of them, so the index is typed.</summary>
    private List<(int Index, string Name, Entity? Entity)>? Choices(string kind)
    {
        if (_choicesFrame != ImGui.GetFrameCount()) { _choices.Clear(); _choicesFrame = ImGui.GetFrameCount(); }
        if (_choices.TryGetValue(kind, out var known)) return known;
        List<(int Index, string Name, Entity? Entity)>? list = null;
        if (Region() is { } region)
        {
            IEnumerable<(int, string, Entity?)> found = kind switch
            {
                "Moby" => region.MobyInstances.Entities.OfType<EntityMoby>().Select(m => (m.GameplayIndex, m.Name, (Entity?)m)),
                "Volume" => region.Volumes.Entities.OfType<EntityVolume>().Select(v => (v.BaseVolume.Index, v.Name, (Entity?)v)),
                "Clue" => region.Volumes.Entities.OfType<EntityVolume>().Where(v => v.BaseVolume.Clue != null)
                    .Select(v => (v.BaseVolume.Clue!.Index, v.Name, (Entity?)v)),
                "Path" => region.Paths.Entities.OfType<EntityPath>().Select(p => (p.Path.Index, p.Name, (Entity?)p)),
                "Controller" => region.Cameras.Entities.Concat(region.ControllerInstances.Entities).OfType<EntityController>()
                    .Select(c => (c.Controller.Index, c.Name, (Entity?)c)),
                "Pod" => region.Pods.Select((p, i) => (i, p.Name, (Entity?)null)),
                "Area" => region.AreaNames.Select((n, i) => (i, n, (Entity?)null)),
                "Scent" => region.ScentNames.Select((n, i) => (i, n, (Entity?)null)),
                _ => [],
            };
            list = [.. found.Where(c => c.Item1 >= 0).OrderBy(c => c.Item1)];
            if (list.Count == 0) list = null;
        }
        return _choices[kind] = list;
    }

    /// <summary>What a kind and index name: the text a node shows and the instance, when it is one.</summary>
    private (string Text, Entity? Entity) Instance(string kind, int index)
    {
        if (index < 0) return ($"{kind}: {LM.Get("GUI_ScriptGraph_None")}", null);
        var choice = Choices(kind)?.FirstOrDefault(c => c.Index == index);
        return choice is { Name: { Length: > 0 } name } c ? ($"{kind} {index}: {name}", c.Entity) : ($"{kind} {index}", null);
    }

    private string InstanceText(ScriptElement element, int kindOffset, bool target)
    {
        if (kindOffset + 8 > element.Data.Length) return "";
        int kind = (int)element.Word(kindOffset);
        return Instance(KindName(target, kind), IndexOf(target, kind, element.Word(kindOffset + 4))).Text;
    }

    /// <summary>A Moby target holds the Moby's handle, (index + 1) &lt;&lt; 16 | index, as old-engine class fields do
    /// (<see cref="OldClassReferences"/>); the game checks it against the live Moby's id. Pods, volumes and areas
    /// hold the index.</summary>
    private static int IndexOf(bool target, int kind, uint word) => OldClassReferences.Decode(KindName(target, kind), word);

    private static uint WordOf(bool target, int kind, int index) => OldClassReferences.Encode(KindName(target, kind), index);

    /// <summary>A target or area pair: its kind decides what the picker lists, and an instance dropped on it sets both.</summary>
    private void PairEditor(ScriptElement element, int offset, bool target, float width, bool compact)
    {
        int kind = (int)element.Word(offset - 4);
        InstanceEditor(KindName(target, kind), IndexOf(target, kind, element.Word(offset)), width, compact, (to, at) =>
        {
            int word = KindWord(target, to);
            if (word < 0) return false;
            element.SetWord(offset - 4, (uint)word);
            element.SetWord(offset, WordOf(target, word, at));
            return true;
        });
    }

    private static string _instanceFilter = "";

    /// <summary>What a reference field takes from an instance dragged from the hierarchy: every kind it can stand for.</summary>
    private static IEnumerable<(string Kind, int Index)> ReferencesTo(Entity? entity)
    {
        switch (entity)
        {
            case EntityMoby moby: yield return ("Moby", moby.GameplayIndex); break;
            case EntityVolume volume:
                if (volume.BaseVolume.Clue is { } clue) yield return ("Clue", clue.Index);
                yield return ("Volume", volume.BaseVolume.Index);
                break;
            case EntityPath path: yield return ("Path", path.Path.Index); break;
            case EntityController controller: yield return ("Controller", controller.Controller.Index); break;
        }
    }

    /// <summary>A reference to an instance of <paramref name="kind"/>: the instance it names (a click selects it and
    /// shows it in the 3D view), a list of the kind's instances to pick another from, a cross to unset it, and a drop
    /// target for an instance dragged from the hierarchy. A kind the level has no table of takes the index.
    /// <paramref name="write"/> sets a kind and index, false when the field cannot take that kind.</summary>
    private void InstanceEditor(string kind, int index, float width, bool compact, Func<string, int, bool> write)
    {
        var (text, entity) = Instance(kind, index);
        var choices = Choices(kind);
        float spacing = ImGui.GetStyle().ItemInnerSpacing.X, button = ImGui.GetFrameHeight();
        float room = MathF.Max(20f, width - (choices != null ? 2f : 1f) * (button + spacing));
        void Set(string to, int at)
        {
            if (write(to, at)) Commit("GUI_ScriptGraph_UndoEdit");
        }

        if (choices == null)
        {
            ImGui.SetNextItemWidth(room);
            if (ImGui.InputInt("##v", ref index, 0)) write(kind, Math.Max(-1, index));
            if (ImGui.IsItemDeactivatedAfterEdit()) Commit("GUI_ScriptGraph_UndoEdit");
        }
        else
        {
            string label = entity?.Name is { Length: > 0 } name ? name
                : index < 0 ? LM.Get("GUI_ScriptGraph_None")
                : choices.FirstOrDefault(c => c.Index == index).Name is { Length: > 0 } listed ? listed : $"{index} ?";
            bool known = index >= 0 && choices.Any(c => c.Index == index);
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)(known ? ImGuiCol.Text : ImGuiCol.TextDisabled)]);
            if (ImGui.Selectable($"{label}##link", false, ImGuiSelectableFlags.None, new Vector2(room, button)) && entity != null)
            {
                SelectionManager.Singleton.Select(entity);
                FocusOn(entity);
            }
            ImGui.PopStyleColor();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(entity != null ? text + "\n" + LM.Get("GUI_ScriptGraph_Focus") : text);
        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload(PropertyInspectorFrame.DragPayload);
            unsafe
            {
                if (payload.Handle != null)
                    foreach (var (to, at) in ReferencesTo(PropertyInspectorFrame.DraggedEntity))
                        if (at >= 0 && write(to, at))
                        {
                            Commit("GUI_ScriptGraph_UndoEdit");
                            break;
                        }
            }
            ImGui.EndDragDropTarget();
        }
        if (choices != null)
        {
            ImGui.SameLine(0f, spacing);
            if (ImGui.ArrowButton("##pick", ImGuiDir.Down)) { _instanceFilter = ""; ImGui.OpenPopup("##instances"); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Frame_InstanceInspector_ReferencePick", kind));
        }
        ImGui.SameLine(0f, spacing);
        ImGui.BeginDisabled(index < 0);
        if (ImGui.Button("x##unset", new Vector2(button, 0f))) Set(kind, -1);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(LM.Get("GUI_Frame_InstanceInspector_ReferenceUnset"));
        if (!compact) ImGui.TextDisabled(text);
        if (choices == null || !ImGui.BeginPopup("##instances")) return;
        ImGui.SetNextItemWidth(260f);
        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        ImGui.InputTextWithHint("##filter", LM.Get("GUI_Frame_InstanceInspector_ReferenceFilter"), ref _instanceFilter, 64);
        if (ImGui.BeginChild("##list", new Vector2(320f, 260f)))
        {
            int shown = 0;
            foreach (var (at, name, _) in choices)
            {
                if (_instanceFilter.Length > 0 && !name.Contains(_instanceFilter, StringComparison.OrdinalIgnoreCase)
                    && !at.ToString().StartsWith(_instanceFilter, StringComparison.Ordinal)) continue;
                if (++shown > 300) break;
                if (ImGui.Selectable($"{at}  {name}##{at}", at == index))
                {
                    Set(kind, at);
                    ImGui.CloseCurrentPopup();
                }
            }
            if (shown > 300) ImGui.TextDisabled(LM.Get("GUI_Frame_InstanceInspector_ReferenceMore"));
        }
        ImGui.EndChild();
        ImGui.EndPopup();
    }

    /// <summary>Frames an instance in the 3D view, keeping the view's direction.</summary>
    private static void FocusOn(Entity entity)
    {
        var v3d = LunaWindow.Instance.GetFirstFrame<View3D>();
        if (v3d == null) return;
        var s = entity.WorldBoundingSphere;
        var centre = s.W > 0f ? new Vector3(s.X, s.Y, s.Z) : entity.Transform.Translation;
        float distance = MathF.Max(3f, MathF.Max(s.W, 0.5f) * 2.4f);
        v3d.ExitPov(restore: false);
        v3d.Camera.Position = centre - v3d.Camera.GetForward() * distance;
        v3d.Camera.Target = centre;
    }
}
