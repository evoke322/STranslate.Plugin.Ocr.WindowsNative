using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Windows.Globalization;
using Windows.Media.Ocr;

namespace STranslate.Plugin.Ocr.WindowsNative.ViewModel;

/// <summary>
/// 设置界面视图模型：语言选择、刷新、可用性检测。
/// </summary>
public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IPluginContext _context;
    private readonly Settings _settings;

    public SettingsViewModel(IPluginContext context, Settings settings)
    {
        _context = context;
        _settings = settings;

        // 初始化语言列表
        RefreshLanguages();

        // 设置当前选中语言
        SelectedLanguage = AvailableLanguages
            .FirstOrDefault(l => l.LanguageTag == settings.LanguageTag)
            ?? AvailableLanguages.FirstOrDefault();

        UseCount = settings.UseCount;
    }

    /// <summary>
    /// 可用的 OCR 语言列表。
    /// </summary>
    [ObservableProperty] public partial List<LanguageInfo> AvailableLanguages { get; set; } = [];

    /// <summary>
    /// 当前选中的语言。
    /// </summary>
    [ObservableProperty] public partial LanguageInfo? SelectedLanguage { get; set; }

    /// <summary>
    /// OCR 引擎是否可用。
    /// </summary>
    [ObservableProperty] public partial bool IsOcrAvailable { get; set; }

    /// <summary>
    /// 使用次数。
    /// </summary>
    [ObservableProperty] public partial long UseCount { get; set; }

    /// <summary>
    /// OCR 引擎当前识别语言（只读信息）。
    /// </summary>
    [ObservableProperty] public partial string? EngineLanguage { get; set; }

    partial void OnSelectedLanguageChanged(LanguageInfo? value)
    {
        if (value == null)
        {
            _settings.LanguageTag = string.Empty;
        }
        else
        {
            _settings.LanguageTag = value.LanguageTag;
        }
        _context.SaveSettingStorage<Settings>();

        // 更新引擎语言显示
        UpdateEngineInfo();
    }

    partial void OnUseCountChanged(long value)
    {
        _settings.UseCount = value;
        _context.SaveSettingStorage<Settings>();
    }

    /// <summary>
    /// 刷新系统可用 OCR 语言列表。
    /// </summary>
    [RelayCommand]
    private void RefreshLanguages()
    {
        try
        {
            var availableLangs = OcrEngine.AvailableRecognizerLanguages;
            IsOcrAvailable = availableLangs.Any();
            _context.Logger.LogDebug("OCR 引擎可用: {IsAvailable}, 语言包数量: {Count}", IsOcrAvailable, availableLangs.Count);

            var languages = new List<LanguageInfo>();

            if (IsOcrAvailable)
            {
                foreach (var lang in availableLangs)
                {
                    languages.Add(new LanguageInfo(lang.LanguageTag, lang.DisplayName));
                }
            }

            AvailableLanguages =
            [
                new LanguageInfo(string.Empty, _context.GetTranslation("STranslate_Plugin_Ocr_WindowsNative_AutoDetect")),
                .. languages.OrderBy(l => l.DisplayName),
            ];

            _context.Logger.LogInformation(
                "已加载 {Count} 个 OCR 语言包",
                languages.Count);

            // 更新引擎信息
            UpdateEngineInfo();
        }
        catch (Exception ex)
        {
            _context.Logger.LogError(ex, "刷新 OCR 语言列表失败");
            AvailableLanguages =
            [
                new LanguageInfo(string.Empty, _context.GetTranslation("STranslate_Plugin_Ocr_WindowsNative_AutoDetect")),
            ];
            IsOcrAvailable = false;
        }
    }

    /// <summary>
    /// 更新引擎信息展示。
    /// </summary>
    private void UpdateEngineInfo()
    {
        try
        {
            OcrEngine? engine = null;

            if (string.IsNullOrWhiteSpace(SelectedLanguage?.LanguageTag))
            {
                engine = OcrEngine.TryCreateFromUserProfileLanguages();
            }
            else
            {
                engine = OcrEngine.TryCreateFromLanguage(
                    new Windows.Globalization.Language(SelectedLanguage!.LanguageTag));
            }

            if (engine != null)
            {
                EngineLanguage = $"{engine.RecognizerLanguage.DisplayName} ({engine.RecognizerLanguage.LanguageTag})";
            }
            else
            {
                EngineLanguage = _context.GetTranslation("STranslate_Plugin_Ocr_WindowsNative_EngineNotAvailable");
            }
        }
        catch (Exception ex)
        {
            _context.Logger.LogError(ex, "获取引擎信息失败");
            EngineLanguage = _context.GetTranslation("STranslate_Plugin_Ocr_WindowsNative_EngineNotAvailable");
        }
    }

    public void Dispose()
    {
        // 无额外资源需要释放
    }
}

/// <summary>
/// 语言信息：供 ComboBox 绑定。
/// </summary>
public class LanguageInfo
{
    public string LanguageTag { get; }
    public string DisplayName { get; }

    public LanguageInfo(string languageTag, string displayName)
    {
        LanguageTag = languageTag;
        DisplayName = displayName;
    }
}