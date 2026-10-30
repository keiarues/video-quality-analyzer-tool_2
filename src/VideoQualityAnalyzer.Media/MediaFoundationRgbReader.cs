using System.Runtime.InteropServices;

namespace VideoQualityAnalyzer.Media;

// AV1の暫定プレビュー用。GPUへの直接提示は段階Bの次工程で実装する。
public sealed class MediaFoundationRgbReader : IDisposable
{
    private const uint FirstVideoStream = 0xFFFFFFFC;
    private const uint EndOfStream = 1;
    private static readonly Guid MajorTypeKey = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid SubtypeKey = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid VideoMajorType = new("73646976-0000-0010-8000-00aa00389b71");
    private static readonly Guid Rgb32Subtype = new("00000016-0000-0010-8000-00aa00389b71");
    private static readonly Guid VideoProcessingKey = new("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");
    private nint reader;
    private bool disposed;

    public MediaFoundationRgbReader(string path)
    {
        Check(MFStartup(0x20070, 0));
        nint attributes = 0;
        nint mediaType = 0;
        try
        {
            Check(MFCreateAttributes(out attributes, 1));
            var setUint32 = Method<SetUint32Delegate>(attributes, 21);
            Check(setUint32(attributes, in VideoProcessingKey, 1));
            Check(MFCreateSourceReaderFromURL(path, attributes, out reader));
            Check(MFCreateMediaType(out mediaType));
            var setGuid = Method<SetGuidDelegate>(mediaType, 24);
            Check(setGuid(mediaType, in MajorTypeKey, in VideoMajorType));
            Check(setGuid(mediaType, in SubtypeKey, in Rgb32Subtype));
            var setMediaType = Method<SetCurrentMediaTypeDelegate>(reader, 7);
            Check(setMediaType(reader, FirstVideoStream, 0, mediaType));
        }
        catch
        {
            if (reader != 0) Marshal.Release(reader);
            reader = 0;
            MFShutdown();
            throw;
        }
        finally
        {
            if (mediaType != 0) Marshal.Release(mediaType);
            if (attributes != 0) Marshal.Release(attributes);
        }
    }

    public void Seek(TimeSpan position)
    {
        var format = Guid.Empty;
        var value = new PropVariant { Type = 20, Value = Math.Max(0, position.Ticks) };
        var setPosition = Method<SetCurrentPositionDelegate>(reader, 8);
        Check(setPosition(reader, in format, in value));
    }

    public RgbFrame? ReadNext(int expectedBytes)
    {
        var readSample = Method<ReadSampleDelegate>(reader, 9);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            nint sample = 0;
            try
            {
                Check(readSample(reader, FirstVideoStream, 0, out _, out var flags, out var timestamp, out sample));
                if (sample != 0)
                {
                    var contiguous = Method<ConvertToContiguousBufferDelegate>(sample, 41);
                    Check(contiguous(sample, out var buffer));
                    try
                    {
                        var lockBuffer = Method<LockDelegate>(buffer, 3);
                        Check(lockBuffer(buffer, out var data, out _, out var length));
                        try
                        {
                            if (length != expectedBytes)
                                throw new InvalidDataException($"RGBフレーム長が不正です: {length} / {expectedBytes}");
                            var pixels = new byte[expectedBytes];
                            Marshal.Copy(data, pixels, 0, expectedBytes);
                            return new RgbFrame(TimeSpan.FromTicks(timestamp), pixels);
                        }
                        finally { Check(Method<UnlockDelegate>(buffer, 4)(buffer)); }
                    }
                    finally { Marshal.Release(buffer); }
                }
                if ((flags & EndOfStream) != 0)
                    return null;
            }
            finally
            {
                if (sample != 0) Marshal.Release(sample);
            }
        }
        throw new InvalidDataException("映像フレームの読み取りが進みませんでした。");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (reader != 0) Marshal.Release(reader);
        reader = 0;
        MFShutdown();
    }

    private static T Method<T>(nint instance, int index) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), index * IntPtr.Size));

    private static void Check(int result)
    {
        if (result < 0) Marshal.ThrowExceptionForHR(result);
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public long Value;
    }

    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFStartup(int version, int flags);
    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFShutdown();
    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFCreateAttributes(out nint attributes, uint count);
    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFCreateMediaType(out nint mediaType);
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MFCreateSourceReaderFromURL(string path, nint attributes, out nint reader);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetUint32Delegate(nint self, in Guid key, uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetGuidDelegate(nint self, in Guid key, in Guid value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetCurrentMediaTypeDelegate(nint self, uint stream, nint reserved, nint type);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetCurrentPositionDelegate(nint self, in Guid format, in PropVariant position);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ReadSampleDelegate(nint self, uint stream, uint control,
        out uint actualStream, out uint flags, out long timestamp, out nint sample);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ConvertToContiguousBufferDelegate(nint self, out nint buffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int LockDelegate(nint self, out nint data, out uint maxLength, out uint length);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int UnlockDelegate(nint self);
}

public sealed record RgbFrame(TimeSpan Position, byte[] Pixels);
