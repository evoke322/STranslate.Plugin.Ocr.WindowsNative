using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using STranslate.Plugin.Ocr.WindowsNative.View;
using STranslate.Plugin.Ocr.WindowsNative.ViewModel;
using System.Windows.Controls;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace STranslate.Plugin.Ocr.WindowsNative;

/// <summary>
/// Windows 原生 OCR 插件主类 — 基于 Windows.Media.Ocr.OcrEngine 实现离线文字识别。
/// </summary>
public class Main : ObservableObject, IOcrPlugin
{
    private Control? _settingUi;
    private SettingsViewModel? _viewModel;
    private Settings Settings { get; set; } = null!;
    private IPluginContext Context { get; set; } = null!;

    /// <summary>
    /// Windows.Media.Ocr 支持所有语言，无需在此限制。
    /// </summary>
    public IEnumerable<LangEnum> SupportedLanguages => Enum.GetValues<LangEnum>();

    public Control GetSettingUI()
    {
        _viewModel ??= new SettingsViewModel(Context, Settings);
        _settingUi ??= new SettingsView { DataContext = _viewModel };
        return _settingUi;
    }

    public void Init(IPluginContext context)
    {
        Context = context;
        Settings = context.LoadSettingStorage<Settings>();

        Context.Logger.LogInformation(
            "Windows 原生 OCR 插件 v{Version} 初始化完成",
            context.MetaData.Version);
    }

    public void Dispose() => _viewModel?.Dispose();

    /// <summary>
    /// LangEnum 到 Windows 语言 Tag 的映射，返回 null 表示使用默认自动检测。
    /// </summary>
    public string? GetLanguage(LangEnum langEnum) => langEnum switch
    {
        LangEnum.Auto => null,
        LangEnum.ChineseSimplified => "zh-CN",
        LangEnum.ChineseTraditional => "zh-Hant",
        LangEnum.Cantonese => "yue",
        LangEnum.English => "en-US",
        LangEnum.Japanese => "ja-JP",
        LangEnum.Korean => "ko-KR",
        LangEnum.French => "fr-FR",
        LangEnum.German => "de-DE",
        LangEnum.Spanish => "es-ES",
        LangEnum.Russian => "ru-RU",
        LangEnum.Italian => "it-IT",
        LangEnum.PortuguesePortugal => "pt-PT",
        LangEnum.PortugueseBrazil => "pt-BR",
        LangEnum.Dutch => "nl-NL",
        LangEnum.Polish => "pl-PL",
        LangEnum.Turkish => "tr-TR",
        LangEnum.Ukrainian => "uk-UA",
        LangEnum.Vietnamese => "vi-VN",
        LangEnum.Indonesian => "id-ID",
        LangEnum.Thai => "th-TH",
        LangEnum.Malay => "ms-MY",
        LangEnum.Arabic => "ar-SA",
        LangEnum.Hindi => "hi-IN",
        LangEnum.Swedish => "sv-SE",
        LangEnum.NorwegianBokmal => "nb-NO",
        LangEnum.Persian => "fa-IR",
        LangEnum.Uzbek => "uz-Latn-UZ",
        _ => null,
    };

