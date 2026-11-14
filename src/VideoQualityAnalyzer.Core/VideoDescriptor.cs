namespace VideoQualityAnalyzer.Core;

public enum BitrateSource { Unknown, VideoStream, Container, FileSizeEstimate }

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
    long? AverageBitrate = null,
    BitrateSource BitrateSource = BitrateSource.Unknown)
{
    public double? AverageBitrateKbps => AverageBitrate is > 0 ? AverageBitrate.Value / 1000d : null;
    // 公称値と平均値の差によるVFR推定。全フレームの時刻を検査した判定ではない。
    public bool IsVariableFrameRate => AverageFrameRate > 0 && NominalFrameRate > 0 &&
        Math.Abs(AverageFrameRate - NominalFrameRate) > 0.01;

    public string Name => System.IO.Path.GetFileName(Path);
}
