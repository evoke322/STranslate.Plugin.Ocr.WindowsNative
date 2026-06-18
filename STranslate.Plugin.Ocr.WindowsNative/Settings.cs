namespace STranslate.Plugin.Ocr.WindowsNative;

/// <summary>
/// 插件配置：记录用户选择的 OCR 语言及使用次数。
/// </summary>
public class Settings
{
    /// <summary>
    /// OCR 识别语言 Tag，如 "zh-CN"、"en-US"。
    /// 空字符串表示自动检测（使用 TryCreateFromUserProfileLanguages）。
    /// </summary>
    public string LanguageTag { get; set; } = string.Empty;

    /// <summary>
    /// OCR 使用次数统计。
    /// </summary>
    public long UseCount { get; set; } = 0;
}