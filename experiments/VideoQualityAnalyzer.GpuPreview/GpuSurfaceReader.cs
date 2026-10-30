using System.IO;
using System.Runtime.InteropServices;

namespace VideoQualityAnalyzer.GpuPreview;

// 実験用。MFのDXGIサーフェスをCPUに複製せずに保持する。
internal sealed class GpuSurfaceReader : IDisposable
{
    private const uint VideoStream = 0xFFFFFFFC;
    private const uint EndOfStream = 1;
    private static readonly Guid MajorTypeKey = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid SubtypeKey = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid VideoType = new("73646976-0000-0010-8000-00aa00389b71");
    private static readonly Guid Nv12Type = new("3231564e-0000-0010-8000-00aa00389b71");
    private static readonly Guid D3dManagerKey = new("ec822da2-e1e9-4b29-a0d8-563c719f5269");
    private static readonly Guid HardwareTransformsKey = new("a634a91c-822b-41b9-a494-4de4643612b0");
    private static readonly Guid DxgiBufferId = new("e7174cfa-1c9e-48b1-8866-626226bfc258");
    private static readonly Guid TextureId = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    private static readonly Guid MultithreadId = new("9b7e4e00-342c-4106-a19f-4f2704f689f0");
    private nint reader;
    private nint manager;
    private bool started;

    public GpuSurfaceReader(string path, nint device)
    {
        Check(MFStartup(0x20070, 0));
        started = true;
        nint attributes = 0;
        nint mediaType = 0;
        try
        {
            Check(Marshal.QueryInterface(device, in MultithreadId, out var multithread));
            try { Method<SetMultithreadProtectedDelegate>(multithread, 5)(multithread, 1); }
            finally { Marshal.Release(multithread); }
            Check(MFCreateDXGIDeviceManager(out var token, out manager));
            Check(Method<ResetDeviceDelegate>(manager, 7)(manager, device, token));
            Check(MFCreateAttributes(out attributes, 2));
            Check(Method<SetUnknownDelegate>(attributes, 27)(attributes, in D3dManagerKey, manager));
            Check(Method<SetUint32Delegate>(attributes, 21)(attributes, in HardwareTransformsKey, 1));
            Check(MFCreateSourceReaderFromURL(path, attributes, out reader));
            Check(MFCreateMediaType(out mediaType));
            var setGuid = Method<SetGuidDelegate>(mediaType, 24);
            Check(setGuid(mediaType, in MajorTypeKey, in VideoType));
            Check(setGuid(mediaType, in SubtypeKey, in Nv12Type));
            Check(Method<SetMediaTypeDelegate>(reader, 7)(reader, VideoStream, 0, mediaType));
        }
        catch { Dispose(); throw; }
        finally
        {
            if (mediaType != 0) Marshal.Release(mediaType);
            if (attributes != 0) Marshal.Release(attributes);
        }
    }

    public GpuFrame? ReadNext()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            nint sample = 0;
            Check(Method<ReadSampleDelegate>(reader, 9)(reader, VideoStream, 0,
                out _, out var flags, out var timestamp, out sample));
            if (sample != 0)
            {
                try
                {
                    Check(Method<GetBufferDelegate>(sample, 40)(sample, 0, out var buffer));
                    try
                    {
                        Check(Marshal.QueryInterface(buffer, in DxgiBufferId, out var dxgiBuffer));
                        try
                        {
                            Check(Method<GetResourceDelegate>(dxgiBuffer, 3)(dxgiBuffer, in TextureId, out var texture));
                            try
                            {
                                Check(Method<GetSubresourceDelegate>(dxgiBuffer, 4)(dxgiBuffer, out var subresource));
                                return new GpuFrame(TimeSpan.FromTicks(timestamp), texture, subresource, sample);
                            }
                            catch { Marshal.Release(texture); throw; }
                        }
                        finally { Marshal.Release(dxgiBuffer); }
                    }
                    finally { Marshal.Release(buffer); }
                }
                catch { Marshal.Release(sample); throw; }
            }
            if ((flags & EndOfStream) != 0) return null;
        }
        throw new InvalidDataException("映像フレームを取得できませんでした。");
    }

    public void Seek(TimeSpan position)
    {
        var format = Guid.Empty;
        var value = new PropVariant { Type = 20, Value = Math.Max(0, position.Ticks) };
        Check(Method<SetCurrentPositionDelegate>(reader, 8)(reader, in format, in value));
    }

    public void Dispose()
    {
        if (reader != 0) Marshal.Release(reader);
        if (manager != 0) Marshal.Release(manager);
        reader = manager = 0;
        if (started) MFShutdown();
        started = false;
    }

    private static T Method<T>(nint pointer, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(pointer), slot * IntPtr.Size));
    private static void Check(int result) { if (result < 0) Marshal.ThrowExceptionForHR(result); }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public long Value;
    }

    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFStartup(int version, int flags);
    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFShutdown();
    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFCreateMediaType(out nint value);
    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFCreateAttributes(out nint value, uint size);
    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFCreateDXGIDeviceManager(out uint token, out nint manager);
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MFCreateSourceReaderFromURL(string path, nint attributes, out nint reader);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ResetDeviceDelegate(nint self, nint device, uint token);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetUnknownDelegate(nint self, in Guid key, nint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetUint32Delegate(nint self, in Guid key, uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetGuidDelegate(nint self, in Guid key, in Guid value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetMediaTypeDelegate(nint self, uint stream, nint reserved, nint mediaType);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetCurrentPositionDelegate(nint self, in Guid format, in PropVariant position);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ReadSampleDelegate(nint self, uint stream, uint control,
        out uint actualStream, out uint flags, out long timestamp, out nint sample);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetBufferDelegate(nint self, uint index, out nint buffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetResourceDelegate(nint self, in Guid id, out nint resource);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetSubresourceDelegate(nint self, out uint index);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetMultithreadProtectedDelegate(nint self, int protect);
}

internal sealed class GpuFrame(TimeSpan position, nint texture, uint subresource, nint sample) : IDisposable
{
    private nint heldSample = sample;
    private nint heldTexture = texture;
    public TimeSpan Position { get; } = position;
    public nint Texture => heldTexture;
    public uint Subresource { get; } = subresource;
    public void ReleaseSample()
    {
        var value = Interlocked.Exchange(ref heldSample, 0);
        if (value != 0) Marshal.Release(value);
    }
    public void Dispose()
    {
        ReleaseSample();
        var value = Interlocked.Exchange(ref heldTexture, 0);
        if (value != 0) Marshal.Release(value);
    }
}
