using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace VideoQualityAnalyzer.GpuPreview;

// 単一動画のGPU提示実験。DXGIサーフェスをVideoProcessor経由で提示する。
internal sealed class GpuVideoHost(int videoWidth, int videoHeight, bool statistics = false) : HwndHost
{
    private static readonly Guid Texture2dId = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    private static readonly Guid VideoDeviceId = new("10ec4d5b-975a-4689-b9e4-d0aac30fe333");
    private static readonly Guid VideoContextId = new("61f21c45-3c0e-4a74-9cea-67100d9ad5e4");
    private nint childWindow;
    private nint swapChain;
    private nint device;
    private nint context;
    private nint backBuffer;
    private nint videoDevice;
    private nint videoContext;
    private nint enumerator;
    private nint processor;
    private nint outputView;
    private nint processedTexture;
    private nint copiedInputTexture;
    private uint frameNumber;
    private readonly Dictionary<uint, TimeSpan> sourcePtsByPresentId = new();
    private uint? lastPresentedId;
    private uint presentedIdIncrease;
    private TimeSpan? lastPresentedPts;
    private uint unmatchedPresentedIds;
    private uint repeatedSourcePts;
    private TimeSpan? previousCountedPts;
    private readonly Queue<(long Tick, uint Count)> presentationHistory = new();
    private string statisticsState = "統計準備中";

    public nint Device => device;
    public bool StatisticsEnabled => statistics;
    public string PresentationStatus
    {
        get
        {
            if (!statistics) return "提示統計なし";
            ObservePresentation();
            var fps = presentationHistory.Count > 1 ?
                (presentationHistory.Last().Count - presentationHistory.Peek().Count) /
                Stopwatch.GetElapsedTime(presentationHistory.Peek().Tick, presentationHistory.Last().Tick).TotalSeconds : 0;
            return $"DXGI提示観測 {fps:F1}fps / ID増分 {presentedIdIncrease} / 対応なし {unmatchedPresentedIds} / 同一PTS {repeatedSourcePts}" +
                (lastPresentedPts is { } pts ? $" / PTS {pts.TotalSeconds:F2}秒" : $" / {statisticsState}");
        }
    }

    public void ResetInput()
    {
        if (copiedInputTexture != 0) Marshal.Release(copiedInputTexture);
        copiedInputTexture = 0;
        frameNumber = 0;
        sourcePtsByPresentId.Clear();
        presentationHistory.Clear();
        lastPresentedId = null;
        presentedIdIncrease = unmatchedPresentedIds = repeatedSourcePts = 0;
        lastPresentedPts = previousCountedPts = null;
        statisticsState = "統計準備中";
    }

    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        const uint childVisible = 0x50000000;
        childWindow = CreateWindowExW(0, "STATIC", "GPU映像", childVisible,
            0, 0, 960, 540, parent.Handle, 0, 0, 0);
        if (childWindow == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

        var description = new SwapChainDescription
        {
            BufferDescription = new ModeDescription
            {
                Width = (uint)videoWidth,
                Height = (uint)videoHeight,
                RefreshRate = new Rational { Numerator = 0, Denominator = 1 },
                Format = 87 // DXGI_FORMAT_B8G8R8A8_UNORM
            },
            SampleDescription = new SampleDescription { Count = 1 },
            BufferUsage = 0x20, // DXGI_USAGE_RENDER_TARGET_OUTPUT
            BufferCount = 2,
            OutputWindow = childWindow,
            Windowed = 1,
            SwapEffect = statistics ? 3 : 0 // DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL
        };
        Check(D3D11CreateDeviceAndSwapChain(0, 1, 0, 0x820, 0, 0, 7,
            in description, out swapChain, out device, out _, out context));
        var getBuffer = Method<GetBufferDelegate>(swapChain, 9);
        Check(getBuffer(swapChain, 0, in Texture2dId, out backBuffer));
        Check(Marshal.QueryInterface(device, in VideoDeviceId, out videoDevice));
        Check(Marshal.QueryInterface(context, in VideoContextId, out videoContext));
        var content = new ProcessorContentDescription
        {
            InputFrameRate = new Rational { Numerator = 60, Denominator = 1 },
            InputWidth = (uint)videoWidth, InputHeight = (uint)videoHeight,
            OutputFrameRate = new Rational { Numerator = 60, Denominator = 1 },
            OutputWidth = (uint)videoWidth, OutputHeight = (uint)videoHeight
        };
        Check(Method<CreateEnumeratorDelegate>(videoDevice, 10)(videoDevice, in content, out enumerator));
        Check(Method<CreateProcessorDelegate>(videoDevice, 4)(videoDevice, enumerator, 0, out processor));
        var textureDescription = new TextureDescription
        {
            Width = (uint)videoWidth, Height = (uint)videoHeight,
            MipLevels = 1, ArraySize = 1, Format = 87,
            SampleDescription = new SampleDescription { Count = 1 },
            BindFlags = 0x20
        };
        Check(Method<CreateTextureDelegate>(device, 5)(device, in textureDescription, 0, out processedTexture));
        var outputDescription = new OutputViewDescription { ViewDimension = 1 };
        Check(Method<CreateOutputViewDelegate>(videoDevice, 9)(videoDevice, processedTexture, enumerator,
            in outputDescription, out outputView));
        return new HandleRef(this, childWindow);
    }

