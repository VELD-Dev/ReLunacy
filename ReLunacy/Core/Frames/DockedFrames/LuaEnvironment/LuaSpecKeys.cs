using ReLunacy.Engine.Assets.Audio;
using ReLunacy.Engine.Config;
using ReLunacy.Engine.Loading.Readers;
using ReLunacy.Engine.Scripting;

namespace ReLunacy.Core.Frames.DockedFrames.LuaEnvironment;

/// <summary>
/// The keys of DialogueSpecs and SoundSpecs, the enums the game's /scripts/ fill at start: what <c>DialogueSpecs.</c>
/// completes to in any script. A dialogue line the opened level plays shows its subtitle, in the language picked in
/// Sound Banks.
/// </summary>
internal static class LuaSpecKeys
{
    private static (string Folder, LevelDialogue? Dialogue, List<LuaCompletionItem> Items)? _dialogue;
    private static (string Folder, List<LuaCompletionItem> Items)? _sounds;

    public static IEnumerable<LuaCompletionItem>? For(string table)
    {
        if (table is not ("DialogueSpecs" or "SoundSpecs")) return null;
        var files = LunaWindow.Instance.fileManager;
        if (files == null || string.IsNullOrEmpty(files.SourcePath) || GameConfigs.GameFolderFor(files.SourcePath) is not { } folder)
            return null;

        if (table == "SoundSpecs")
        {
            if (_sounds is not { } sounds || sounds.Folder != folder)
                _sounds = sounds = (folder, Items(SoundReader.SoundSpecs(folder), "SoundSpecs", null));
            return sounds.Items;
        }

        // Rebuilt once the level's dialogue is read, to add its subtitles.
        var dialogue = SoundReader.DialogueFor(files, SoundBanksFrame.DialogueLanguage);
        if (_dialogue is not { } cached || cached.Folder != folder || !ReferenceEquals(cached.Dialogue, dialogue))
            _dialogue = cached = (folder, dialogue, Items(SoundReader.DialogueSpecs(folder), "DialogueSpecs", dialogue));
        return cached.Items;
    }

    private static List<LuaCompletionItem> Items(IReadOnlyDictionary<int, string> names, string table, LevelDialogue? dialogue)
    {
        string? Subtitle(int id)
        {
            foreach (var bank in new[] { dialogue?.Level, dialogue?.Global })
                if (bank?.Find(id) is { } line && bank.Subtitles.TryGetValue(line.NameIndex, out var text)) return text;
            return null;
        }

        // Names are identifiers in the game's own scripts, so plain dot access always works.
        return names.OrderBy(n => n.Value, StringComparer.Ordinal)
            .Select(n => new LuaCompletionItem(n.Value, n.Value, LuaCompletionKind.Field, $"{table} {n.Key}", Subtitle(n.Key)))
            .ToList();
    }
}
