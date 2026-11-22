using System.Numerics;
using NativeFileDialogSharp;
using NAudio.SoundFile;
using ReLunacy.Engine.Assets.Audio;
using ReLunacy.Engine.Assets.LevelElements;
using ReLunacy.Engine.Config;
using ReLunacy.Engine.Games;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Readers;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// View > Sound Banks: the level's resident and streamed sounds (Tools of Destruction, Quest for Booty and A Crack in Time),
/// grouped by bank and mixer group, mixer group alone or name prefix. The selected sound shows the SoundSpecs ids that play it (what a
/// settings field holds), its sample, its Scream grains and the instances whose sound fields name it; Play renders a
/// new take, drawn as a waveform with a cursor, levels and a spectrum.
/// </summary>
public sealed class SoundBanksFrame : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetWorkCenter(ImGui.GetMainViewport());
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.None;

    private enum GroupMode { Bank, Group, Prefix }

    // A looping sound's take is drawn this many seconds at a time; that much is rendered before it plays.
    private const double WindowSeconds = 10.0;
    private const int FftSize = 1024, Bands = 48;

    private LevelSounds? _sounds;
    private FileManager? _bankFiles;
    private string? _gameFolder;
    private Task<IReadOnlyList<SoundReader.LevelBankArchive>>? _bankDiscovery;
    private SoundReader.LevelBankArchive? _alternateBank;
    private Task<LevelSounds?>? _alternateSounds;
    private GroupMode _mode = GroupMode.Bank;
    private string _filter = "";
    private BankSound? _selected;
    private bool _reveal, _treeChanged;

    private (LevelSounds Sounds, GroupMode Mode, string Filter)? _treeFor;
    private sealed record SoundGroup(string Key, string Label, List<BankSound> Sounds);
    private List<(SoundBank? Bank, List<SoundGroup> Groups)> _tree = [];

    // The selected sound's details, read once.
    private BankSound? _detailsOf;
    private (int SampleRate, int Channels, double Seconds)? _format;
    private ScreamBank.SoundSummary? _summary;
    private List<(EntityRegion Region, string Kind, string Name, int Index, string Field, int Id)> _usedBy = [];
    private double _usedByTime = -10.0;

    private SoundTake? _take;
    private SoundTake? _playingTake;
    private BankSound? _takeOf, _rendering, _unplayable;
    private Task<SoundTake?>? _takeJob;
    private bool _playWhenReady;
    private readonly float[] _meterHold = new float[2];
    private readonly float[] _bands = new float[Bands];

    private readonly string[] _exportFormats = new[]
    {
        ".wav",
        ".aif",
        ".snd",
        ".caf",
        ".w64",
        ".pcm",
        ".flac",
        ".ogg",
        ".opus",
        ".mp3"
    };
    private int _selectedExportFormat = 6;
    private bool _exportAsSoundtrack = false;
    private HashSet<int>? _exportParts;
    private Task<string>? _exportJob;
    private string? _exportError;
    private bool _hasExportedTrack = false;
    private string _exportedPath = "";
    private double _exportedSince = 0.0;

    private readonly KeyValuePair<string, float>[] _exportCompressionLevels = new[]
    {
        new KeyValuePair<string, float>("GUI_SoundBanks_Compression_FAST", 0f),
        new("GUI_SoundBanks_Compression_BALANCED", 0.625f),
        new("GUI_SoundBanks_Compression_SLOW", 1.0f)
    };
    private int _exportCompression = 0;
    
    public SoundBanksFrame()
    {
        FrameName = LM.Get("GUI_Frame_SoundBanks");
    }

    /// <summary>Selects the sound a SoundSpecs id plays (from the inspector's sound field).</summary>
    public void Show(int id)
    {
        if (CurrentSounds() is not { } sounds || sounds.Find(id) is not { } sound) return;
        SelectBank(null);
        _sounds = sounds;
        _selected = sounds.BankSounds.FirstOrDefault(b => b.NameIndex == sound.NameIndex);
        _filter = "";
        _reveal = true;
    }

    /// <summary>Selects a sound by its name index (BankSound.NameIndex).</summary>
    public void ShowSound(int nameIndex)
    {
        if (CurrentSounds() is not { } sounds) return;
        SelectBank(null);
        _sounds = sounds;
        _selected = sounds.BankSounds.FirstOrDefault(b => b.NameIndex == nameIndex);
        _filter = "";
        _reveal = true;
    }

    private static LevelSounds? CurrentSounds() =>
        LunaWindow.Instance.fileManager is { } files ? SoundReader.For(files) : null;

    protected override void Render(double deltaTime)
    {
        var window = LunaWindow.Instance;
        var files = window.Level == null ? null : window.fileManager;
        if (!ReferenceEquals(files, _bankFiles))
        {
            _bankFiles = files;
            SelectBank(null);
            _gameFolder = files != null && window.Level?.GameDefinition?.Id is GameId.ToolsOfDestruction or GameId.QuestForBooty or GameId.ACrackInTime
                ? GameConfigs.GameFolderFor(files.SourcePath) : null;
            _bankDiscovery = _gameFolder is { } gameFolder ? Task.Run(() => SoundReader.ListLevelBanks(gameFolder)) : null;
        }

        DrawBankSelector();
        var sounds = _alternateSounds is { } alternate
            ? (alternate.IsCompletedSuccessfully ? alternate.Result : null)
            : files != null ? SoundReader.For(files) : null;
        if (sounds == null)
        {
            bool pending = _alternateSounds is { } load ? !load.IsCompleted : files != null && SoundReader.Pending(files);
            ImGui.TextDisabled(LM.Get(pending ? "GUI_SoundBanks_Loading" : "GUI_SoundBanks_None"));
            return;
        }
        if (!ReferenceEquals(sounds, _sounds))
        {
            _sounds = sounds;
            _selected = null;
            ClearTake();
        }
        if (sounds.BankSounds.Count == 0)
        {
            ImGui.TextDisabled(LM.Get("GUI_SoundBanks_None"));
            return;
        }

        if (ImGui.BeginChild("##soundtree", new Vector2(300f, 0f), ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeX))
            RenderTree(sounds);
        ImGui.EndChild();
        ImGui.SameLine();
        if (ImGui.BeginChild("##sound", Vector2.Zero))
        {
            if (_selected == null) ImGui.TextDisabled(LM.Get("GUI_SoundBanks_Pick"));
            else RenderSound(sounds, _selected);
        }
        ImGui.EndChild();
    }

    private void DrawBankSelector()
    {
        if (_bankDiscovery is not { IsCompletedSuccessfully: true } sources || sources.Result.Count == 0)
            return;

        string current = LM.Get("GUI_SoundBanks_CurrentLevel");
        if (!ImGui.BeginCombo(LM.Get("GUI_SoundBanks_Source") + "##bank_source", _alternateBank?.LevelName ?? current))
            return;
        if (ImGui.Selectable(current, _alternateBank == null))
            SelectBank(null);
        string? currentLevel = Path.GetFileName(Path.GetDirectoryName(_bankFiles?.SourcePath));
        foreach (var bank in sources.Result)
        {
            if (bank.LevelName.Equals(currentLevel, StringComparison.OrdinalIgnoreCase)) continue;
            if (ImGui.Selectable(bank.LevelName, bank == _alternateBank))
                SelectBank(bank);
        }
        ImGui.EndCombo();
    }

    private void SelectBank(SoundReader.LevelBankArchive? bank)
    {
        if (_alternateBank == bank) return;
        _alternateBank = bank;
        _alternateSounds = bank != null && _gameFolder is { } gameFolder
            ? Task.Run(() => SoundReader.ReadLevelBank(bank, gameFolder)) : null;

        ClearTake();
        _selected = null;
        _detailsOf = null;
    }

    private void ClearTake()
    {
        if (_playingTake != null && ReferenceEquals(SoundPlayer.Status.Key, _playingTake))
            SoundPlayer.Stop();
        _takeJob = null;
        _take = null;
        _playingTake = null;
        _takeOf = _rendering = _unplayable = null;
    }

    // ---------------------------------------------------------------- tree

    private void RenderTree(LevelSounds sounds)
    {
        string[] modes = [LM.Get("GUI_SoundBanks_ByBank"), LM.Get("GUI_SoundBanks_ByGroup"), LM.Get("GUI_SoundBanks_ByPrefix")];
        int mode = (int)_mode;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.Combo("##groupby", ref mode, modes, modes.Length)) { _mode = (GroupMode)mode; _reveal = true; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_SoundBanks_GroupBy"));
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputTextWithHint("##soundfilter", LM.Get("GUI_SoundBanks_Filter"), ref _filter, 64)) _treeChanged = true;

        if (_treeFor is not { } built || !ReferenceEquals(built.Sounds, sounds) || built.Mode != _mode || built.Filter != _filter)
        {
            _tree = BuildTree(sounds);
            _treeFor = (sounds, _mode, _filter);
        }
        ImGui.TextDisabled(LM.Get("GUI_SoundBanks_Count", _tree.Sum(bank => bank.Groups.Sum(group => group.Sounds.Count)), sounds.BankSounds.Count));
        ImGui.Separator();

        if (!ImGui.BeginChild("##soundlist", Vector2.Zero)) { ImGui.EndChild(); return; }
        foreach (var (bank, groups) in _tree)
        {
            if (bank is { } kind && !OpenSoundTreeNode(
                    BankLabel(kind), $"bank_{kind}", groups.Sum(g => g.Sounds.Count),
                    _selected != null && groups.Any(g => g.Sounds.Contains(_selected))))
                continue;

            foreach (var (key, label, members) in groups)
            {
                if (!OpenSoundTreeNode(label, key, members.Count, _selected != null && members.Contains(_selected)))
                    continue;
                foreach (var sound in members)
                {
                    var ids = sounds.IdsOf(sound).ToList();
                    bool reached = ids.Count > 0;
                    if (!reached) ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
                    bool selected = ReferenceEquals(sound, _selected);
                    if (ImGui.Selectable($"{sound.Name}##{sound.NameIndex}", selected)) _selected = sound;
                    if (!reached) ImGui.PopStyleColor();
                    if (selected && _reveal) { ImGui.SetScrollHereY(0.5f); _reveal = false; }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(reached ? string.Join("\n", ids.Select(i => $"{i.Id}  {i.SpecName}")) : LM.Get("GUI_SoundBanks_NoIds"));
                }
                ImGui.TreePop();
            }

            if (bank != null) ImGui.TreePop();
        }
        _treeChanged = false;
        ImGui.EndChild();
    }

    private bool OpenSoundTreeNode(string label, string key, int count, bool holdsSelected)
    {
        if (_treeChanged && _filter.Length > 0 || _reveal && holdsSelected)
            ImGui.SetNextItemOpen(true);
        return ImGui.TreeNodeEx($"{label}  ({count})###{key}", ImGuiTreeNodeFlags.SpanAvailWidth);
    }

    private List<(SoundBank? Bank, List<SoundGroup> Groups)> BuildTree(LevelSounds sounds)
    {
        bool Matches(BankSound s)
        {
            if (_filter.Length == 0 || s.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)
                || BankLabel(s.Bank).Contains(_filter, StringComparison.OrdinalIgnoreCase)
                || GroupName(sounds, s.Group).Contains(_filter, StringComparison.OrdinalIgnoreCase))
                return true;
            foreach (var id in sounds.IdsOf(s))
                if (id.SpecName.Contains(_filter, StringComparison.OrdinalIgnoreCase) || id.Id.ToString().StartsWith(_filter, StringComparison.Ordinal))
                    return true;
            return false;
        }
        (string Key, string Label) GroupOf(BankSound s)
        {
            if (_mode != GroupMode.Prefix)
                return (s.Group < 0 ? "~" : s.Group.ToString("D6"), GroupName(sounds, s.Group));
            return s.Name.IndexOf('_') is > 0 and var cut
                ? (s.Name[..cut].ToLowerInvariant(), s.Name[..cut].ToLowerInvariant() + "_")
                : (s.Name, s.Name);
        }
        return sounds.BankSounds.Where(Matches)
            .GroupBy(s => _mode == GroupMode.Bank ? (SoundBank?)s.Bank : null)
            .OrderBy(bank => bank.Key)
            .Select(bank => (bank.Key, bank.GroupBy(GroupOf)
                .OrderBy(group => group.Key.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new SoundGroup(group.Key.Key, group.Key.Label,
                    group.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList()))
                .ToList()))
            .ToList();
    }

    private static string BankLabel(SoundBank bank) =>
        LM.Get(bank == SoundBank.Resident ? "GUI_SoundBanks_Resident" : "GUI_SoundBanks_Streamed");

    private static string GroupName(LevelSounds sounds, int group) =>
        sounds.GroupLabel(group, LM.Get("GUI_SoundBanks_NoGroup"));

    // ---------------------------------------------------------------- details

    private void RenderSound(LevelSounds sounds, BankSound sound)
    {
        if (!ReferenceEquals(sound, _detailsOf))
        {
            _detailsOf = sound;
            _exportParts = null;
            _format = SoundReader.Format(sounds, sound.Bank, sound.BankIndex);
            _summary = sound.Bank == SoundBank.Resident ? sounds.Scream?.Describe(sound.BankIndex) : null;
            _usedByTime = -10.0;
        }
        var ids = sounds.IdsOf(sound).ToList();
        // Recounted each second, so edits show.
        if (ImGui.GetTime() - _usedByTime > 1.0)
        {
            _usedBy = _alternateBank == null ? UsersOf(ids.Select(i => i.Id).ToHashSet()) : [];
            _usedByTime = ImGui.GetTime();
        }

        ImGui.TextUnformatted(sound.Name);
        RenderPlayer(sounds, sound);
        ImGui.SeparatorText(LM.Get("GUI_SoundBanks_Details"));

        if (ImGui.BeginTable("##details", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("##label", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("##value", ImGuiTableColumnFlags.WidthStretch);
            void Row(string label)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextDisabled(LM.Get(label));
                ImGui.TableNextColumn();
            }

            Row("GUI_SoundBanks_Bank");
            ImGui.TextUnformatted(LM.Get(sound.Bank == SoundBank.Resident ? "GUI_SoundBanks_BankResident" : "GUI_SoundBanks_BankStreamed", sound.BankIndex));
            Row("GUI_SoundBanks_Group");
            ImGui.TextUnformatted(GroupName(sounds, sound.Group));

            Row("GUI_SoundBanks_Ids");
            if (ids.Count == 0) ImGui.TextDisabled(LM.Get("GUI_SoundBanks_NoIds"));
            foreach (var id in ids)
            {
                ImGui.TextUnformatted(id.Id.ToString());
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_SoundBanks_CopyId"));
                if (ImGui.IsItemClicked()) ImGui.SetClipboardText(id.Id.ToString());
                ImGui.SameLine();
                ImGui.TextDisabled(id.SpecName.Length > 0 ? id.SpecName : "-");
            }

            Row(sound.Bank == SoundBank.Resident ? "GUI_SoundBanks_FirstTone" : "GUI_SoundBanks_Format");
            if (_format is { } f)
            {
                ImGui.TextUnformatted(LM.Get(
                    "GUI_SoundBanks_FormatValue",
                    f.SampleRate,
                    f.Channels,
                    FormatTimecode(f.Seconds)
                ));
            }
            else ImGui.TextDisabled("-");

            if (_summary is { } summary)
            {
                Row("GUI_SoundBanks_VolumePan");
                ImGui.TextUnformatted($"{summary.Volume} / 127, {summary.Pan}°");
                Row("GUI_SoundBanks_Grains");
                ImGui.TextUnformatted(summary.Grains.ToString());
                foreach (var (type, count) in summary.Types)
                {
                    ImGui.TextDisabled($"{ScreamBank.GrainName(type)}");
                    ImGui.SameLine();
                    ImGui.TextUnformatted($"×{count}");
                }
                if (summary.Choices.Count > 0)
                {
                    Row("GUI_SoundBanks_Choices");
                    foreach (var choice in summary.Choices)
                        ImGui.TextUnformatted(LM.Get(choice.Shuffle ? "GUI_SoundBanks_ChoiceShuffle" : "GUI_SoundBanks_Choice",
                            ScreamBank.GrainName(choice.Type), choice.Options, choice.Each));
                }
                if (summary.Children.Count > 0)
                {
                    Row("GUI_SoundBanks_Children");
                    foreach (int child in summary.Children)
                    {
                        var target = sounds.BankSounds.FirstOrDefault(b => b.Bank == SoundBank.Resident && b.BankIndex == child);
                        if (target == null) { ImGui.TextDisabled($"#{child}"); continue; }
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.45f, 0.75f, 1f, 1f));
                        bool clicked = ImGui.Selectable($"{target.Name}##child{child}", false, ImGuiSelectableFlags.None, ImGui.CalcTextSize(target.Name));
                        ImGui.PopStyleColor();
                        if (clicked) { _selected = target; _reveal = true; }
                    }
                }
            }

            Row("GUI_SoundBanks_UsedBy");
            if (_usedBy.Count == 0) ImGui.TextDisabled(LM.Get("GUI_SoundBanks_UsedByNone"));
            bool several = _usedBy.Select(u => u.Region).Distinct().Count() > 1;
            for (int i = 0; i < _usedBy.Count; i++)
            {
                var (region, kind, name, index, field, id) = _usedBy[i];
                ImGui.PushID(i);
                PropertyInspectorFrame.ReferenceLink(region, kind, name, index);
                ImGui.SameLine();
                ImGui.TextDisabled((several ? region.RegionName + "  " : "") + field + (ids.Count > 1 ? $"  {id}" : ""));
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        
        var parts = ReferenceEquals(_takeOf, sound) ? _take?.Parts : null;
        ImGui.SeparatorText(LM.Get("GUI_SoundBanks_ExportSection"));
        if(ImGui.BeginTable("###export", 2, ImGuiTableFlags.SizingStretchProp))
        {
            void Row(string label)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(LM.Get(label));
                ImGui.TableNextColumn();
            }
            
            if (parts is { Count: > 1 })
            {
                Row("GUI_SoundBanks_ExportParts");
                DrawExportPartsCombo(parts);
            }

            Row("GUI_SoundBanks_ExportAsSoundtrack");
            ImGui.Checkbox("##export_as_soundtrack", ref _exportAsSoundtrack);
            ImGui.SameLine();
            ImGuiPlus.HelpMarker(LM.Get("GUI_SoundBanks_ExportAsSoundtrackHelp"));
            Row("GUI_SoundBanks_ExportType");
            ImGui.Combo("##export_type_combo", ref _selectedExportFormat, _exportFormats.Stringify("\0"));
            Row("GUI_SoundBanks_ExportCompression");
            if (ImGui.BeginCombo("##export_compression", LM.Get(_exportCompressionLevels[_exportCompression].Key)))
            {
                for(int i = 0; i < _exportCompressionLevels.Length; i++)
                {
                    var complvl =  _exportCompressionLevels[i];
                    if (ImGui.Selectable(LM.Get(complvl.Key) + "##compresssion_" + i,
                            i == _exportCompression))
                    {
                        _exportCompression = i;
                    }
                }
                ImGui.EndCombo();
            }
            ImGui.SameLine();
            ImGuiPlus.HelpMarker(LM.Get("GUI_SoundBanks_Compression_Help"));
            ImGui.EndTable();
        }
        
        if (_exportJob is { IsCompleted: true } job)
        {
            _hasExportedTrack = job.IsCompletedSuccessfully;
            _exportedPath = _hasExportedTrack ? job.Result : "";
            _exportError = job.Exception?.GetBaseException().Message;
            _exportedSince = 0;
            _exportJob = null;
        }

        ImGui.BeginDisabled(_exportJob != null || _exportParts is { Count: 0 });
        if (ImGui.Button(LM.Get("GUI_SoundBanks_ExportButton")))
        {
            var level = LunaWindow.Instance.LevelMetadata;
            string levelName = _alternateBank?.LevelName ?? level?.Name ?? "Sounds";
            var selectedParts = _exportParts != null && parts != null && _exportParts.Count < parts.Count
                ? new HashSet<int>(_exportParts) : null;
            string suffix = selectedParts == null ? "" : "_parts_" + string.Join("-", selectedParts.Order().Select(i => i + 1));
            string output = Path.Combine(Program.EditorPath, "Extracted", "Audio", levelName,
                sound.Name + suffix + _exportFormats[_selectedExportFormat]);
            var format = (SoundFileMajorFormat)_selectedExportFormat;
            bool isSoundtrack = _exportAsSoundtrack;
            double compression = _exportCompressionLevels[_exportCompression].Value;

            _hasExportedTrack = false;
            _exportError = null;
            _exportJob = Task.Run(() =>
            {
                SoundExporter.ExportSoundAsFormat(output, sounds, sound, format, level, isSoundtrack, compression, selectedParts);
                return output;
            });
        }
        ImGui.EndDisabled();
        if (_exportJob != null) ImGui.TextDisabled(LM.Get("GUI_SoundBanks_Exporting"));
        if (_exportError != null) ImGui.TextWrapped(LM.Get("GUI_SoundBanks_ExportFailed", _exportError));

        if (_hasExportedTrack)
        {
            ImGui.TextColored(new Vector4(25, 255, 25, 255) / 255, LM.Get("GUI_SoundBanks_ExportedTrackLabel", _exportedPath));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(25, 180, 25, 255) / 255);
            if (ImGui.Button(LM.Get("GUI_SoundBanks_ExportedOpenFolder")))
            {
                try
                {
                    Process.Start(new ProcessStartInfo()
                    {
                        FileName = Path.GetDirectoryName(_exportedPath),
                        UseShellExecute = true
                    });
                }
                catch (Exception e)
                {
                    LunaLog.LogError($"Could not open the sound export folder. {e.Message}");
                }
            }
            ImGui.PopStyleColor();

            _exportedSince += Time.Delta;
            if (_exportedSince >= 10.0f)
            {
                _hasExportedTrack = false;
                _exportedSince = 0;
            }
        }
    }

    private void DrawExportPartsCombo(IReadOnlyList<SoundPart> parts)
    {
        string[] names = PartNames(parts);
        string entire = LM.Get("GUI_SoundBanks_EntireSong");
        string preview = _exportParts switch
        {
            null => entire,
            { Count: 0 } => LM.Get("GUI_SoundBanks_NoParts"),
            { Count: 1 } => names[_exportParts.Single()],
            _ => LM.Get("GUI_SoundBanks_SelectedParts", _exportParts.Count),
        };
        if (!ImGui.BeginCombo("##export_parts", preview)) return;

        const ImGuiSelectableFlags keepOpen = ImGuiSelectableFlags.NoAutoClosePopups;
        if (ImGui.Selectable(entire, _exportParts == null, keepOpen))
            _exportParts = null;
        ImGui.Separator();
        for (int i = 0; i < names.Length; i++)
        {
            bool selected = _exportParts?.Contains(i) == true;
            if (!ImGui.Selectable($"{names[i]}##part{i}", selected, keepOpen)) continue;
            _exportParts ??= [];
            if (selected) _exportParts.Remove(i);
            else _exportParts.Add(i);
        }
        ImGui.EndCombo();
    }

    private static string[] PartNames(IReadOnlyList<SoundPart> parts)
    {
        int loops = parts.Count(part => part.Kind == SoundPartKind.Loop);
        int loop = 0;
        return parts.Select((part, i) => part.Kind switch
        {
            SoundPartKind.Intro => LM.Get("GUI_SoundBanks_PartIntro"),
            SoundPartKind.Loop when loops == 1 => LM.Get("GUI_SoundBanks_PartLoop"),
            SoundPartKind.Loop => LM.Get("GUI_SoundBanks_PartNumberedLoop", ++loop),
            SoundPartKind.Outro => LM.Get("GUI_SoundBanks_PartOutro"),
            _ => LM.Get("GUI_SoundBanks_PartNumbered", i + 1),
        }).ToArray();
    }

    /// <summary>Every Clue, controller and Moby of the loaded regions whose documented sound fields hold one of the
    /// ids, with the class and the field.</summary>
    private static List<(EntityRegion, string, string, int, string, int)> UsersOf(HashSet<int> ids)
    {
        var users = new List<(EntityRegion, string, string, int, string, int)>();
        if (ids.Count == 0) return users;
        var game = LunaWindow.Instance.Level?.GameDefinition?.Id;
        if (game == null) return users;
        void Scan(EntityRegion region, string kind, string name, int index, uint cls, string? className, byte[] settings)
        {
            foreach (var field in ClassFields.Of(game, cls))
            {
                if (field.Kind != ClassFieldKind.Sound || field.Offset + 4 > settings.Length) continue;
                int id = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(settings.AsSpan(field.Offset));
                if (!ids.Contains(id)) continue;
                string owner = ClassFields.ClassName(game, cls) ?? className ?? $"0x{cls:X}";
                users.Add((region, kind, name, index, $"{owner} {field.Name}", id));
            }
        }
        foreach (var region in EntityManager.Singleton.Regions)
        {
            foreach (var volume in region.Volumes.Entities.OfType<EntityVolume>())
                if (volume.BaseVolume.Clue is { } clue)
                    Scan(region, "Clue", clue.Name, clue.Index, clue.Class, clue.ClassName, clue.Settings);
            foreach (var controller in region.Controllers)
                Scan(region, "Controller", controller.Name, controller.Index, controller.Class, controller.ClassName, controller.Settings);
            foreach (var moby in region.MobyInstances.Entities.OfType<EntityMoby>())
                if (moby.Settings.Length >= 4)
                    Scan(region, "Moby", moby.Name, moby.GameplayIndex,
                        System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(moby.Settings), null, moby.Settings);
        }
        return users;
    }

    // ---------------------------------------------------------------- player

    private void RenderPlayer(LevelSounds sounds, BankSound sound)
    {
        if (ReferenceEquals(_rendering, sound) && _takeJob is { IsCompleted: true } job)
        {
            _take = job.IsCompletedSuccessfully ? job.Result : null;
            _takeOf = sound;
            _takeJob = null;
            _rendering = null;
            if (_take == null || _playWhenReady && !StartPlayback(_take))
                _unplayable = sound;
        }

        var take = ReferenceEquals(_takeOf, sound) ? _take : null;
        if (take == null && !ReferenceEquals(_rendering, sound) && !ReferenceEquals(_unplayable, sound))
            PrepareTake(sounds, sound, play: false);
        var (key, queued) = SoundPlayer.Status;
        if (!ReferenceEquals(key, _playingTake)) _playingTake = null;
        bool playing = take != null && ReferenceEquals(key, take);
        bool rendering = ReferenceEquals(_rendering, sound);

        ImGui.BeginDisabled(rendering);
        if (ImGui.Button(LM.Get(playing ? "GUI_SoundBanks_Stop" : "GUI_SoundBanks_Play") + "##play", new Vector2(80f, 0f)))
        {
            if (playing) SoundPlayer.Stop();
            else if (sound.Bank == SoundBank.Streamed && take?.Rewind() == true)
                _unplayable = StartPlayback(take) ? null : sound;
            else PrepareTake(sounds, sound, play: true);
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(LM.Get(playing ? "GUI_SoundBanks_StopHint" : "GUI_SoundBanks_PlayHint"));
        ImGui.SameLine();
        if (rendering) ImGui.TextDisabled(LM.Get("GUI_SoundBanks_Rendering"));
        else if (ReferenceEquals(_unplayable, sound)) ImGui.TextDisabled(LM.Get("GUI_SoundBanks_Unplayable"));
        else if (take != null)
        {
            var duration = TimeSpan.FromSeconds(take.Written / (double)take.SampleRate);
            ImGui.TextDisabled(LM.Get(
                take.Complete ? "GUI_SoundBanks_TakeLength" : "GUI_SoundBanks_TakeLooping",
                FormatTimecode(duration),
                take.SampleRate,
                take.Channels
            ));
        }

        if (take == null) return;
        long cursor = playing ? Math.Max(0, take.Served - queued) : -1;
        if (RenderWaveform(take, cursor) is { } frame)
        {
            if (SoundPlayer.TrySeek(take, frame))
            {
                if (rendering)
                {
                    _takeJob = null;
                    _rendering = null;
                }
                _playingTake = take;
                _unplayable = null;
                Array.Clear(_meterHold);
                Array.Clear(_bands);
                cursor = frame;
                playing = true;
            }
            else _unplayable = sound;
        }
        RenderMeters(take, cursor, playing);
    }

    private void PrepareTake(LevelSounds sounds, BankSound sound, bool play)
    {
        _rendering = sound;
        _unplayable = null;
        _playWhenReady = play;
        _takeJob = Task.Run(() =>
        {
            var source = SoundReader.Open(sounds, sound.Bank, sound.BankIndex);
            // A stream is kept whole for its waveform; a Scream sound is rendered a window ahead.
            return source == null ? null : new SoundTake(source, source is PcmSource ? 900.0 : WindowSeconds, WindowSeconds * 2.0);
        });
    }

    private bool StartPlayback(SoundTake take)
    {
        if (!SoundPlayer.Play(take, take)) return false;
        _playingTake = take;
        Array.Clear(_meterHold);
        Array.Clear(_bands);
        return true;
    }

    /// <summary>The take's waveform, its channels mixed: the whole take, or a window of a looping one that follows
    /// the cursor.</summary>
    private long? RenderWaveform(SoundTake take, long cursor)
    {
        long written = take.Written, oldest = take.Oldest;
        long window = take.Complete ? Math.Max(1, written) : (long)(WindowSeconds * take.SampleRate);
        long start;
        if (take.Complete) start = 0;
        else if (cursor >= 0) start = Math.Max(oldest, cursor - window * 4 / 5);
        else start = Math.Max(oldest, written - window);

        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var size = new Vector2(MathF.Max(64f, ImGui.GetContentRegionAvail().X), 110f);
        var markers = LayoutPartMarkers(take.Parts, start, window, size.X);
        float rowHeight = ImGui.GetTextLineHeight() + 4f;
        float labelHeight = markers.Count == 0 ? 0f : (markers.Max(marker => marker.Row) + 1) * rowHeight + 4f;
        bool clicked = ImGui.InvisibleButton("##waveform", size + new Vector2(0f, labelHeight));
        bool hovered = ImGui.IsItemHovered();
        var labelOrigin = origin;
        origin.Y += labelHeight;
        draw.AddRectFilled(origin, origin + size, ImGui.GetColorU32(new Vector4(0.06f, 0.07f, 0.09f, 1f)));
        float mid = origin.Y + size.Y * 0.5f, half = size.Y * 0.5f - 2f;
        draw.AddLine(new Vector2(origin.X, mid), new Vector2(origin.X + size.X, mid), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.12f)));
        uint played = ImGui.GetColorU32(new Vector4(0.35f, 0.75f, 1f, 1f)), ahead = ImGui.GetColorU32(new Vector4(0.35f, 0.75f, 1f, 0.45f));
        int columns = (int)size.X;
        for (int x = 0; x < columns; x++)
        {
            long from = start + window * x / columns, to = Math.Max(from + 1, start + window * (x + 1) / columns);
            if (from >= written) break;
            var (min, max) = take.Range(from, to);
            float top = mid - Math.Clamp(max, -1f, 1f) * half,
                bottom = mid - Math.Clamp(min, -1f, 1f) * half;
            draw.AddLine(new Vector2(origin.X + x + 0.5f, top), new Vector2(origin.X + x + 0.5f, MathF.Max(bottom, top + 1f)),
                cursor < 0 || from < cursor ? played : ahead);
        }
        uint markerColor = ImGui.GetColorU32(new Vector4(1f, 0.35f, 0.35f, 1f));
        foreach (var marker in markers)
        {
            float leftEdge = origin.X + marker.Left;
            float rightEdge = origin.X + marker.Right;
            if (marker.Left > 0f)
                draw.AddLine(new Vector2(leftEdge, origin.Y - 2f), new Vector2(leftEdge, origin.Y + size.Y), markerColor);
            float gap = MathF.Min(1f, (rightEdge - leftEdge) * 0.25f);
            draw.AddLine(new Vector2(leftEdge + gap, origin.Y - 2f), new Vector2(rightEdge - gap, origin.Y - 2f), markerColor, 2f);
            draw.AddText(labelOrigin + new Vector2(marker.LabelX, marker.Row * rowHeight), markerColor, marker.Label);
        }
        if (cursor >= start && cursor <= start + window)
        {
            float cx = origin.X + (cursor - start) / (float)window * size.X;
            draw.AddLine(new Vector2(cx, origin.Y), new Vector2(cx, origin.Y + size.Y), ImGui.GetColorU32(new Vector4(1f, 0.85f, 0.3f, 1f)), 1.5f);
        }
        uint text = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.5f));
        string left = $"{FormatTimecode(start / (double)take.SampleRate)}", right = $"{FormatTimecode((start + window) / (double)take.SampleRate)}";
        draw.AddText(origin + new Vector2(4f, 2f), text, left);
        draw.AddText(origin + new Vector2(size.X - ImGui.CalcTextSize(right).X - 4f, 2f), text, right);
        if (cursor >= 0)
        {
            string now = $"{FormatTimecode(cursor / (double)take.SampleRate)}";
            draw.AddText(origin + new Vector2((size.X - ImGui.CalcTextSize(now).X) * 0.5f, 2f), ImGui.GetColorU32(new Vector4(1f, 0.85f, 0.3f, 1f)), now);
        }

        var mouse = ImGui.GetMousePos();
        if (!hovered || mouse.Y < origin.Y) return null;
        float xOffset = Math.Clamp(mouse.X - origin.X, 0f, size.X);
        long frame = start + Math.Min(window - 1, (long)(xOffset / (double)size.X * window));
        if (frame < oldest || frame >= written) return null;

        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        float xHover = origin.X + xOffset;
        uint preview = ImGui.GetColorU32(new Vector4(0.68f, 0.70f, 0.74f, 1f));
        draw.AddLine(new Vector2(xHover, origin.Y), new Vector2(xHover, origin.Y + size.Y), preview);
        string timecode = FormatTimecode(frame / (double)take.SampleRate);
        var textSize = ImGui.CalcTextSize(timecode);
        float labelX = Math.Clamp(xOffset - textSize.X * 0.5f, 4f, MathF.Max(4f, size.X - textSize.X - 4f));
        var label = origin + new Vector2(labelX, size.Y - textSize.Y - 3f);
        var padding = new Vector2(3f, 2f);
        draw.AddRectFilled(label - padding, label + textSize + padding, ImGui.GetColorU32(new Vector4(0.06f, 0.07f, 0.09f, 0.94f)));
        draw.AddText(label, preview, timecode);
        return clicked ? frame : null;
    }

    private static List<(float Left, float Right, float LabelX, int Row, string Label)> LayoutPartMarkers(
        IReadOnlyList<SoundPart> parts, long start, long window, float width)
    {
        if (parts.Count < 2) return [];
        var markers = new List<(float, float, float, int, string)>();
        var rowEnds = new List<float>();
        string[] names = PartNames(parts);
        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            long from = Math.Max(start, part.StartFrame), to = Math.Min(start + window, (long)part.StartFrame + part.FrameCount);
            if (from >= to) continue;
            float left = (from - start) / (float)window * width, right = (to - start) / (float)window * width;
            string label = names[i];
            float textWidth = ImGui.CalcTextSize(label).X;
            float labelX = Math.Clamp((left + right - textWidth) * 0.5f, 2f, MathF.Max(2f, width - textWidth - 2f));
            int row = rowEnds.FindIndex(end => end + 6f <= labelX);
            if (row < 0)
            {
                row = rowEnds.Count;
                rowEnds.Add(0f);
            }
            rowEnds[row] = labelX + textWidth;
            markers.Add((left, right, labelX, row, label));
        }
        return markers;
    }

    /// <summary>Peak and RMS of each channel over the 50 ms before the cursor (dB, -60..0), and a spectrum of the
    /// 1024 frames before it.</summary>
    private void RenderMeters(SoundTake take, long cursor, bool playing)
    {
        float dt = ImGui.GetIO().DeltaTime;
        var draw = ImGui.GetWindowDrawList();
        float width = MathF.Max(64f, ImGui.GetContentRegionAvail().X);
        float meterWidth = MathF.Min(220f, width * 0.35f), barHeight = 10f, gap = 4f;
        var origin = ImGui.GetCursorScreenPos() + new Vector2(0f, gap);
        int channels = Math.Min(2, take.Channels);
        float spectrumHeight = 70f;
        ImGui.Dummy(new Vector2(width, spectrumHeight + gap));
        uint back = ImGui.GetColorU32(new Vector4(0.06f, 0.07f, 0.09f, 1f));
        static float Db(float v) => Math.Clamp((20f * MathF.Log10(MathF.Max(v, 1e-6f)) + 60f) / 60f, 0f, 1f);

        // Levels.
        long span = take.SampleRate / 20;
        for (int c = 0; c < channels; c++)
        {
            var (peak, rms) = playing ? take.Level(cursor - span, cursor, c) : (0f, 0f);
            _meterHold[c] = MathF.Max(Db(peak), _meterHold[c] - dt * 0.5f);
            var a = origin + new Vector2(0f, c * (barHeight + gap));
            var b = a + new Vector2(meterWidth, barHeight);
            draw.AddRectFilled(a, b, back);
            float level = Db(rms);
            var color = level > 0.95f ? new Vector4(1f, 0.35f, 0.3f, 1f) : level > 0.8f ? new Vector4(1f, 0.8f, 0.3f, 1f) : new Vector4(0.35f, 0.85f, 0.45f, 1f);
            draw.AddRectFilled(a, a + new Vector2(meterWidth * level, barHeight), ImGui.GetColorU32(color));
            float hold = a.X + meterWidth * _meterHold[c];
            draw.AddLine(new Vector2(hold, a.Y), new Vector2(hold, b.Y), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.8f)), 2f);
            draw.AddText(b + new Vector2(4f, -barHeight - 2f), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.5f)),
                channels == 1 ? "M" : c == 0 ? "L" : "R");
        }

        // Spectrum.
        var sa = origin + new Vector2(meterWidth + 24f, 0f);
        var sb = new Vector2(origin.X + width, origin.Y + spectrumHeight);
        if (sb.X - sa.X < 40f) return;
        draw.AddRectFilled(sa, sb, back);
        Span<float> target = stackalloc float[Bands];
        if (playing) Spectrum(take, cursor, target);
        float bandWidth = (sb.X - sa.X) / Bands;
        uint bar = ImGui.GetColorU32(new Vector4(0.35f, 0.75f, 1f, 0.85f));
        for (int i = 0; i < Bands; i++)
        {
            // Rises at once, falls smoothly.
            _bands[i] = MathF.Max(target[i], _bands[i] - dt * 1.5f);
            float h = _bands[i] * (sb.Y - sa.Y);
            draw.AddRectFilled(new Vector2(sa.X + i * bandWidth + 1f, sb.Y - h), new Vector2(sa.X + (i + 1) * bandWidth - 1f, sb.Y), bar);
        }
    }

    /// <summary>Log-spaced bands from 40 Hz to Nyquist of a Hann-windowed FFT, as 0..1 over -70..0 dB.</summary>
    private static void Spectrum(SoundTake take, long cursor, Span<float> bands)
    {
        Span<float> samples = stackalloc float[FftSize];
        take.Mix(cursor - FftSize, samples);
        var re = new float[FftSize];
        var im = new float[FftSize];
        for (int i = 0; i < FftSize; i++)
            re[i] = samples[i] * (0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / (FftSize - 1)));
        Fft(re, im);
        float nyquist = take.SampleRate / 2f, low = MathF.Min(40f, nyquist / 2f);
        for (int b = 0; b < bands.Length; b++)
        {
            float f0 = low * MathF.Pow(nyquist / low, b / (float)bands.Length);
            float f1 = low * MathF.Pow(nyquist / low, (b + 1) / (float)bands.Length);
            int k0 = Math.Clamp((int)(f0 / nyquist * (FftSize / 2)), 1, FftSize / 2 - 1);
            int k1 = Math.Clamp((int)MathF.Ceiling(f1 / nyquist * (FftSize / 2)), k0 + 1, FftSize / 2);
            float power = 0f;
            for (int k = k0; k < k1; k++) power = MathF.Max(power, re[k] * re[k] + im[k] * im[k]);
            float db = 10f * MathF.Log10(MathF.Max(power, 1e-12f)) - 20f * MathF.Log10(FftSize / 4f);
            bands[b] = Math.Clamp((db + 70f) / 70f, 0f, 1f);
        }
    }

    // In-place radix-2 FFT.
    private static void Fft(float[] re, float[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int length = 2; length <= n; length <<= 1)
        {
            float angle = -2f * MathF.PI / length;
            float wr = MathF.Cos(angle), wi = MathF.Sin(angle);
            for (int i = 0; i < n; i += length)
            {
                float cr = 1f, ci = 0f;
                for (int k = 0; k < length / 2; k++)
                {
                    int a = i + k, b = a + length / 2;
                    float tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                    (cr, ci) = (cr * wr - ci * wi, cr * wi + ci * wr);
                }
            }
        }
    }

    /// <summary>
    /// Turns seconds into a properly formatted x:xx.xx time.
    /// </summary>
    /// <param name="seconds"></param>
    /// <returns></returns>
    private static string FormatTimecode(in double seconds)
    {
        long centiseconds = (long)Math.Round(seconds * 100);
        return $"{centiseconds / 6000:00}:{centiseconds / 100 % 60:00}.{centiseconds % 100:00}";
    }
    
    private static string FormatTimecode(in TimeSpan timespan) => FormatTimecode(timespan.TotalSeconds);
}
