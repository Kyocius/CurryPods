using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;

namespace OppoPodsManager.Assets.Localization;

// 为 XAML 提供基于资源键的本地化标记扩展。
[MarkupExtensionReturnType(ReturnType = typeof(string))]
public class Translate : MarkupExtension
{
    public Translate()
    {
    }

    public Translate(string key)
    {
        Key = key;
    }

    public string Key { get; set; } = string.Empty;

    protected override object ProvideValue()
    {
        return TranslationCatalog.Get(Key);
    }

    protected override object ProvideValue(IXamlServiceProvider serviceProvider)
    {
        var value = TranslationCatalog.Get(Key);
        if (serviceProvider?.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget target
            && target.TargetObject is DependencyObject targetObject
            && target.TargetProperty is DependencyProperty targetProperty)
        {
            TranslationCatalog.Register(targetObject, targetProperty, Key);
        }

        return value;
    }
}

public sealed class TranslateExtension : Translate
{
    public TranslateExtension()
    {
    }

    public TranslateExtension(string key) : base(key)
    {
    }
}



internal static class TranslationCatalog
{
    private static readonly object RegistrationGate = new();
    private static readonly List<TranslationTarget> Targets = new();

    private static IReadOnlyDictionary<string, string> _values = LoadValues("zh");
    private static string _language = "zh";

    public static event Action? LanguageChanged;

    public static string CurrentLanguage => Volatile.Read(ref _language);

    public static void Register(DependencyObject targetObject, DependencyProperty targetProperty, string key)
    {
        lock (RegistrationGate)
        {
            Targets.RemoveAll(target => !target.Object.TryGetTarget(out _));
            Targets.Add(new TranslationTarget(targetObject, targetProperty, key));
        }
    }

    public static string Get(string key)
    {
        var values = Volatile.Read(ref _values);
        return values.TryGetValue(key, out var value) ? value : key.Replace('_', ' ');
    }

    public static void SetLanguage(string language)
    {
        var catalogLanguage = NormalizeLanguage(language);
        Volatile.Write(ref _language, catalogLanguage);
        Volatile.Write(ref _values, LoadValues(catalogLanguage));
        RefreshTargets();
        LanguageChanged?.Invoke();
    }

    private static void RefreshTargets()
    {
        TranslationTarget[] targets;
        lock (RegistrationGate)
        {
            targets = Targets.ToArray();
            Targets.RemoveAll(target => !target.Object.TryGetTarget(out _));
        }

        foreach (var target in targets)
        {
            if (!target.Object.TryGetTarget(out var targetObject))
                continue;

            targetObject.DispatcherQueue?.TryEnqueue(() =>
            {
                try
                {
                    targetObject.SetValue(target.Property, Get(target.Key));
                }
                catch (Exception)
                {
                }
            });
        }
    }

    private sealed class TranslationTarget
    {
        public TranslationTarget(DependencyObject targetObject, DependencyProperty property, string key)
        {
            Object = new WeakReference<DependencyObject>(targetObject);
            Property = property;
            Key = key;
        }

        public WeakReference<DependencyObject> Object { get; }
        public DependencyProperty Property { get; }
        public string Key { get; }
    }

    private static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return "zh";

        if (language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            return "en";
        if (language.StartsWith("de", StringComparison.OrdinalIgnoreCase))
            return "de";
        if (language.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
            return "ru";
        return "zh";
    }

    private static IReadOnlyDictionary<string, string> LoadValues(string language)
    {
        var resourceName = language switch
        {
            "en" => "OppoPodsManager.Assets.Localization.Strings.en.json",
            "de" => "OppoPodsManager.Assets.Localization.Strings.de.json",
            "ru" => "OppoPodsManager.Assets.Localization.Strings.ru.json",
            _ => "OppoPodsManager.Assets.Localization.Strings.json"
        };
        using var stream = typeof(TranslationCatalog).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return new Dictionary<string, string>();

        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty);
    }
}
