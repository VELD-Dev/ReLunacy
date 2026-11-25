using System.Globalization;
using ReLunacy.Core.Frames.DockedFrames;
using ReLunacy.Engine.Games;
using ReLunacy.Engine.Projects;
using ReLunacy.Engine.Text;

namespace ReLunacy.Core.Project;

internal sealed record ProjectTextTag(int Id, string Name, string LuaValue);

internal static class ProjectTextTags
{
    private static (GameId? Game, LunaProject? Project, int Revision, TextTablesFrame? Table, int EditVersion)? _stamp;
    private static IReadOnlyList<ProjectTextTag> _tags = [];

    public static IReadOnlyList<ProjectTextTag> List()
    {
        var game = ProjectSession.Game;
        var project = ProjectSession.Current;
        var table = LunaWindow.Instance.GetFirstFrame<TextTablesFrame>();
        var stamp = (game, project, project?.Revision ?? 0, table, table?.EditVersion ?? 0);
        if (_stamp == stamp) return _tags;

        var builtIn = GameText.BuiltInTags(game).ToDictionary(t => t.Id, t => t.Name);
        var names = new Dictionary<int, string>(builtIn);
        if (project != null)
            foreach (var (id, name) in project.TextTags()) names[id] = name;
        if (table != null)
            foreach (var (id, name) in table.PendingTags)
                names[id] = name.Length == 0 ? builtIn.GetValueOrDefault(id, "") : name;

        _tags = names.Where(pair => pair.Value.Length > 0).Select(pair => new ProjectTextTag(pair.Key, pair.Value,
            pair.Value == builtIn.GetValueOrDefault(pair.Key) ? pair.Value : pair.Key.ToString(CultureInfo.InvariantCulture))).ToArray();
        _stamp = stamp;
        return _tags;
    }
}