    public void Render(GpuFrame frame, bool synchronize = true, TimeSpan? sourcePts = null)
    {
        if (outputView == 0) return;
        if (copiedInputTexture == 0)
        {
            Method<GetTextureDescriptionDelegate>(frame.Texture, 10)(frame.Texture, out var description);
            description.BindFlags = 0;
            description.CpuAccessFlags = 0;
            description.MiscFlags = 0;
            Check(Method<CreateTextureDelegate>(device, 5)(device, in description, 0, out copiedInputTexture));
        }
        Method<CopyResourceDelegate>(context, 47)(context, copiedInputTexture, frame.Texture);
        var inputDescription = new InputViewDescription { ViewDimension = 1,
            ArraySlice = frame.Subresource };
        Check(Method<CreateInputViewDelegate>(videoDevice, 8)(videoDevice, copiedInputTexture, enumerator,
            in inputDescription, out var inputView));
        try
        {
            var stream = new ProcessorStream { Enable = 1, InputFrameOrField = frameNumber,
                InputSurface = inputView };
            Check(Method<VideoProcessorBltDelegate>(videoContext, 53)(videoContext, processor,
                outputView, frameNumber++, 1, in stream));
            Method<CopyResourceDelegate>(context, 47)(context, backBuffer, processedTexture);
            frame.ReleaseSample();
            Check(Method<PresentDelegate>(swapChain, 8)(swapChain, synchronize ? 1u : 0u, 0));
            if (statistics)
            {
                if (Method<GetLastPresentCountDelegate>(swapChain, 17)(swapChain, out var presentId) >= 0)
                    sourcePtsByPresentId[presentId] = sourcePts ?? frame.Position;
                ObservePresentation();
            }
        }
        finally { Marshal.Release(inputView); }
    }

    private void ObservePresentation()
    {
        if (!statistics || swapChain == 0) return;
        var now = Stopwatch.GetTimestamp();
        while (presentationHistory.Count > 1 &&
            Stopwatch.GetElapsedTime(presentationHistory.Peek().Tick, now) > TimeSpan.FromSeconds(3))
            presentationHistory.Dequeue();
        var result = Method<GetFrameStatisticsDelegate>(swapChain, 16)(swapChain, out var sample);
        if (result < 0)
        {
            statisticsState = $"取得待ち 0x{result:X8}";
            return;
        }
        statisticsState = $"提示ID {sample.PresentCount}";
        if (lastPresentedId is { } previous && sample.PresentCount > previous)
        {
            for (var id = previous + 1; id <= sample.PresentCount; id++)
            {
                presentedIdIncrease++;
                if (!sourcePtsByPresentId.TryGetValue(id, out var presentedPts))
                    unmatchedPresentedIds++;
                else
                {
                    if (previousCountedPts == presentedPts) repeatedSourcePts++;
                    previousCountedPts = presentedPts;
                }
            }
        }
        if (lastPresentedId != sample.PresentCount)
        {
            presentationHistory.Enqueue((now, sample.PresentCount));
            while (presentationHistory.Count > 2 &&
                Stopwatch.GetElapsedTime(presentationHistory.Peek().Tick, now) > TimeSpan.FromSeconds(3))
                presentationHistory.Dequeue();
        }
        lastPresentedId = sample.PresentCount;
        if (sourcePtsByPresentId.TryGetValue(sample.PresentCount, out var pts))
            lastPresentedPts = pts;
        foreach (var id in sourcePtsByPresentId.Keys.Where(id => id + 120 < sample.PresentCount).ToArray())
            sourcePtsByPresentId.Remove(id);
    }

