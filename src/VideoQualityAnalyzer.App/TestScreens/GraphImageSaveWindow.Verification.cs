using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

// 画像保存画面のサンプル表示と自動検証。
internal sealed partial class GraphImageSaveWindow
{
    private async Task VerifyAsync(string directory)
    {
        var savedAt = new DateTime(2026, 11, 14, 9, 8, 7);
        if (referenceFileName != "サンプル基準動画.mkv" ||
            PngFileName(savedAt, referenceFileName, QualityMetric.Vmaf, GraphImageTarget.Frames) !=
                "20261114-090807_サンプル基準動画.mkv_フレームグラフ_VMAF.png" ||
            PngFileName(savedAt, "基準動画.mp4", QualityMetric.Ssim, GraphImageTarget.Files) !=
                "20261114-090807_基準動画.mp4_ファイルグラフ_SSIM.png")
            throw new InvalidOperationException("PNGの初期ファイル名が命名規則と一致しません。");
        if (CreateSaveDialog(savedAt).InitialDirectory !=
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures))
            throw new InvalidOperationException("保存先の初期フォルダーがピクチャではありません。");
        UpdateLayout(); resizeTimer.Stop(); RefreshPreview();
        if (saveButton.ActualHeight < 31 ||
            saveButton.BorderBrush is not SolidColorBrush saveBorder ||
            saveBorder.Color != ((SolidColorBrush)UiTheme.ControlBorder).Color)
            throw new InvalidOperationException("PNG保存ボタンの高さまたは枠線が主画面と一致しません。");
        if (size.Items.Count != 9 || size.SelectedIndex != 3 || presets[1] != (1024, 768) ||
            presets[2] != (1280, 960) || presets[3] != (1600, 1200) ||
            metricSelection.Items.Count != 3 || metricSelection.SelectedIndex != 0 ||
            font.Items.Count != 6 || font.Items.Contains("3倍") ||
            fitToWindow || zoom.Value != 100 || zoomLabel.Text != "100%" ||
            RequirePreview() is not { PixelWidth: 1024, PixelHeight: 768 })
            throw new InvalidOperationException("画像サイズまたは表示倍率の初期値が不正です。");
        static Color FirstPixel(BitmapSource image)
        {
            var pixel = new byte[4];
            image.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
            return Color.FromRgb(pixel[2], pixel[1], pixel[0]);
        }
        if (imageTheme.Items.Count != 2 || CurrentImageTheme != GraphImageTheme.Light ||
            FirstPixel(RequirePreview()) != Colors.White)
            throw new InvalidOperationException("出力画像の初期テーマがライトではありません。");
        var lightImage = preview.Source;
        imageTheme.SelectedIndex = (int)GraphImageTheme.Dark;
        if (CurrentImageTheme != GraphImageTheme.Dark || ReferenceEquals(lightImage, preview.Source) ||
            FirstPixel(RequirePreview()) != ((SolidColorBrush)GraphPalette.ForExport(GraphImageTheme.Dark).Surface).Color)
            throw new InvalidOperationException("ダーク画像への切り替えがプレビューへ即時反映されません。");
        var darkPng = Path.Combine(directory, "dark-theme.png");
        SavePng(darkPng);
        using (var darkStream = File.OpenRead(darkPng))
        {
            var saved = new PngBitmapDecoder(darkStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (FirstPixel(saved.Frames[0]) != FirstPixel(RequirePreview()))
                throw new InvalidOperationException("保存したダークPNGとプレビューの配色が一致しません。");
        }
        imageTheme.SelectedIndex = (int)GraphImageTheme.Light;
        if (FirstPixel(RequirePreview()) != Colors.White)
            throw new InvalidOperationException("ライト画像へ即時に戻せません。");
        foreach (var targetIndex in new[] { 0, 1 })
        foreach (var metric in Enum.GetValues<QualityMetric>())
        {
            metricSelection.SelectedIndex = (int)metric; UpdateLayout();
            target.SelectedIndex = targetIndex; RefreshPreview();
            if (metricSelection.SelectedIndex != (int)metric)
                throw new InvalidOperationException("評価方式の選択がプレビューに反映されません。");
            if (graph.FilePointCount != 30 || graph.DisplayedVideoIds.Count != 30)
                throw new InvalidOperationException("サンプルの表示件数が30件ではありません。");
            if (preview.Source is not BitmapSource) throw new InvalidOperationException("プレビューがありません。");
            var image = RequirePreview();
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = File.Create(Path.Combine(directory, $"responsive-{metric}-{targetIndex}.png")); encoder.Save(stream);
        }
        var previousMetricImage = preview.Source;
        metricSelection.SelectedIndex = (int)QualityMetric.Vmaf;
        if (graph.SelectedMetric != QualityMetric.Vmaf || preview.Source is null ||
            ReferenceEquals(previousMetricImage, preview.Source))
            throw new InvalidOperationException("プレビューの評価方式選択がグラフと画像に反映されません。");
        bool MatchesWindowRatio((int Width, int Height) image)
        {
            var available = AvailableSize();
            var dpi = VisualTreeHelper.GetDpi(this);
            return Math.Abs(image.Width / (double)image.Height -
                available.Width * dpi.DpiScaleX / (available.Height * dpi.DpiScaleY)) < 0.01;
        }
        foreach (var mode in Enumerable.Range(0, size.Items.Count))
        {
            size.SelectedIndex = mode; RefreshPreview();
            var expected = ImageSize();
            if (preview.Source is null && mode is not (3 or 4 or 5) && status.Text.Contains("不足")) continue;
            if (preview.Source is not BitmapSource image || image.PixelWidth != expected.Width || image.PixelHeight != expected.Height)
                throw new InvalidOperationException("出力寸法が選択に一致しません。");
            if (mode >= 2 && (expected != presets[mode - 2] || expected.Width * 3 != expected.Height * 4))
                throw new InvalidOperationException("4:3のプリセットが不正です。");
            if (mode == 0 && !MatchesWindowRatio(expected))
                throw new InvalidOperationException("自動サイズがウィンドウの縦横比に合いません。");
        }
        size.SelectedIndex = 0; RefreshPreview();
        var before = RequirePreview();
        Width = 1600; Height = 1000; UpdateLayout();
        // 実際のリサイズ通知からの自動更新を待つ。明示的な再生成では代用しない。
        await Task.Delay(350);
        var after = RequirePreview();
        if (before.PixelWidth == after.PixelWidth || before.PixelHeight == after.PixelHeight ||
            !MatchesWindowRatio((after.PixelWidth, after.PixelHeight)) ||
            Math.Abs(before.PixelWidth / (double)before.PixelHeight - after.PixelWidth / (double)after.PixelHeight) < 0.01)
            throw new InvalidOperationException("画像サイズがウィンドウの縦横比に追従しません。");
        // 30件の凡例と最大倍率が収まる4K出力で全倍率を照合する。
        size.SelectedIndex = 8; RefreshPreview();
        for (var index = 0; index < font.Items.Count; index++)
        {
            font.SelectedIndex = index; RefreshPreview();
            var image = RequirePreview();
            var expected = CreateImage();
            var fontEncoder = new PngBitmapEncoder(); fontEncoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(Path.Combine(directory, $"responsive-font-{index}.png"))) fontEncoder.Save(stream);
            var stride = image.PixelWidth * 4; var pixels = new byte[stride * image.PixelHeight];
            var other = new byte[pixels.Length]; image.CopyPixels(pixels, stride, 0); expected.CopyPixels(other, stride, 0);
            var colored = 0;
            for (var pixel = 0; pixel < pixels.Length; pixel += 4)
                if (Math.Max(pixels[pixel], Math.Max(pixels[pixel + 1], pixels[pixel + 2])) -
                    Math.Min(pixels[pixel], Math.Min(pixels[pixel + 1], pixels[pixel + 2])) > 60) colored++;
            if (colored < 100 || !pixels.SequenceEqual(other)) throw new InvalidOperationException("系列の描画または出力画像の一致が不正です。");
        }
        // 同じ出力設定は通常・最大化・別サイズのウィンドウで全画素一致しなければならない。
        static byte[] Pixels(BitmapSource bitmap)
        {
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels;
        }
        size.SelectedIndex = 8;
        var fixedImages = new Dictionary<(int Target, int Font), byte[]>();
        foreach (var targetIndex in new[] { 0, 1 })
        for (var fontIndex = 0; fontIndex < font.Items.Count; fontIndex++)
        {
            target.SelectedIndex = targetIndex; font.SelectedIndex = fontIndex; RefreshPreview();
            fixedImages[(targetIndex, fontIndex)] = Pixels(RequirePreview());
        }
        foreach (var state in new[] { WindowState.Maximized, WindowState.Normal })
        {
            WindowState = state;
            if (state == WindowState.Normal) { Width = 1600; Height = 950; }
            UpdateLayout(); await Task.Delay(350);
            foreach (var targetIndex in new[] { 0, 1 })
            for (var fontIndex = 0; fontIndex < font.Items.Count; fontIndex++)
            {
                target.SelectedIndex = targetIndex; font.SelectedIndex = fontIndex; RefreshPreview();
                if (preview.Source is not BitmapSource image || image.PixelWidth != 3840 || image.PixelHeight != 2880 ||
                    !fixedImages[(targetIndex, fontIndex)].SequenceEqual(Pixels(image)))
                    throw new InvalidOperationException("固定解像度の画像がウィンドウサイズによって変化しました。");
            }
        }
        // 出力解像度が変わっても、同じピクセル指定のタイトルは同じ画素で描画される。
        static byte[] TitlePixels(BitmapSource bitmap)
        {
            var pixels = new byte[600 * 20 * 4];
            bitmap.CopyPixels(new Int32Rect(12, 12, 600, 20), pixels, 600 * 4, 0); return pixels;
        }
        font.SelectedIndex = 2;
        foreach (var targetIndex in new[] { 0, 1 })
        {
            target.SelectedIndex = targetIndex; size.SelectedIndex = 4; RefreshPreview();
            var title = TitlePixels(RequirePreview());
            size.SelectedIndex = 5; RefreshPreview();
            if (!title.SequenceEqual(TitlePixels(RequirePreview())))
                throw new InvalidOperationException("解像度変更で文字のピクセルサイズが変化しました。");
            size.SelectedIndex = 1; dimension.Text = "1280"; heightDimension.Text = "960"; RefreshPreview();
            WindowState = WindowState.Maximized; UpdateLayout(); await Task.Delay(350);
            if (preview.Source is not BitmapSource { PixelWidth: 1280, PixelHeight: 960 } fixedWidth ||
                !title.SequenceEqual(TitlePixels(fixedWidth)))
                throw new InvalidOperationException("最大化した1280×960指定で文字サイズが変化しました。");
            WindowState = WindowState.Normal; UpdateLayout(); await Task.Delay(350);
        }
        target.SelectedIndex = 1; size.SelectedIndex = 4; font.SelectedIndex = 3; RefreshPreview();
        var original = preview.Source;
        fitToWindow = false; zoom.Value = 200; ApplyZoom(); UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(this); var bitmap = RequirePreview();
        if (Math.Abs(preview.Width - bitmap.PixelWidth * 2 / dpi.DpiScaleX) > 0.1 || !ReferenceEquals(original, preview.Source) ||
            scroll.ExtentWidth <= scroll.ViewportWidth || scroll.ExtentHeight <= scroll.ViewportHeight)
            throw new InvalidOperationException("拡大表示が画像サイズを変更しました。");
        zoom.Value = 100; ApplyZoom(); UpdateLayout();
        if (Math.Abs(preview.Width - bitmap.PixelWidth / dpi.DpiScaleX) > 0.1) throw new InvalidOperationException("100%表示が不正です。");
        fitToWindow = true; ApplyZoom(); UpdateLayout();
        if (preview.Width > AvailableSize().Width + 1 || preview.Height > AvailableSize().Height + 1)
            throw new InvalidOperationException("ウィンドウに収まりません。");
        font.SelectedIndex = 3; RefreshPreview(); UpdateLayout();
        var screenshot = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        screenshot.Render(this); var shotEncoder = new PngBitmapEncoder(); shotEncoder.Frames.Add(BitmapFrame.Create(screenshot));
        using (var stream = File.Create(Path.Combine(directory, "preview-window.png"))) shotEncoder.Save(stream);
        Width = MinWidth; UpdateLayout(); await Task.Delay(350);
        var narrowScreenshot = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        narrowScreenshot.Render(this);
        var narrowEncoder = new PngBitmapEncoder(); narrowEncoder.Frames.Add(BitmapFrame.Create(narrowScreenshot));
        using (var stream = File.Create(Path.Combine(directory, "preview-window-narrow.png"))) narrowEncoder.Save(stream);
        size.SelectedIndex = 1;
        dimension.Text = "入力エラー";
        if (preview.Source is not null) throw new InvalidOperationException("不正な入力で古い画像が残っています。");
        dimension.Text = "1280";
        if (preview.Source is not BitmapSource { PixelWidth: 1280 }) throw new InvalidOperationException("入力修正後に画像が復元されません。");
        size.SelectedIndex = 1; dimension.Text = "200"; heightDimension.Text = "100"; RefreshPreview();
        if (preview.Source is not null || !status.Text.Contains("不足"))
            throw new InvalidOperationException("描画領域が不足しても文字を縮小せずエラーを表示する必要があります。");
        size.SelectedIndex = 4; RefreshPreview();
        graph.SelectFile("sample-1"); RefreshPreview();
        if (!graph.DisplayedVideoIds.SequenceEqual(new[] { "sample-1" })) throw new InvalidOperationException("動画選択が反映されません。");
        graph.UpdateData([], [], null); RefreshPreview();
        if (preview.Source is null) throw new InvalidOperationException("空のデータを出力できません。");
        var production = new GraphImageSaveWindow(CreateSampleSource());
        try
        {
            production.Show(); production.UpdateLayout(); await Task.Delay(150);
            production.RefreshPreview(); production.UpdateLayout();
            if (production.Content is not DockPanel { Children.Count: 3 } ||
                production.preview.Source is not BitmapSource { PixelWidth: 1024, PixelHeight: 768 } ||
                production.referenceFileName != "サンプル基準動画.mkv")
                throw new InvalidOperationException("本体用の画像保存画面を開けません。");
            production.imageTheme.SelectedIndex = (int)GraphImageTheme.Dark;
            if (production.CurrentImageTheme != GraphImageTheme.Dark ||
                FirstPixel(production.RequirePreview()) !=
                    ((SolidColorBrush)GraphPalette.ForExport(GraphImageTheme.Dark).Surface).Color)
                throw new InvalidOperationException("本体用の画像保存画面でダーク画像に切り替えられません。");
            var savedPng = Path.Combine(directory, "production-save.png");
            production.SavePng(savedPng);
            using (var savedStream = File.OpenRead(savedPng))
            {
                var savedImage = new PngBitmapDecoder(savedStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                if (savedImage.Frames[0].PixelWidth != 1024 || savedImage.Frames[0].PixelHeight != 768)
                    throw new InvalidOperationException("保存したPNGの寸法がプレビューと一致しません。");
                if (FirstPixel(savedImage.Frames[0]) !=
                    ((SolidColorBrush)GraphPalette.ForExport(GraphImageTheme.Dark).Surface).Color)
                    throw new InvalidOperationException("本体用の保存画像がダーク配色ではありません。");
            }
            production.imageTheme.SelectedIndex = (int)GraphImageTheme.Light;
            if (FirstPixel(production.RequirePreview()) != Colors.White)
                throw new InvalidOperationException("本体用の画像保存画面でライト画像に戻せません。");
            production.UpdateLayout();
            var productionShot = new RenderTargetBitmap((int)production.ActualWidth, (int)production.ActualHeight,
                96, 96, PixelFormats.Pbgra32);
            productionShot.Render(production);
            var productionEncoder = new PngBitmapEncoder();
            productionEncoder.Frames.Add(BitmapFrame.Create(productionShot));
            using var stream = File.Create(Path.Combine(directory, "save-window.png"));
            productionEncoder.Save(stream);
        }
        finally { production.Close(); }
        File.WriteAllText(Path.Combine(directory, "result.txt"), "合格：PNGの初期ファイル名、ライト・ダーク画像の即時切り替えと保存、30件、3指標×2対象、評価方式の双方向切り替え、9サイズ選択（1024×768・1600×1200を含む）、初期値1024×768・表示100%、6フォント倍率、通常・最大化・リサイズ時の全画素一致、解像度変更時のタイトル画素一致、1280×960指定、200%・100%・全体表示。サンプルは架空データ。");
        resizeTimer.Stop(); Console.WriteLine("グラフ画像保存テスト：合格");
    }

