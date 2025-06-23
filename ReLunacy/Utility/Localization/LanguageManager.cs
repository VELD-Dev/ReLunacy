using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Utility.Localization;

public static class LM
{
    public readonly static Dictionary<string, Language> Languages = [];
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

            File.WriteAllText(Path.Combine(defaultLanguageFolder, $"template_locale.json"), locfile);
            LunaLog.LogDebug($"Created language template file in {defaultLanguageFolder}");

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
            LunaLog.LogWarn($"Default language folder {defaultLanguageFolder} does not exist. Creating it. Fallback string keys will be used instead.");
            Directory.CreateDirectory(defaultLanguageFolder);
            return;
        }

        var files = Directory.GetFiles(defaultLanguageFolder, "*.json", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            try
            {
                var locale = Language.LoadFromFile(file);
                if (locale is null)
                {
                    LunaLog.LogWarn($"Locale {file} was an incorrect localization file. It have been skipped.");
                    continue;
                }

                if (Languages.ContainsKey(locale.LangCode) && !overwrite)
                {
                    LunaLog.LogWarn($"A concurrent locale with code {locale.LangCode} already exists. Skipping this one. ({file})");
                    continue;
                }
                else if (Languages.ContainsKey(locale.LangCode) && overwrite)
                {
                    Languages[locale.LangCode] = locale;
                    continue;
                }

                Languages.Add(locale.LangCode, locale);
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
            LunaLog.LogInfo($"Language set to {CurrentLanguage.LangCode}.");
            Program.Settings.Language = CurrentLanguage.LangCode;
        }
        else if (DefaultLanguage != null)
        {
            LunaLog.LogWarn($"Unable to set language to {langcode}. Language not found. Defaulting to {defaultLanguage}.");
            CurrentLanguage = DefaultLanguage;
            Program.Settings.Language = CurrentLanguage.LangCode;
        }
        else
        {
            LunaLog.LogError($"Unable to set language to {langcode}. No locale file found. Using keys instead.");
            CurrentLanguage = null;
        }

    }

    public static string Get(string key, params object[] args)
    {
        if (CurrentLanguage != null && CurrentLanguage.strings.TryGetValue(key, out string? fmt) && fmt != string.Empty)
        {
            return string.Format(fmt, args);
        }
        else if (CurrentLanguage != null && !CurrentLanguage.strings.TryGetValue(key, out fmt))
        {
            CurrentLanguage.strings.Add(key, "");
        }

        if (UseFallbackLanguage && DefaultLanguage != null && DefaultLanguage.strings.TryGetValue(key, out fmt) && fmt != string.Empty)
        {
            return string.Format(fmt, args);
        }
        else if (DefaultLanguage != null && !DefaultLanguage.strings.TryGetValue(key, out fmt))
        {
            DefaultLanguage.strings.Add(key, "");
        }

        return key;
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

        LunaLog.LogInfo("Saved primary language files.");

        if (!all)
            return;

        foreach (var lang in Languages)
        {
            if (lang.Value == CurrentLanguage || lang.Value == DefaultLanguage)
                continue;
            var locfile = JsonConvert.SerializeObject(lang.Value, Formatting.Indented);
            File.WriteAllText(lang.Value.Filepath, locfile);
        }

        LunaLog.LogInfo("Saved all language files.");
    }
}