    public async Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken)
    {
        Context.Logger.LogDebug(
            "开始 OCR 识别，图片大小: {ImageSize} bytes, 目标语言: {Language}",
            request.ImageData.Length,
            request.Language);

        // 1. byte[] -> SoftwareBitmap
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(request.ImageData);
            await writer.StoreAsync();
            writer.DetachStream();
        }
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream);
        var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

        var originalWidth = (uint)softwareBitmap.PixelWidth;
        var originalHeight = (uint)softwareBitmap.PixelHeight;
        float scaleX = 1.0f;
        float scaleY = 1.0f;

        // 2. 检查图片尺寸，如超出则缩放
        if (originalWidth > OcrEngine.MaxImageDimension || originalHeight > OcrEngine.MaxImageDimension)
        {
            Context.Logger.LogWarning(
                "图片尺寸 ({Width}x{Height}) 超过限制 {Max}px，执行缩放",
                originalWidth, originalHeight, OcrEngine.MaxImageDimension);

            var maxDimension = OcrEngine.MaxImageDimension;
            var scale = Math.Min((double)maxDimension / originalWidth, (double)maxDimension / originalHeight);
            var scaledWidth = Math.Max(1u, (uint)Math.Round(originalWidth * scale));
            var scaledHeight = Math.Max(1u, (uint)Math.Round(originalHeight * scale));

            var bitmapTransform = new BitmapTransform
            {
                ScaledWidth = scaledWidth,
                ScaledHeight = scaledHeight,
            };
            softwareBitmap.Dispose();
            softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                bitmapTransform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);
            scaleX = (float)originalWidth / softwareBitmap.PixelWidth;
            scaleY = (float)originalHeight / softwareBitmap.PixelHeight;
        }

        // 3. 创建 OCR 引擎
        OcrEngine engine;
        var languageTag = string.IsNullOrWhiteSpace(Settings.LanguageTag)
            ? null
            : Settings.LanguageTag;

        if (languageTag != null)
        {
            var language = new Language(languageTag);
            bool supported = OcrEngine.IsLanguageSupported(language);

            if (!supported)
            {
                var msg = string.Format(Context.GetTranslation("LanguageNotSupported"), languageTag) + "\n" +
                          Context.GetTranslation("LanguageNotSupported_Hint");
                var err = new OcrResult();
                return err.Fail(msg);
            }

            engine = OcrEngine.TryCreateFromLanguage(language);
            if (engine == null)
            {
                var msg = Context.GetTranslation("EngineCreationFailed") + "\n" +
                          Context.GetTranslation("EngineCreationFailed_Hint");
                var err = new OcrResult();
                return err.Fail(msg);
            }
        }
        else
        {
            // 自动模式：尝试从用户配置文件语言创建
            engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null)
            {
                var msg = Context.GetTranslation("AutoLanguageFailed") + "\n" +
                          Context.GetTranslation("AutoLanguageFailed_Hint");
                var err = new OcrResult();
                return err.Fail(msg);
            }
        }

        // 4. 执行 OCR 识别
        var ocrResult = await engine.RecognizeAsync(softwareBitmap);

        if (string.IsNullOrWhiteSpace(ocrResult?.Text))
        {
            Context.Logger.LogWarning("OCR 识别结果为空");
            var err = new OcrResult();
            return err.Fail(Context.GetTranslation("EmptyResult"));
        }

        // 5. 组装返回结果 — 按行组织，每行包含 BoxPoints 位置信息
        var result = new OcrResult();
        var lines = ocrResult.Lines;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.Text))
                continue;

            var content = new OcrContent { Text = line.Text };

            // 计算该行所有词的综合边界框（还原到原始图片坐标）
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            bool hasPosition = false;

            foreach (var word in line.Words)
            {
                var rect = word.BoundingRect;
                if (rect.Width > 0 && rect.Height > 0)
                {
                    var left = (float)(rect.X * scaleX);
                    var top = (float)(rect.Y * scaleY);
                    var right = (float)((rect.X + rect.Width) * scaleX);
                    var bottom = (float)((rect.Y + rect.Height) * scaleY);

                    if (left < minX) minX = left;
                    if (top < minY) minY = top;
                    if (right > maxX) maxX = right;
                    if (bottom > maxY) maxY = bottom;
                    hasPosition = true;
                }
            }

            // 仅当有有效位置信息时添加四角点
            if (hasPosition)
            {
                content.BoxPoints.Add(new BoxPoint(minX, minY));       // 左上
                content.BoxPoints.Add(new BoxPoint(maxX, minY));       // 右上
                content.BoxPoints.Add(new BoxPoint(maxX, maxY));       // 右下
                content.BoxPoints.Add(new BoxPoint(minX, maxY));       // 左下
            }

            result.OcrContents.Add(content);
        }

        // 更新使用次数
        if (_viewModel != null)
            _viewModel.UseCount++;

        Context.Logger.LogInformation(
            "OCR 识别完成，共 {LineCount} 行，文本长度: {TextLength}",
            result.OcrContents.Count,
            result.OcrContents.Sum(c => c.Text?.Length ?? 0));

        return result;
    }
}
