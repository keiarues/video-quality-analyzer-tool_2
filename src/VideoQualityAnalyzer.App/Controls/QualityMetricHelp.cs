using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

internal static class QualityMetricHelp
{
    public static string GetText(QualityMetric metric) => metric switch
    {
        QualityMetric.Vmaf => "VMAF：複数の特徴から、人が感じる映像品質を推定する指標です。一般に0～100で、大きいほど元動画に近い品質と評価します。libvmaf対応のFFmpegが必要です。",
        QualityMetric.Ssim => "SSIM：元動画と比較動画の明るさ・コントラスト・構造の類似度を評価します。1に近いほど似ています。",
        _ => "PSNR：元動画との画素の誤差を、dB（デシベル）で表します。大きいほど誤差が小さいことを示します。完全一致による無限大は現在の平均集計から除外します。"
    };
}
