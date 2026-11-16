using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using STranslate.Plugin.Ocr.WindowsNative.View;
using STranslate.Plugin.Ocr.WindowsNative.ViewModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
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
    /// <summary>
    /// Windows.Media.Ocr 的语言能力取决于系统语言包，此处声明全部 LangEnum，
    /// 实际可用性在识别时检测；缓存数组避免每次访问分配。
    /// </summary>
    private static readonly LangEnum[] AllLanguages = Enum.GetValues<LangEnum>();

    private Control? _settingUi;
    private SettingsViewModel? _viewModel;
    private Settings Settings { get; set; } = null!;
    private IPluginContext Context { get; set; } = null!;

    public IEnumerable<LangEnum> SupportedLanguages => AllLanguages;

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

    /// <summary>
    /// 声明本插件返回图片像素坐标的文本框，从而可被注册为图片翻译 OCR 服务。
    /// Windows.Media.Ocr 的 <see cref="Windows.Media.Ocr.OcrWord.BoundingRect"/>
    /// 直接对应图片像素位置，满足图片翻译的坐标需求。
    /// </summary>
    public bool SupportBoxPoints() => true;

    public async Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        Context.Logger.LogDebug(
            "开始 OCR 识别，图片大小: {ImageSize} bytes, 目标语言: {Language}",
            request.ImageData.Length,
            request.Language);

        cancellationToken.ThrowIfCancellationRequested();

        // 1. byte[] -> SoftwareBitmap
        //   统一解码为 Bgra8 + Premultiplied（OcrEngine 要求的格式），避免 16 位 PNG、
        //   灰度图等原生格式直接进入识别导致失败；EXIF 方向统一按 Respect 处理，
        //   保证是否触发缩放的两条路径行为一致。
        SoftwareBitmap softwareBitmap;
        uint originalWidth, originalHeight;
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(request.ImageData);
                await writer.StoreAsync().AsTask(cancellationToken);
                writer.DetachStream();
            }
            stream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);

            // 先读元数据尺寸（含 EXIF 方向），超限时只解码一次（直接带缩放变换）
            originalWidth = decoder.OrientedPixelWidth;
            originalHeight = decoder.OrientedPixelHeight;
            var maxDimension = OcrEngine.MaxImageDimension;

            if (originalWidth > maxDimension || originalHeight > maxDimension)
            {
                Context.Logger.LogWarning(
                    "图片尺寸 ({Width}x{Height}) 超过限制 {Max}px，执行缩放",
                    originalWidth, originalHeight, maxDimension);

                var scale = Math.Min((double)maxDimension / originalWidth, (double)maxDimension / originalHeight);
                var transform = new BitmapTransform
                {
                    ScaledWidth = Math.Max(1u, (uint)Math.Round(originalWidth * scale)),
                    ScaledHeight = Math.Max(1u, (uint)Math.Round(originalHeight * scale)),
                    InterpolationMode = BitmapInterpolationMode.Fant,
                };

                softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    transform,
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.DoNotColorManage).AsTask(cancellationToken);
            }
            else
            {
                softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    new BitmapTransform(),
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.DoNotColorManage).AsTask(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Context.Logger.LogError(ex, "图片解码失败");
            return new OcrResult().Fail(
                Context.GetTranslation("STranslate_Plugin_Ocr_WindowsNative_ImageDecodeFailed"));
        }

        // 坐标还原比例：识别在（可能被缩放的）位图上进行，BoxPoints 需映射回原图像素
        var scaleX = (float)originalWidth / softwareBitmap.PixelWidth;
        var scaleY = (float)originalHeight / softwareBitmap.PixelHeight;

        // 2. 创建 OCR 引擎
        //   优先采用宿主传入的 per-request 语言（request.Language → BCP-47 tag），
        //   不受支持时回落到用户设置语言，均为空或不可用则走自动检测；
        //   全部失败时返回明确的"语言不可用"错误（而非把宿主语言问题误报为自动检测失败）。
        OcrEngine? engine = null;
        string? attemptedTag = null;

        var requestTag = GetLanguage(request.Language);
        if (!string.IsNullOrWhiteSpace(requestTag))
        {
            attemptedTag = requestTag;
            engine = TryCreateEngine(requestTag);
            if (engine == null)
            {
                Context.Logger.LogWarning(
                    "宿主指定语言 {Tag} 不可用，回落到插件设置语言", requestTag);
            }
        }

        if (engine == null && !string.IsNullOrWhiteSpace(Settings.LanguageTag))
        {
            attemptedTag = Settings.LanguageTag;
            engine = TryCreateEngine(Settings.LanguageTag);
            if (engine == null)
            {
                Context.Logger.LogWarning(
                    "设置语言 {Tag} 不可用，回落到自动检测", Settings.LanguageTag);
            }
        }

        if (engine == null)
        {
            engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null)
            {
                softwareBitmap.Dispose();
                string msg;
                if (attemptedTag != null)
                {
                    msg = string.Format(
                              Context.GetTranslation("STranslate_Plugin_Ocr_WindowsNative_LanguageNotSupported"),
                              attemptedTag) + "\n" +
                          Context.GetTranslation("STranslate_Plugin_Ocr_WindowsNative_LanguageNotSupported_Hint");
                }
                else
                {
                    msg = Context.GetTranslation("STranslate_Plugin_Ocr_WindowsNative_AutoLanguageFailed") + "\n" +
                          Context.GetTranslation("STranslate_Plugin_Ocr_WindowsNative_AutoLanguageFailed_Hint");
                }
                return new OcrResult().Fail(msg);
            }
        }

        // 3. 执行 OCR 识别，并在完成后释放 SoftwareBitmap 句柄
        cancellationToken.ThrowIfCancellationRequested();

        Windows.Media.Ocr.OcrResult? ocrResult;
        try
        {
            ocrResult = await engine.RecognizeAsync(softwareBitmap).AsTask(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Context.Logger.LogError(ex, "OCR 识别失败");
            return new OcrResult().Fail(
                Context.GetTranslation("STranslate_Plugin_Ocr_WindowsNative_RecognizeFailed"));
        }
        finally
        {
            softwareBitmap.Dispose();
        }

        if (string.IsNullOrWhiteSpace(ocrResult?.Text))
        {
            Context.Logger.LogWarning("OCR 识别结果为空");
            return new OcrResult().Fail(
                Context.GetTranslation("STranslate_Plugin_Ocr_WindowsNative_EmptyResult"));
        }

        // 4. 组装返回结果 — 按行组织，每行包含 BoxPoints 位置信息
        var result = new OcrResult
        {
            Language = engine.RecognizerLanguage.LanguageTag,
        };

        foreach (var line in ocrResult.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Text))
                continue;

            var content = new OcrContent { Text = NormalizeRecognizedText(line.Text) };

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

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;

        Context.Logger.LogInformation(
            "OCR 识别完成，共 {LineCount} 行，文本长度: {TextLength}，耗时: {Duration}ms",
            result.OcrContents.Count,
            result.OcrContents.Sum(c => c.Text?.Length ?? 0),
            stopwatch.ElapsedMilliseconds);

        return result;
    }

    /// <summary>
    /// 按 BCP-47 tag 尝试创建引擎；语言不受支持或创建失败返回 null（由调用方回落）。
    /// </summary>
    private static OcrEngine? TryCreateEngine(string languageTag)
    {
        try
        {
            var language = new Language(languageTag);
            return OcrEngine.IsLanguageSupported(language)
                ? OcrEngine.TryCreateFromLanguage(language)
                : null;
        }
        catch (ArgumentException)
        {
            // 非法语言 tag
            return null;
        }
    }

    /// <summary>
    /// 归一化 OCR 识别文本中的空格：仅保留以下三类空格，其余一律移除。
    /// 1) 英文标点之后（如 "Hello, world" 中 ',' 后的空格）；
    /// 2) 英文单词 / 数字之间（如 "hello world"、"foo 123"）；
    /// 3) 中文与英文 / 数字之间，双向（如 "你好 world"、"版本 2"）。
    /// Windows.Media.Ocr 将每个汉字视为独立词，输出形如"你 好 世 界"，必须去除 CJK 字间空格。
    /// </summary>
    private static string NormalizeRecognizedText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var chars = text.ToCharArray();
        var result = new StringBuilder(chars.Length);

        for (var i = 0; i < chars.Length; i++)
        {
            var current = chars[i];
            if (current == ' ' && i > 0 && i < chars.Length - 1)
            {
                // 与已写入的末字符比较（而非原数组前一字符），
                // 连续多个空格按单个空格处理，避免 "hello  world" 被粘连成 "helloworld"
                var left = result.Length > 0 ? result[^1] : chars[i - 1];
                if (!ShouldKeepSpace(left, chars[i + 1]))
                {
                    continue;
                }
            }
            result.Append(current);
        }

        return result.ToString();
    }

    /// <summary>
    /// 判断 left 与 right 之间的空格是否应予保留。
    /// </summary>
    private static bool ShouldKeepSpace(char left, char right) =>
        // 英文标点之后保留空格
        IsEnglishPunctuation(left) ||
        // 英文单词 / 数字之间保留空格
        (IsEnglishLetterOrDigit(left) && IsEnglishLetterOrDigit(right)) ||
        // 中文与英文 / 数字之间保留空格（双向）
        (TextHelper.IsCjk(left) && IsEnglishLetterOrDigit(right)) ||
        (IsEnglishLetterOrDigit(left) && TextHelper.IsCjk(right));

    /// <summary>
    /// ASCII 字母或数字。
    /// </summary>
    private static bool IsEnglishLetterOrDigit(char c) =>
        c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';

    /// <summary>
    /// ASCII 标点或符号（非字母、非数字、非空白、非控制字符）。
    /// 覆盖常见英文标点：, . ! ? ; : ' " ( ) [ ] { } / \ @ # $ % ^ &amp; * _ + = ~ ` &lt; &gt; 等。
    /// </summary>
    private static bool IsEnglishPunctuation(char c) =>
        c < 128 && !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c) && !char.IsControl(c);
}