    private void SetSample()
    {
        var source = CreateSampleSource();
        SetGraph(source.SelectedVideos, source.AllVideos, source.Results, source.Reference.Name, source.Groups);
        status.Text = "30本の架空サンプルを表示中（実測品質ではありません）。実データはSSIM検証用JSONを読み込めます。凡例・選択・指標を変えて保存を確認してください。";
    }

    private static GraphImageSaveSource CreateSampleSource()
    {
        var videos = Enumerable.Range(0, 30).Select(index => new VideoDescriptor($"sample-{index}",
            $"サンプル動画{index + 1:00}.mkv", 1920, 1080, "av1", 30, 30, TimeSpan.FromSeconds(30), false, 900, 1,
            1_000_000 + index * 100_000)).ToArray();
        var results = new Dictionary<(string, QualityMetric), QualityResult>();
        foreach (var metric in Enum.GetValues<QualityMetric>())
        for (var index = 0; index < videos.Length; index++)
        {
            var qualityLevel = index * 3d / (videos.Length - 1);
            var frames = Enumerable.Range(0, 900).Select(number => new QualityFrame(number,
                metric switch
                {
                    QualityMetric.Ssim => 0.94 + qualityLevel * 0.012 + 0.01 * Math.Sin(number / 30d) - (number % 120 == 0 ? 0.08 : 0),
                    QualityMetric.Vmaf => 80 + qualityLevel * 4 + 3 * Math.Sin(number / 30d),
                    _ => 34 + qualityLevel * 3 + 2 * Math.Sin(number / 30d)
                })).ToArray();
            results[(videos[index].Id, metric)] = new(metric, frames.Average(frame => frame.Value), frames);
        }
        var reference = new VideoDescriptor("sample-reference", "サンプル基準動画.mkv", 1920, 1080,
            "av1", 30, 30, TimeSpan.FromSeconds(30), false, 900, 1);
        return new GraphImageSaveSource(reference, videos, videos, results,
            new Dictionary<string, string>(), QualityMetric.Vmaf);
    }}
