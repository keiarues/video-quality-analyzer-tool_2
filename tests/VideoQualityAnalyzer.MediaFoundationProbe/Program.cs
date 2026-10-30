using System.Runtime.InteropServices;
using VideoQualityAnalyzer.Media;

var useGpuManager = args.FirstOrDefault() == "--gpu";
var useRgb = args.FirstOrDefault() == "--rgb";
var useReader = args.FirstOrDefault() == "--reader";
var inspectSurface = args.FirstOrDefault() == "--surface";
var paths = useGpuManager || useRgb || useReader || inspectSurface ? args.Skip(1).ToArray() : args;
if (paths.Length == 0)
{
    Console.Error.WriteLine("動画ファイルのパスを指定してください。");
    return 2;
}

var failures = 0;
foreach (var path in paths)
{
    if (!File.Exists(path))
    {
        Console.WriteLine($"{Path.GetFileName(path)}: ファイルがありません");
        failures++;
        continue;
    }

    try
    {
        if (useReader)
        {
            using var reader = new MediaFoundationRgbReader(path);
            reader.Seek(TimeSpan.FromSeconds(5));
            var frame = reader.ReadNext(1440 * 1080 * 4) ?? throw new InvalidDataException("フレームがありません。");
            Console.WriteLine($"{Path.GetFileName(path)}: RGB取得成功 / PTS {frame.Position.TotalMilliseconds:F1} ms / 長さ {frame.Pixels.Length} バイト");
            continue;
        }
        var result = MediaFoundationProbe.ReadFirstDecodedFrame(path, useGpuManager || inspectSurface, useRgb, inspectSurface);
        Console.WriteLine($"{Path.GetFileName(path)}: デコード成功 / PTS {result.Timestamp / 10_000d:F1} ms / ストリーム {result.StreamIndex} / 長さ {result.BufferLength} バイト / GPU管理 {useGpuManager || inspectSurface} / DXGIサーフェス {result.HasDxgiSurface} / 実HW利用 未確認");
    }
    catch (Exception exception)
    {
        Console.WriteLine($"{Path.GetFileName(path)}: デコード失敗 / {exception.Message} / HRESULT 0x{exception.HResult:X8}");
        failures++;
    }
}

return failures == 0 ? 0 : 1;

internal static class MediaFoundationProbe
{
    private const int MfVersion = 0x20070;
    private const uint FirstVideoStream = 0xFFFFFFFC;
    private const uint EndOfStream = 0x00000001;
    private static readonly Guid MajorTypeKey = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid SubtypeKey = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid VideoMajorType = new("73646976-0000-0010-8000-00aa00389b71");
    private static readonly Guid Nv12Subtype = new("3231564e-0000-0010-8000-00aa00389b71");
    private static readonly Guid Rgb32Subtype = new("00000016-0000-0010-8000-00aa00389b71");
    private static readonly Guid VideoProcessingKey = new("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");
    private static readonly Guid D3dManagerKey = new("ec822da2-e1e9-4b29-a0d8-563c719f5269");
    private static readonly Guid EnableHardwareTransformsKey = new("a634a91c-822b-41b9-a494-4de4643612b0");
    private static readonly Guid DxgiBufferId = new("e7174cfa-1c9e-48b1-8866-626226bfc258");

