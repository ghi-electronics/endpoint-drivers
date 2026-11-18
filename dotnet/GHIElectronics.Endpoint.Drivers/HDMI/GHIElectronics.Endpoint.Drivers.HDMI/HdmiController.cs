// HDMI framebuffer display: writes to /dev/fb0 and pixels appear on the
// attached HDMI monitor. Works on any board whose GPU firmware (or DRM/vc4
// driver) exposes an HDMI framebuffer at /dev/fb0 — Raspberry Pi today, any
// future HDMI-capable board tomorrow. No pins, no bus, no board-specific
// knowledge.
//
// Subclass of GHIElectronics.Endpoint.Devices.Display.DisplayController so
// user code treats HDMI exactly like the parallel LCD on Endpoint boards or
// the ST7735 / SSD1306 SPI panels:
//
//     using var display = new HdmiController();
//     display.Enable();
//     display.Flush(pixels, PixelFormat.Format16bppRgb565);
//
// Framebuffer geometry (width, height, bpp, stride) is read live from
// /sys/class/graphics/fb0 at Enable() time, so if the user tweaks config.txt
// (hdmi_mode, framebuffer_width/height) the next Enable picks up the change.

#nullable enable
#pragma warning disable CA1416 // PixelFormat is a plain enum; cross-platform safe

using System;
using System.Buffers;
using System.IO;
using GHIElectronics.Endpoint.Devices.Display;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace GHIElectronics.Endpoint.Drivers.HDMI;

/// <summary>HDMI display via the Linux framebuffer at /dev/fb0.</summary>
public sealed class HdmiController : DisplayController
{
    private FileStream? framebuffer;
    private int width, height, bpp, stride;

    public override int Width => this.width;
    public override int Height => this.height;
    public override bool IsEnabled => this.framebuffer != null;

