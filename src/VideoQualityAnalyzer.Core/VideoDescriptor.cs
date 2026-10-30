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
    double PixelAspectRatio)
{
    public string Name => System.IO.Path.GetFileName(Path);
}
