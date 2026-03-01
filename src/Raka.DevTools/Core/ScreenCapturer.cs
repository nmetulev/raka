using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using WinRT;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Raka.DevTools.Core;

/// <summary>
/// Captures window screenshots using Windows.Graphics.Capture API.
/// Produces pixel-perfect screenshots that include system backdrops (Mica, Acrylic).
/// </summary>
internal static class ScreenCapturer
{
    /// <summary>
    /// Captures the entire window as a PNG, including Mica/Acrylic backdrops.
    /// Window must be visible (not minimized).
    /// </summary>
    public static async Task<(byte[] PngBytes, int Width, int Height)> CaptureWindowAsync(Window window)
    {
        var hwnd = global::WinRT.Interop.WindowNative.GetWindowHandle(window);
        var item = CreateCaptureItemForWindow(hwnd);
        var device = CreateDirect3DDevice();

        try
        {
            var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, item.Size);

            var session = framePool.CreateCaptureSession(item);
            try { session.GetType().GetProperty("IsBorderRequired")?.SetValue(session, false); } catch { /* older Windows versions */ }

            var tcs = new TaskCompletionSource<Direct3D11CaptureFrame>();
            framePool.FrameArrived += (sender, _) =>
            {
                var frame = sender.TryGetNextFrame();
                if (frame != null) tcs.TrySetResult(frame);
            };

            session.StartCapture();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            cts.Token.Register(() => tcs.TrySetCanceled());

            Direct3D11CaptureFrame captured;
            try
            {
                captured = await tcs.Task;
            }
            finally
            {
                session.Dispose();
            }

            SoftwareBitmap softwareBitmap;
            using (captured)
            {
                softwareBitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(
                    captured.Surface, BitmapAlphaMode.Premultiplied);
            }

            framePool.Dispose();
            return await EncodeSoftwareBitmapAsync(softwareBitmap);
        }
        finally
        {
            device.Dispose();
        }
    }

    /// <summary>
    /// Captures a specific element by taking a full window capture and cropping.
    /// Element bounds are calculated relative to the window content area.
    /// </summary>
    public static async Task<(byte[] PngBytes, int Width, int Height)> CaptureElementFromWindowAsync(
        Window window, UIElement element)
    {
        var (windowPng, windowW, windowH) = await CaptureWindowAsync(window);

        // Get element bounds relative to window content root
        var transform = element.TransformToVisual(window.Content);
        var bounds = transform.TransformBounds(
            new Windows.Foundation.Rect(0, 0, element.ActualSize.X, element.ActualSize.Y));

        var scale = element.XamlRoot.RasterizationScale;

        // Title bar offset: window pixel height minus client pixel height
        var appWindow = window.AppWindow;
        var titleBarHeight = appWindow.Size.Height - appWindow.ClientSize.Height;

        int cropX = Math.Max(0, (int)(bounds.X * scale));
        int cropY = Math.Max(0, (int)(bounds.Y * scale + titleBarHeight));
        int cropW = (int)(bounds.Width * scale);
        int cropH = (int)(bounds.Height * scale);

        cropW = Math.Min(cropW, windowW - cropX);
        cropH = Math.Min(cropH, windowH - cropY);

        if (cropW <= 0 || cropH <= 0)
            throw new InvalidOperationException("Element is outside the visible window area");

        return await CropPngAsync(windowPng, cropX, cropY, cropW, cropH);
    }

    /// <summary>
    /// Composites BGRA8 premultiplied pixel data over a solid background color.
    /// Makes transparent areas visible when using RenderTargetBitmap.
    /// </summary>
    public static void ApplyBackground(byte[] pixels, byte bgR, byte bgG, byte bgB)
    {
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte a = pixels[i + 3];
            if (a == 255) continue; // Fully opaque, no blending needed
            if (a == 0)
            {
                // Fully transparent — just use background
                pixels[i] = bgB;
                pixels[i + 1] = bgG;
                pixels[i + 2] = bgR;
                pixels[i + 3] = 255;
                continue;
            }

            // Premultiplied alpha compositing over solid background
            float invAlpha = 1.0f - (a / 255.0f);
            pixels[i] = (byte)Math.Min(255, pixels[i] + bgB * invAlpha);
            pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] + bgG * invAlpha);
            pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] + bgR * invAlpha);
            pixels[i + 3] = 255;
        }
    }

    /// <summary>
    /// Parses a hex color string (#RGB, #RRGGBB, or #AARRGGBB) into R, G, B components.
    /// </summary>
    public static (byte R, byte G, byte B) ParseHexColor(string hex)
    {
        hex = hex.TrimStart('#');
        return hex.Length switch
        {
            3 => (
                (byte)(Convert.ToByte(hex[0..1], 16) * 17),
                (byte)(Convert.ToByte(hex[1..2], 16) * 17),
                (byte)(Convert.ToByte(hex[2..3], 16) * 17)),
            6 => (
                Convert.ToByte(hex[0..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16)),
            8 => (
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16),
                Convert.ToByte(hex[6..8], 16)),
            _ => throw new ArgumentException($"Invalid hex color: #{hex}")
        };
    }

    #region PNG Encoding Helpers

    private static async Task<(byte[] PngBytes, int Width, int Height)> EncodeSoftwareBitmapAsync(
        SoftwareBitmap bitmap)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();

        stream.Seek(0);
        var bytes = new byte[stream.Size];
        await stream.ReadAsync(bytes.AsBuffer(), (uint)stream.Size, InputStreamOptions.None);

        return (bytes, bitmap.PixelWidth, bitmap.PixelHeight);
    }

    public static async Task<byte[]> EncodePngFromPixelsAsync(byte[] pixels, int width, int height)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)width, (uint)height, 96, 96, pixels);
        await encoder.FlushAsync();

        stream.Seek(0);
        var bytes = new byte[stream.Size];
        await stream.ReadAsync(bytes.AsBuffer(), (uint)stream.Size, InputStreamOptions.None);
        return bytes;
    }

    private static async Task<(byte[] PngBytes, int Width, int Height)> CropPngAsync(
        byte[] pngBytes, int x, int y, int width, int height)
    {
        using var inputStream = new InMemoryRandomAccessStream();
        await inputStream.WriteAsync(pngBytes.AsBuffer());
        inputStream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(inputStream);

        width = Math.Min(width, (int)decoder.PixelWidth - x);
        height = Math.Min(height, (int)decoder.PixelHeight - y);

        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("Crop area is outside the image bounds");

        var bitmapTransform = new BitmapTransform
        {
            Bounds = new BitmapBounds
            {
                X = (uint)x,
                Y = (uint)y,
                Width = (uint)width,
                Height = (uint)height
            }
        };

        var pixelData = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            bitmapTransform, ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);

        using var outputStream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outputStream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)width, (uint)height, 96, 96, pixelData.DetachPixelData());
        await encoder.FlushAsync();

        outputStream.Seek(0);
        var bytes = new byte[outputStream.Size];
        await outputStream.ReadAsync(bytes.AsBuffer(), (uint)outputStream.Size, InputStreamOptions.None);

        return (bytes, width, height);
    }

    #endregion

    #region Windows.Graphics.Capture Interop

    private static GraphicsCaptureItem CreateCaptureItemForWindow(IntPtr hwnd)
    {
        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        int hr = WindowsCreateString(className, (uint)className.Length, out var hstring);
        if (hr != 0)
            throw new COMException("WindowsCreateString failed", hr);

        try
        {
            // Get the activation factory and QI for IGraphicsCaptureItemInterop
            Guid interopGuid = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
            hr = RoGetActivationFactory(hstring, in interopGuid, out var interopPtr);
            if (hr != 0)
                throw new COMException("Failed to get IGraphicsCaptureItemInterop", hr);

            try
            {
                // Call CreateForWindow via raw vtable (slot 3, after QI/AddRef/Release)
                IntPtr vtable = Marshal.ReadIntPtr(interopPtr);
                IntPtr fnPtr = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
                var createForWindow = Marshal.GetDelegateForFunctionPointer<CreateForWindowFn>(fnPtr);

                Guid captureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760"); // IGraphicsCaptureItem
                hr = createForWindow(interopPtr, hwnd, ref captureItemIid, out var raw);
                if (hr != 0)
                    throw new COMException("CreateForWindow failed", hr);

                var item = MarshalInterface<GraphicsCaptureItem>.FromAbi(raw);
                Marshal.Release(raw);
                return item;
            }
            finally
            {
                Marshal.Release(interopPtr);
            }
        }
        finally
        {
            WindowsDeleteString(hstring);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateForWindowFn(
        IntPtr thisPtr, IntPtr window, ref Guid riid, out IntPtr result);

    #endregion

    #region Direct3D Device Creation

    private static IDirect3DDevice CreateDirect3DDevice()
    {
        int hr = D3D11CreateDevice(
            IntPtr.Zero,    // adapter
            1,              // D3D_DRIVER_TYPE_HARDWARE
            IntPtr.Zero,    // software
            0x20,           // D3D11_CREATE_DEVICE_BGRA_SUPPORT
            IntPtr.Zero,    // feature levels
            0,              // num feature levels
            7,              // D3D11_SDK_VERSION
            out var d3dDevice,
            out _,
            out var d3dContext);

        if (hr != 0)
            throw new COMException("Failed to create D3D11 device", hr);

        try
        {
            Guid dxgiGuid = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c"); // IDXGIDevice
            Marshal.QueryInterface(d3dDevice, in dxgiGuid, out var dxgiDevice);

            try
            {
                hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out var inspectable);
                if (hr != 0)
                    throw new COMException("CreateDirect3D11DeviceFromDXGIDevice failed", hr);

                try
                {
                    return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
                }
                finally
                {
                    Marshal.Release(inspectable);
                }
            }
            finally
            {
                Marshal.Release(dxgiDevice);
            }
        }
        finally
        {
            Marshal.Release(d3dContext);
            Marshal.Release(d3dDevice);
        }
    }

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(
        IntPtr pAdapter, int driverType, IntPtr software, uint flags,
        IntPtr pFeatureLevels, uint featureLevels, uint sdkVersion,
        out IntPtr ppDevice, out int pFeatureLevel, out IntPtr ppImmediateContext);

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(
        IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int RoGetActivationFactory(
        IntPtr activatableClassId,
        in Guid iid,
        out IntPtr factory);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
        uint length,
        out IntPtr hstring);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    #endregion
}