    public void Render(byte[] pixels)
    {
        if (backBuffer == 0) return;
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            var update = Method<UpdateSubresourceDelegate>(context, 48);
            update(context, backBuffer, 0, 0, pin.AddrOfPinnedObject(), (uint)(videoWidth * 4), 0);
            var present = Method<PresentDelegate>(swapChain, 8);
            Check(present(swapChain, 1, 0));
        }
        finally { pin.Free(); }
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        ReleaseGpu();
        if (childWindow != 0) DestroyWindow(childWindow);
        childWindow = 0;
    }

    private void ReleaseGpu()
    {
        if (outputView != 0) Marshal.Release(outputView);
        if (processedTexture != 0) Marshal.Release(processedTexture);
        if (copiedInputTexture != 0) Marshal.Release(copiedInputTexture);
        if (processor != 0) Marshal.Release(processor);
        if (enumerator != 0) Marshal.Release(enumerator);
        if (videoContext != 0) Marshal.Release(videoContext);
        if (videoDevice != 0) Marshal.Release(videoDevice);
        if (backBuffer != 0) Marshal.Release(backBuffer);
        if (swapChain != 0) Marshal.Release(swapChain);
        if (context != 0) Marshal.Release(context);
        if (device != 0) Marshal.Release(device);
        backBuffer = swapChain = context = device = 0;
        outputView = processor = enumerator = videoContext = videoDevice = 0;
        processedTexture = 0;
        copiedInputTexture = 0;
    }

    private static T Method<T>(nint instance, int index) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), index * IntPtr.Size));

    private static void Check(int result)
    {
        if (result < 0) Marshal.ThrowExceptionForHR(result);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rational { public uint Numerator, Denominator; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ModeDescription
    {
        public uint Width, Height;
        public Rational RefreshRate;
        public int Format, ScanlineOrdering, Scaling;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct SampleDescription { public uint Count, Quality; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TextureDescription
    {
        public uint Width, Height, MipLevels, ArraySize;
        public uint Format;
        public SampleDescription SampleDescription;
        public uint Usage, BindFlags, CpuAccessFlags, MiscFlags;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessorContentDescription
    {
        public uint InputFrameFormat;
        public Rational InputFrameRate;
        public uint InputWidth, InputHeight;
        public Rational OutputFrameRate;
        public uint OutputWidth, OutputHeight, Usage;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct InputViewDescription
    {
        public uint FourCC, ViewDimension, MipSlice, ArraySlice;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct OutputViewDescription
    {
        public uint ViewDimension, MipSlice;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessorStream
    {
        public int Enable;
        public uint OutputIndex, InputFrameOrField, PastFrames, FutureFrames;
        public nint PastSurfaces, InputSurface, FutureSurfaces;
        public nint PastSurfacesRight, InputSurfaceRight, FutureSurfacesRight;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct SwapChainDescription
    {
        public ModeDescription BufferDescription;
        public SampleDescription SampleDescription;
        public uint BufferUsage, BufferCount;
        public nint OutputWindow;
        public int Windowed, SwapEffect;
        public uint Flags;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FrameStatistics
    {
        public uint PresentCount, PresentRefreshCount, SyncRefreshCount;
        public long SyncQpcTime, SyncGpuTime;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern nint CreateWindowExW(uint styleEx, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDeviceAndSwapChain(nint adapter, uint driverType, nint software,
        uint flags, nint featureLevels, uint featureLevelsCount, uint sdkVersion,
        in SwapChainDescription description, out nint swapChain, out nint device,
        out uint featureLevel, out nint context);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetBufferDelegate(nint self, uint index, in Guid interfaceId, out nint buffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void UpdateSubresourceDelegate(nint self, nint destination, uint subresource,
        nint box, nint source, uint rowPitch, uint depthPitch);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int PresentDelegate(nint self, uint interval, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetFrameStatisticsDelegate(nint self, out FrameStatistics statistics);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetLastPresentCountDelegate(nint self, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateEnumeratorDelegate(nint self, in ProcessorContentDescription description, out nint enumerator);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateProcessorDelegate(nint self, nint enumerator, uint rateConversionIndex, out nint processor);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateInputViewDelegate(nint self, nint resource, nint enumerator,
        in InputViewDescription description, out nint inputView);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateOutputViewDelegate(nint self, nint resource, nint enumerator,
        in OutputViewDescription description, out nint outputView);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int VideoProcessorBltDelegate(nint self, nint processor, nint outputView,
        uint outputFrame, uint streamCount, in ProcessorStream stream);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateTextureDelegate(nint self, in TextureDescription description, nint initialData, out nint texture);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void CopyResourceDelegate(nint self, nint destination, nint source);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GetTextureDescriptionDelegate(nint self, out TextureDescription description);
}