    /// <summary>Opens /dev/fb0 and reads the live framebuffer geometry from /sys/class/graphics/fb0.</summary>
    public override void Enable()
    {
        if (this.framebuffer != null) return;

        ReadSize("virtual_size", out this.width, out this.height);
        this.bpp = ReadInt("bits_per_pixel", fallback: 32);
        this.stride = ReadInt("stride", fallback: this.width * (this.bpp / 8));

        this.framebuffer = new FileStream("/dev/fb0", FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
    }

    /// <summary>Closes /dev/fb0. The HDMI output keeps showing whatever was last written.</summary>
    public override void Disable()
    {
        this.framebuffer?.Dispose();
        this.framebuffer = null;
    }

    public override void Dispose()
    {
        try { this.Disable(); } catch { /* best effort */ }
    }

    /// <summary>Blit a rectangle. Converts source pixels to the framebuffer's native bpp (16, 24, or 32).</summary>
    public override void Flush(ReadOnlySpan<byte> pixels, PixelFormat format,
                               int x, int y, int rectWidth, int rectHeight)
    {
        if (this.framebuffer == null)
            throw new InvalidOperationException("Flush called before Enable");
        if (x < 0 || y < 0 || rectWidth <= 0 || rectHeight <= 0
            || x + rectWidth > this.width || y + rectHeight > this.height)
            throw new ArgumentException("Flush rectangle is outside the panel");

        int sourceStride = format switch
        {
            PixelFormat.Format16bppRgb565 => rectWidth * 2,
            PixelFormat.Format24bppRgb    => rectWidth * 3,
            PixelFormat.Format32bppRgb
                or PixelFormat.Format32bppArgb => rectWidth * 4,
            _ => throw new NotSupportedException($"Unsupported source pixel format: {format}"),
        };
        if (pixels.Length < sourceStride * rectHeight)
            throw new ArgumentException("Pixel buffer is smaller than the rectangle");

        int bytesPerPixel = this.bpp / 8;
        int rowBytes = rectWidth * bytesPerPixel;

        byte[]? rented = null;
        Span<byte> row = rowBytes <= 8192
            ? stackalloc byte[rowBytes]
            : (rented = ArrayPool<byte>.Shared.Rent(rowBytes)).AsSpan(0, rowBytes);
        try
        {
            for (int r = 0; r < rectHeight; r++)
            {
                ConvertRow(pixels.Slice(r * sourceStride, sourceStride), row, format, this.bpp, rectWidth);
                this.framebuffer.Seek((long)(y + r) * this.stride + x * bytesPerPixel, SeekOrigin.Begin);
                this.framebuffer.Write(row);
            }
            this.framebuffer.Flush();
        }
        finally
        {
            if (rented != null) ArrayPool<byte>.Shared.Return(rented);
        }
    }

    // ---------- helpers ----------

    private static int ReadInt(string name, int fallback)
    {
        try { return int.Parse(File.ReadAllText($"/sys/class/graphics/fb0/{name}").Trim()); }
        catch { return fallback; }
    }

    private static void ReadSize(string name, out int w, out int h)
    {
        w = h = 0;
        try
        {
            var parts = File.ReadAllText($"/sys/class/graphics/fb0/{name}").Trim().Split(',');
            if (parts.Length >= 2 && int.TryParse(parts[0], out int pw) && int.TryParse(parts[1], out int ph))
            {
                w = pw; h = ph;
            }
        }
        catch { /* fallbacks stay at 0 — Enable will still open fb0 and user can check Width/Height */ }
    }

    // Convert one row of source pixels to the destination bpp.
    // Matches the conversion matrix of ParallelDisplayController so HDMI behaves
    // identically to the Endpoint parallel display for the same source formats.
    private static void ConvertRow(ReadOnlySpan<byte> src, Span<byte> dst,
                                   PixelFormat format, int targetBpp, int pixels)
    {
        if (targetBpp == 16)
        {
            if (format == PixelFormat.Format16bppRgb565)
            {
                src.Slice(0, pixels * 2).CopyTo(dst);
                return;
            }
            int srcBpp = format switch
            {
                PixelFormat.Format24bppRgb => 3,
                PixelFormat.Format32bppRgb or PixelFormat.Format32bppArgb => 4,
                _ => throw new NotSupportedException($"Cannot convert {format} to RGB565"),
            };
            for (int p = 0; p < pixels; p++)
            {
                byte b = src[p * srcBpp + 0];
                byte g = src[p * srcBpp + 1];
                byte r = src[p * srcBpp + 2];
                ushort v = (ushort)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));
                dst[p * 2 + 0] = (byte)(v & 0xFF);
                dst[p * 2 + 1] = (byte)(v >> 8);
            }
            return;
        }
        if (targetBpp == 24)
        {
            if (format == PixelFormat.Format24bppRgb)
            {
                src.Slice(0, pixels * 3).CopyTo(dst);
                return;
            }
            if (format is PixelFormat.Format32bppRgb or PixelFormat.Format32bppArgb)
            {
                for (int p = 0; p < pixels; p++)
                {
                    dst[p * 3 + 0] = src[p * 4 + 0];
                    dst[p * 3 + 1] = src[p * 4 + 1];
                    dst[p * 3 + 2] = src[p * 4 + 2];
                }
                return;
            }
        }
        if (targetBpp == 32)
        {
            // BGRX8888 target. Byte 0 = B, 1 = G, 2 = R, 3 = X.
            if (format == PixelFormat.Format16bppRgb565)
            {
                for (int p = 0; p < pixels; p++)
                {
                    byte lo = src[p * 2 + 0], hi = src[p * 2 + 1];
                    int r5 = (hi >> 3) & 0x1F;
                    int g6 = ((hi & 0x07) << 3) | ((lo >> 5) & 0x07);
                    int b5 = lo & 0x1F;
                    dst[p * 4 + 0] = (byte)((b5 << 3) | (b5 >> 2));
                    dst[p * 4 + 1] = (byte)((g6 << 2) | (g6 >> 4));
                    dst[p * 4 + 2] = (byte)((r5 << 3) | (r5 >> 2));
                    dst[p * 4 + 3] = 0;
                }
                return;
            }
            if (format == PixelFormat.Format24bppRgb)
            {
                for (int p = 0; p < pixels; p++)
                {
                    dst[p * 4 + 0] = src[p * 3 + 0];
                    dst[p * 4 + 1] = src[p * 3 + 1];
                    dst[p * 4 + 2] = src[p * 3 + 2];
                    dst[p * 4 + 3] = 0;
                }
                return;
            }
            if (format is PixelFormat.Format32bppRgb or PixelFormat.Format32bppArgb)
            {
                src.Slice(0, pixels * 4).CopyTo(dst);
                return;
            }
        }
        throw new NotSupportedException($"Cannot convert {format} to {targetBpp}bpp");
    }
}
