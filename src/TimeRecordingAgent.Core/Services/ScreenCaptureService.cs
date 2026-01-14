using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TimeRecordingAgent.Core.Services;

/// <summary>
/// Service for capturing screenshots of windows for AI vision analysis.
/// </summary>
public sealed class ScreenCaptureService
{
    private readonly ILogger<ScreenCaptureService> _logger;

    // Maximum dimensions to keep image size reasonable for AI
    private const int MaxWidth = 1280;
    private const int MaxHeight = 720;

    public ScreenCaptureService(ILogger<ScreenCaptureService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Captures a screenshot of the foreground window and returns it as a base64-encoded JPEG.
    /// </summary>
    /// <returns>Base64-encoded JPEG image, or null if capture fails.</returns>
    public string? CaptureActiveWindowAsBase64()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                _logger.LogDebug("No foreground window to capture");
                return null;
            }

            return CaptureWindowAsBase64(hwnd);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to capture active window screenshot");
            return null;
        }
    }

    /// <summary>
    /// Captures a screenshot of a specific window and returns it as a base64-encoded JPEG.
    /// </summary>
    public string? CaptureWindowAsBase64(IntPtr hwnd)
    {
        try
        {
            if (!GetWindowRect(hwnd, out var rect))
            {
                _logger.LogDebug("Failed to get window rect for handle {Handle}", hwnd);
                return null;
            }

            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;

            if (width <= 0 || height <= 0)
            {
                _logger.LogDebug("Window has invalid dimensions: {Width}x{Height}", width, height);
                return null;
            }

            // Capture the window using PrintWindow for better results with layered windows
            using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using var graphics = Graphics.FromImage(bitmap);
            
            var hdcBitmap = graphics.GetHdc();
            try
            {
                // Try PrintWindow first (better for some apps)
                if (!PrintWindow(hwnd, hdcBitmap, PW_RENDERFULLCONTENT))
                {
                    // Fall back to BitBlt from screen
                    graphics.ReleaseHdc(hdcBitmap);
                    graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height));
                }
                else
                {
                    graphics.ReleaseHdc(hdcBitmap);
                }
            }
            catch
            {
                try { graphics.ReleaseHdc(hdcBitmap); } catch { }
                // Fall back to BitBlt from screen
                graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height));
            }

            // Resize if too large
            var resized = ResizeIfNeeded(bitmap, MaxWidth, MaxHeight);
            
            // Convert to base64 JPEG
            using var ms = new MemoryStream();
            var encoder = GetEncoder(ImageFormat.Jpeg);
            var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 75L); // 75% quality for smaller size

            if (encoder != null)
            {
                resized.Save(ms, encoder, encoderParams);
            }
            else
            {
                resized.Save(ms, ImageFormat.Jpeg);
            }

            if (resized != bitmap)
            {
                resized.Dispose();
            }

            var base64 = Convert.ToBase64String(ms.ToArray());
            _logger.LogDebug("Captured window screenshot: {Width}x{Height}, {Size} bytes base64", 
                width, height, base64.Length);
            
            return base64;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to capture window screenshot");
            return null;
        }
    }

    private static Bitmap ResizeIfNeeded(Bitmap original, int maxWidth, int maxHeight)
    {
        if (original.Width <= maxWidth && original.Height <= maxHeight)
        {
            return original;
        }

        var ratioX = (double)maxWidth / original.Width;
        var ratioY = (double)maxHeight / original.Height;
        var ratio = Math.Min(ratioX, ratioY);

        var newWidth = (int)(original.Width * ratio);
        var newHeight = (int)(original.Height * ratio);

        var resized = new Bitmap(newWidth, newHeight, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(resized);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(original, 0, 0, newWidth, newHeight);

        return resized;
    }

    private static ImageCodecInfo? GetEncoder(ImageFormat format)
    {
        var codecs = ImageCodecInfo.GetImageEncoders();
        return codecs.FirstOrDefault(c => c.FormatID == format.Guid);
    }

    #region Native Methods

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    #endregion
}
