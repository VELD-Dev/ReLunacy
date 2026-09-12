namespace ReLunacy.Engine.Export;

/// <summary>Shared filename sanitization for exporters - strips path-like and invalid characters from asset/material names.</summary>
public static class ExportPaths
{
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    public static string SanitizeFileName(string name) =>
        string.Concat(name.Select(c => char.IsWhiteSpace(c) || InvalidFileNameChars.Contains(c) ? '_' : c));
}
