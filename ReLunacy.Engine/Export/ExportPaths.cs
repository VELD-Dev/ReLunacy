namespace ReLunacy.Engine.Export;

/// <summary>Shared filename sanitization for exporters - asset/material names routinely contain
/// path-like characters (e.g. "levels/great_clock_a/entities/.../foo.entity.irb"), which break
/// file creation if used as-is.</summary>
public static class ExportPaths
{
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    public static string SanitizeFileName(string name) =>
        string.Concat(name.Select(c => char.IsWhiteSpace(c) || InvalidFileNameChars.Contains(c) ? '_' : c));
}