    public static (long Timestamp, uint StreamIndex, uint BufferLength, bool HasDxgiSurface) ReadFirstDecodedFrame(string path, bool useGpuManager, bool useRgb, bool inspectSurface)
    {
        Marshal.ThrowExceptionForHR(MFStartup(MfVersion, 0));
        nint reader = 0;
        nint mediaType = 0;
        nint attributes = 0;
        nint device = 0;
        nint context = 0;
        nint manager = 0;
        try
        {
            if (useGpuManager)
            {
                const uint videoSupport = 0x800;
                const uint bgraSupport = 0x20;
                Check("D3D11CreateDevice", D3D11CreateDevice(0, 1, 0, videoSupport | bgraSupport,
                    0, 0, 7, out device, out _, out context));
                Check("MFCreateDXGIDeviceManager", MFCreateDXGIDeviceManager(out var resetToken, out manager));
                var resetDevice = GetMethod<ResetDeviceDelegate>(manager, 7);
                Check("ResetDevice", resetDevice(manager, device, resetToken));
                Check("MFCreateAttributes", MFCreateAttributes(out attributes, 2));
                var setUnknown = GetMethod<SetUnknownDelegate>(attributes, 27);
                Check("SetUnknown(D3D_MANAGER)", setUnknown(attributes, in D3dManagerKey, manager));
                var setUint32 = GetMethod<SetUint32Delegate>(attributes, 21);
                Check("SetUINT32(HARDWARE_TRANSFORMS)", setUint32(attributes, in EnableHardwareTransformsKey, 1));
            }
            if (useRgb)
            {
                Check("MFCreateAttributes", MFCreateAttributes(out attributes, 1));
                var setUint32 = GetMethod<SetUint32Delegate>(attributes, 21);
                Check("SetUINT32(VIDEO_PROCESSING)", setUint32(attributes, in VideoProcessingKey, 1));
            }
            Check("MFCreateSourceReaderFromURL", MFCreateSourceReaderFromURL(path, attributes, out reader));
            Marshal.ThrowExceptionForHR(MFCreateMediaType(out mediaType));
            var setGuid = GetMethod<SetGuidDelegate>(mediaType, 24);
            Marshal.ThrowExceptionForHR(setGuid(mediaType, in MajorTypeKey, in VideoMajorType));
            var subtype = useRgb ? Rgb32Subtype : Nv12Subtype;
            Marshal.ThrowExceptionForHR(setGuid(mediaType, in SubtypeKey, in subtype));
            var setMediaType = GetMethod<SetCurrentMediaTypeDelegate>(reader, 7);
            Check("SetCurrentMediaType", setMediaType(reader, FirstVideoStream, 0, mediaType));
            var readSample = GetMethod<ReadSampleDelegate>(reader, 9);
            for (var index = 0; index < 20; index++)
            {
                nint sample = 0;
                try
                {
                    Check("ReadSample", readSample(reader, FirstVideoStream, 0,
                        out var streamIndex, out var flags, out var timestamp, out sample));
                    if (sample != 0)
                    {
                        var hasDxgiSurface = false;
                        if (inspectSurface)
                        {
                            var getBuffer = GetMethod<GetBufferByIndexDelegate>(sample, 40);
                            Check("GetBufferByIndex", getBuffer(sample, 0, out var firstBuffer));
                            try
                            {
                                hasDxgiSurface = Marshal.QueryInterface(firstBuffer, in DxgiBufferId, out var dxgiBuffer) >= 0;
                                if (hasDxgiSurface) Marshal.Release(dxgiBuffer);
                            }
                            finally { Marshal.Release(firstBuffer); }
                        }
                        var contiguous = GetMethod<ConvertToContiguousBufferDelegate>(sample, 41);
                        Check("ConvertToContiguousBuffer", contiguous(sample, out var buffer));
                        try
                        {
                            var getLength = GetMethod<GetCurrentLengthDelegate>(buffer, 5);
                            Check("GetCurrentLength", getLength(buffer, out var length));
                            return (timestamp, streamIndex, length, hasDxgiSurface);
                        }
                        finally { Marshal.Release(buffer); }
                    }
                    if ((flags & EndOfStream) != 0)
                        throw new InvalidDataException("映像フレームがありません。");
                }
                finally
                {
                    if (sample != 0)
                        Marshal.Release(sample);
                }
            }
            throw new InvalidDataException("20回の読み取りで映像フレームを取得できませんでした。");
        }
        finally
        {
            if (mediaType != 0)
                Marshal.Release(mediaType);
            if (reader != 0)
                Marshal.Release(reader);
            if (attributes != 0)
                Marshal.Release(attributes);
            if (manager != 0)
                Marshal.Release(manager);
            if (context != 0)
                Marshal.Release(context);
            if (device != 0)
                Marshal.Release(device);
            MFShutdown();
        }
    }

    private static T GetMethod<T>(nint instance, int slot) where T : Delegate
    {
        var table = Marshal.ReadIntPtr(instance);
        return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(table, slot * IntPtr.Size));
    }

    private static void Check(string operation, int hresult)
    {
        if (hresult < 0)
            throw new COMException(operation, hresult);
    }

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFStartup(int version, int flags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMediaType(out nint mediaType);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateAttributes(out nint attributes, uint initialSize);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateDXGIDeviceManager(out uint resetToken, out nint manager);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(nint adapter, uint driverType, nint software, uint flags,
        nint featureLevels, uint featureLevelsCount, uint sdkVersion, out nint device,
        out uint featureLevel, out nint context);

    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MFCreateSourceReaderFromURL(string url, nint attributes, out nint reader);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetGuidDelegate(nint instance, in Guid key, in Guid value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetUnknownDelegate(nint instance, in Guid key, nint value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetUint32Delegate(nint instance, in Guid key, uint value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ResetDeviceDelegate(nint instance, nint device, uint resetToken);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetCurrentMediaTypeDelegate(nint instance, uint streamIndex, nint reserved, nint mediaType);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ReadSampleDelegate(nint instance, uint streamIndex, uint controlFlags,
        out uint actualStreamIndex, out uint streamFlags, out long timestamp, out nint sample);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ConvertToContiguousBufferDelegate(nint instance, out nint buffer);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetBufferByIndexDelegate(nint instance, uint index, out nint buffer);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetCurrentLengthDelegate(nint instance, out uint length);
}
