using Newtonsoft.Json;

namespace ReLunacy.Utility.Localization;

public static class LM
{
    public static readonly Dictionary<string, Language> Languages = [];
    private static readonly string defaultLanguageFolder = Path.Combine(Program.EditorPath, "Locales");
    private static readonly string defaultLanguage = "en";
    public static Language? CurrentLanguage { get; private set; }
    public static Language? DefaultLanguage { get; private set; }

    public static bool UseFallbackLanguage => Program.Settings.UseFallbackLanguage;

    public static void Initialize()
    {
        RefreshLanguages();

        if (Languages.Count > 0 && Languages.TryGetValue(defaultLanguage, out Language? defloc))
        {
            DefaultLanguage = defloc;
        }
        else if (Languages.Count == 0)
        {
            var templateLanguage = new Language()
            {
                LangCode = "template_locale",
                LangName = "Template Language"
            };
            Languages.Add(templateLanguage.LangCode, templateLanguage);

            var locfile = JsonConvert.SerializeObject(templateLanguage, Formatting.Indented);

            if (!Directory.Exists(defaultLanguageFolder))
                Directory.CreateDirectory(defaultLanguageFolder);

            File.WriteAllText(Path.Combine(defaultLanguageFolder, "template_locale.json"), locfile);

            TrySetLanguage(templateLanguage.LangCode);
            return;
        }
        else if (Languages.ContainsKey("template_locale"))
        {
            TrySetLanguage("template_locale");
        }

        TrySetLanguage(Program.Settings.Language);
    }

    public static void RefreshLanguages(bool overwrite = false)
    {
        if (!Directory.Exists(defaultLanguageFolder))
        {
            Directory.CreateDirectory(defaultLanguageFolder);
            return;
        }

        var files = Directory.GetFiles(defaultLanguageFolder, "*.json", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            try
            {
                var locale = Language.LoadFromFile(file);
                if (locale is null) continue;

                if (Languages.ContainsKey(locale.LangCode) && !overwrite)
                    continue;

                Languages[locale.LangCode] = locale;
            }
            catch (Exception ex)
            {
                LunaLog.LogError($"Unable to load locale {file}. Exception: {ex}");
            }
        }
    }

    public static void TrySetLanguage(string langcode)
    {
        if (Languages.TryGetValue(langcode, out Language? newLocale))
        {
            CurrentLanguage = newLocale;
            Program.Settings.Language = CurrentLanguage.LangCode;
        }
        else if (DefaultLanguage != null)
        {
            CurrentLanguage = DefaultLanguage;
            Program.Settings.Language = CurrentLanguage.LangCode;
        }
        else
        {
            CurrentLanguage = null;
        }
    }

    public static string Get(string key, params object[] args)
    {
        if (CurrentLanguage != null && CurrentLanguage.strings.TryGetValue(key, out string? fmt) && fmt != string.Empty)
            return SafeFormat(fmt, args);
        else if (CurrentLanguage != null && !CurrentLanguage.strings.TryGetValue(key, out fmt))
            CurrentLanguage.strings.Add(key, "");

        if (UseFallbackLanguage && DefaultLanguage != null && DefaultLanguage.strings.TryGetValue(key, out fmt) && fmt != string.Empty)
            return SafeFormat(fmt, args);
        else if (DefaultLanguage != null && !DefaultLanguage.strings.TryGetValue(key, out fmt))
            DefaultLanguage.strings.Add(key, "");

        return key;
    }

    // A locale string's placeholder count can drift out of sync with its call site — most often a
    // stale on-disk translation left over after a key's format changed elsewhere: self-healing
    // (above) only fills in keys that are entirely MISSING, it never reconciles an EXISTING key's
    // value against a call site that now passes a different number of args. string.Format throwing
    // on that mismatch used to take the whole app down over a single mistranslated/stale label —
    // degrade to the raw unformatted string instead.
    private static string SafeFormat(string fmt, object[] args)
    {
        try
        {
            return string.Format(fmt, args);
        }
        catch (FormatException)
        {
            return fmt;
        }
    }

    public static void SaveLanguages(bool all = false)
    {
        if (CurrentLanguage != null)
        {
            var locfile = JsonConvert.SerializeObject(CurrentLanguage, Formatting.Indented);
            File.WriteAllText(CurrentLanguage.Filepath, locfile);
        }

        if (DefaultLanguage != null)
        {
            var locfile = JsonConvert.SerializeObject(DefaultLanguage, Formatting.Indented);
            File.WriteAllText(DefaultLanguage.Filepath, locfile);
        }

        if (!all) return;

        foreach (var lang in Languages)
        {
            if (lang.Value == CurrentLanguage || lang.Value == DefaultLanguage) continue;
            var locfile = JsonConvert.SerializeObject(lang.Value, Formatting.Indented);
            File.WriteAllText(lang.Value.Filepath, locfile);
        }
    }
}
