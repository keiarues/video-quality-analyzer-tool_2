namespace VideoQualityAnalyzer.Core;

public sealed record VideoDescriptor(
    string Id,
    string Path,
    int Width,
    int Height,
    string Codec,
    double AverageFrameRate,
    double NominalFrameRate,
    TimeSpan Duration,
    bool HasAudio,
    long? TotalFrames,
    double PixelAspectRatio,
    long? AverageBitrate = null)
{
    // 公称値と平均値の差によるVFR推定。全フレームの時刻を検査した判定ではない。
    public bool IsVariableFrameRate => AverageFrameRate > 0 && NominalFrameRate > 0 &&
        Math.Abs(AverageFrameRate - NominalFrameRate) > 0.01;

    public string Name => System.IO.Path.GetFileName(Path);
}
