// SinoWealth SH1106 / SH1107 I2C 1bpp OLED driver for GHI Endpoint (C/C++).
//
// Companion to GHIElectronics.Endpoint.Drivers.SinoWealth.SH110x (.NET).
// Header-only; drop this file next to main.cpp.
//
// Usage:
//
//     #include <endpoint.h>
//     #include <endpoint/I2cController.h>
//     #include "Sh110x.h"
//
//     namespace Core    = GHIElectronics::Endpoint::Core;
//     namespace I2cNs   = GHIElectronics::Endpoint::Devices::I2c;
//     namespace Drawing = GHIElectronics::Endpoint::UI::Drawing;
//
//     Core::I2cController::Initialize(Core::EPDomino24::I2c::I2c6);
//     auto i2c = I2cNs::I2cDevice::Create(
//                    Sh110xController::GetConnectionSettings(Core::EPDomino24::I2c::I2c6));
//     Sh110xController oled(i2c, Sh110xChip::SH1106);
//     oled.Enable();
//
//     auto canvas = Drawing::Canvas::Create(oled.Width(), oled.Height(),
//                                           Drawing::PixelFormat::Monochrome);
//     canvas.Clear(Drawing::Color::Black());
//     canvas.DrawText("Hello", font, 2, 2, 22, Drawing::Color::White());
//     oled.Flush(canvas.Buffer(), Drawing::PixelFormat::Monochrome);
//
// Differences from SSD1306 that this driver handles:
//  - SH110x has no horizontal addressing mode; a frame is pushed one page at a
//    time (set page, set column low/high, write bytes).
//  - SH1106 has 132 columns of RAM behind 128 columns of glass, so the visible
//    area normally starts at column 2. Adjust via SetColumnOffset if needed.
//  - The charge pump command is 0xAD 0x8B, not the SSD1306 0x8D 0x14.
#pragma once

#include <algorithm>
#include <cstdint>
#include <span>
#include <vector>

#include <endpoint.h>

enum class Sh110xChip {
    SH1106 = 0,   // 132x64 RAM behind 128x64 glass. Needs a column offset (usually 2). No horizontal addressing mode.
    SH1107 = 1,   // 128x128 RAM (16 pages). Used for 128x64 panels and for rotated 64x128 panels.
};

class Sh110xController {
public:
    static GHIElectronics::Endpoint::Devices::I2c::I2cConnectionSettings
    GetConnectionSettings(int busId, int slaveAddress = 0x3C) {
        return GHIElectronics::Endpoint::Devices::I2c::I2cConnectionSettings{busId, slaveAddress};
    }

    Sh110xController(GHIElectronics::Endpoint::Devices::I2c::I2cDevice i2c,
                     Sh110xChip chip = Sh110xChip::SH1106,
                     int width = 128, int height = 64,
                     int columnOffset = -1, bool rotate90 = false)
        : i2c_(std::move(i2c)), chip_(chip), width_(width), height_(height),
          rotate90_(rotate90) {
        if (width <= 0 || height <= 0 || (width % 8) != 0 || (height % 8) != 0) {
            throw GHIElectronics::Endpoint::Core::ArgumentException(
                "Width and height must be positive multiples of 8.");
        }
        if (rotate90 && chip != Sh110xChip::SH1107) {
            throw GHIElectronics::Endpoint::Core::ArgumentException(
                "rotate90 is only supported on SH1107.");
        }
        columnOffset_ = columnOffset >= 0 ? columnOffset
                                          : (chip == Sh110xChip::SH1106 ? 2 : 0);

        pageWidth_ = rotate90 ? height : width;
        pageCount_ = rotate90 ? (width / 8) : (height / 8);

        vram_.assign((size_t)width * height / 8, 0);
        shadow_.assign((size_t)pageCount_ * pageWidth_, 0);
        pageBuffer_.assign((size_t)pageWidth_ + 1, 0);
        pageBuffer_[0] = 0x40;    // data stream control byte
    }

    int  Width()  const { return width_; }
    int  Height() const { return height_; }
    bool IsEnabled() const { return isEnabled_; }
    Sh110xChip Chip() const { return chip_; }

    int  ColumnOffset() const { return columnOffset_; }
    void SetColumnOffset(int v) { columnOffset_ = v; forceFullFlush_ = true; }

    bool SkipUnchangedPages() const { return skipUnchangedPages_; }
    void SetSkipUnchangedPages(bool v) { skipUnchangedPages_ = v; }

    void Enable() {
        if (chip_ == Sh110xChip::SH1106) InitializeSh1106();
        else                             InitializeSh1107();
        Clear();
        SendCommand(CMD_DISPLAY_ON);
        isEnabled_ = true;
    }

    void Disable() {
        if (!isEnabled_) return;
        SendCommand(CMD_DISPLAY_OFF);
        isEnabled_ = false;
    }

    // Full-screen Flush with pixel format (Rgb565 or Monochrome).
    void Flush(std::span<const uint8_t> pixels,
               GHIElectronics::Endpoint::UI::Drawing::PixelFormat format) {
        Flush(pixels, format, 0, 0, width_, height_);
    }

    // Region Flush: pushes a rectangle `(x, y, width, height)` taken from
    // `pixels`. Rgb565 input: any non-black pixel lights the cell. Monochrome
    // input: 1 bpp MSB-first, matches Canvas.PixelFormat.Monochrome.
    void Flush(std::span<const uint8_t> pixels,
               GHIElectronics::Endpoint::UI::Drawing::PixelFormat format,
               int x, int y, int width, int height) {
        using GHIElectronics::Endpoint::UI::Drawing::PixelFormat;
        if (x < 0 || y < 0 || width <= 0 || height <= 0
            || x + width > width_ || y + height > height_) {
            throw GHIElectronics::Endpoint::Core::ArgumentException(
                "Flush region outside the panel");
        }

        if (format == PixelFormat::Rgb565) {
            if ((int)pixels.size() < width * height * 2) {
                throw GHIElectronics::Endpoint::Core::ArgumentException(
                    "Pixel buffer is smaller than the region");
            }
            for (int row = 0; row < height; ++row) {
                int panelY = y + row;
                int page = panelY >> 3;
                uint8_t mask = (uint8_t)(1 << (panelY & 7));
                int srcRow = row * width * 2;
                for (int col = 0; col < width; ++col) {
                    int srcIndex = srcRow + col * 2;
                    uint16_t px = (uint16_t)(pixels[srcIndex] | (pixels[srcIndex + 1] << 8));
                    int vIndex = page * width_ + (x + col);
                    if (px != 0) vram_[vIndex] |= mask;
                    else         vram_[vIndex] = (uint8_t)(vram_[vIndex] & ~mask);
                }
            }
        }
        else if (format == PixelFormat::Monochrome) {
            int srcStride = (width + 7) / 8;
            if ((int)pixels.size() < srcStride * height) {
                throw GHIElectronics::Endpoint::Core::ArgumentException(
                    "Pixel buffer is smaller than the region");
            }
            for (int row = 0; row < height; ++row) {
                int panelY = y + row;
                int page = panelY >> 3;
                uint8_t mask = (uint8_t)(1 << (panelY & 7));
                int srcRow = row * srcStride;
                for (int col = 0; col < width; ++col) {
                    bool lit = (pixels[srcRow + (col >> 3)] & (1 << (7 - (col & 7)))) != 0;
                    int vIndex = page * width_ + (x + col);
                    if (lit) vram_[vIndex] |= mask;
                    else     vram_[vIndex] = (uint8_t)(vram_[vIndex] & ~mask);
                }
            }
        }
        else {
            throw GHIElectronics::Endpoint::Core::NotSupportedException(
                "Sh110xController accepts Rgb565 or Monochrome pixel formats.");
        }

        FlushVram();
    }

    // Push a pre-packed VRAM blob (SH110x native page/column format,
    // width * height / 8 bytes). Faster than Rgb565 / Monochrome paths when
    // the caller already has the panel's native layout.
    void DrawBufferNative(std::span<const uint8_t> buffer) {
        size_t n = std::min(buffer.size(), vram_.size());
        std::copy(buffer.begin(), buffer.begin() + n, vram_.begin());
        FlushVram();
    }

    // Raw command escape hatch, useful while feeling out an unknown module.
    void SendCommand(uint8_t command) {
        commandBuffer_[0] = 0x00;    // command stream control byte
        commandBuffer_[1] = command;
        i2c_.Write(commandBuffer_);
    }

    void SetColorFormat(bool invert) { SendCommand(invert ? 0xA7 : 0xA6); }

    void SetContrast(uint8_t level) {
        SendCommand(CMD_SET_CONTRAST);
        SendCommand(level);
    }

    // Turns the picture 180 degrees. Both commands must agree or the image comes out mirrored.
    void SetUpsideDown(bool upsideDown) {
        SendCommand(upsideDown ? CMD_SET_SEG_REMAP : (uint8_t)(CMD_SET_SEG_REMAP | 0x01));
        SendCommand(upsideDown ? CMD_SET_COM_SCAN_INC : CMD_SET_COM_SCAN_DEC);
    }

    void TurnOn()  { SendCommand(CMD_DISPLAY_ON); }
    void TurnOff() { SendCommand(CMD_DISPLAY_OFF); }

    void Clear() {
        std::fill(vram_.begin(), vram_.end(), 0);
        ClearControllerRam();
        std::fill(shadow_.begin(), shadow_.end(), 0);
        forceFullFlush_ = false;
    }

    ~Sh110xController() {
        try { Disable(); } catch (...) {}
    }

    Sh110xController(const Sh110xController&) = delete;
    Sh110xController& operator=(const Sh110xController&) = delete;

private:
    // Command set. Shared with SSD1306 except where noted.
    static constexpr uint8_t CMD_SET_LOW_COLUMN      = 0x00;
    static constexpr uint8_t CMD_SET_HIGH_COLUMN     = 0x10;
    static constexpr uint8_t CMD_SET_MEMORY_MODE     = 0x20;    // SH1107: 0x20 page mode, 0x21 vertical mode
    static constexpr uint8_t CMD_SET_CONTRAST        = 0x81;
    static constexpr uint8_t CMD_SET_SEG_REMAP       = 0xA0;    // OR 1 for reversed
    static constexpr uint8_t CMD_SET_MULTIPLEX       = 0xA8;
    static constexpr uint8_t CMD_DC_DC               = 0xAD;    // SH110x charge pump. SSD1306 uses 0x8D.
    static constexpr uint8_t CMD_DC_DC_ON            = 0x8B;
    static constexpr uint8_t CMD_DISPLAY_OFF         = 0xAE;
    static constexpr uint8_t CMD_DISPLAY_ON          = 0xAF;
    static constexpr uint8_t CMD_SET_PAGE            = 0xB0;
    static constexpr uint8_t CMD_SET_COM_SCAN_INC    = 0xC0;
    static constexpr uint8_t CMD_SET_COM_SCAN_DEC    = 0xC8;
    static constexpr uint8_t CMD_SET_DISPLAY_OFFSET  = 0xD3;
    static constexpr uint8_t CMD_SET_CLOCK_DIV       = 0xD5;
    static constexpr uint8_t CMD_SET_PRECHARGE       = 0xD9;
    static constexpr uint8_t CMD_SET_COM_PINS        = 0xDA;
    static constexpr uint8_t CMD_SET_VCOM_DETECT     = 0xDB;
    static constexpr uint8_t CMD_SET_START_LINE_1107 = 0xDC;    // SH1107 start line is 2 bytes.

    GHIElectronics::Endpoint::Devices::I2c::I2cDevice i2c_;
    Sh110xChip chip_;
    int width_, height_;
    bool rotate90_;
    int columnOffset_ = 0;
    int pageCount_ = 0;
    int pageWidth_ = 0;

    std::vector<uint8_t> vram_;
    std::vector<uint8_t> shadow_;
    std::vector<uint8_t> pageBuffer_;
    std::array<uint8_t, 2> commandBuffer_{};

    bool skipUnchangedPages_ = true;
    bool forceFullFlush_ = true;
    bool isEnabled_ = false;

    void InitializeSh1106() {
        SendCommand(CMD_DISPLAY_OFF);
        SendCommand(CMD_SET_CLOCK_DIV);        SendCommand(0x80);
        SendCommand(CMD_SET_MULTIPLEX);        SendCommand((uint8_t)(height_ - 1));
        SendCommand(CMD_SET_DISPLAY_OFFSET);   SendCommand(0x00);
        SendCommand(0x40);                                              // start line 0
        SendCommand(CMD_DC_DC);                SendCommand(CMD_DC_DC_ON);
        SendCommand(0x32);                                              // pump output 8.0 V
        SendCommand((uint8_t)(CMD_SET_SEG_REMAP | 0x01));                // column 127 maps to SEG0
        SendCommand(CMD_SET_COM_SCAN_DEC);
        SendCommand(CMD_SET_COM_PINS);         SendCommand(height_ > 32 ? 0x12 : 0x02);
        SendCommand(CMD_SET_CONTRAST);         SendCommand(0x80);
        SendCommand(CMD_SET_PRECHARGE);        SendCommand(0x22);
        SendCommand(CMD_SET_VCOM_DETECT);      SendCommand(0x35);
        SendCommand(0xA4);                                              // resume from RAM
        SendCommand(0xA6);                                              // normal, not inverted
    }

    void InitializeSh1107() {
        SendCommand(CMD_DISPLAY_OFF);
        SendCommand(CMD_SET_CLOCK_DIV);        SendCommand(0x51);
        SendCommand(CMD_SET_MEMORY_MODE);                                // page addressing mode
        SendCommand(CMD_SET_CONTRAST);         SendCommand(0x4F);
        SendCommand(CMD_DC_DC);                SendCommand(CMD_DC_DC_ON);
        SendCommand(CMD_SET_SEG_REMAP);                                  // 0xA0 normal, 0xA1 mirrored
        SendCommand(CMD_SET_COM_SCAN_INC);                               // 0xC0 normal, 0xC8 flipped
        SendCommand(CMD_SET_START_LINE_1107);  SendCommand(0x00);
        SendCommand(CMD_SET_DISPLAY_OFFSET);   SendCommand(0x00);
        SendCommand(CMD_SET_MULTIPLEX);        SendCommand((uint8_t)((rotate90_ ? width_ : height_) - 1));
        SendCommand(CMD_SET_PRECHARGE);        SendCommand(0x22);
        SendCommand(CMD_SET_VCOM_DETECT);      SendCommand(0x35);
        SendCommand(0xA4);
        SendCommand(0xA6);
    }

    void ClearControllerRam() {
        int ramWidth = chip_ == Sh110xChip::SH1106 ? 132 : 128;
        int ramPages = chip_ == Sh110xChip::SH1106 ? 8   : 16;
        std::vector<uint8_t> zeros((size_t)ramWidth + 1, 0);
        zeros[0] = 0x40;    // data stream control byte

        for (int page = 0; page < ramPages; ++page) {
            SendCommand((uint8_t)(CMD_SET_PAGE | page));
            SendCommand(CMD_SET_LOW_COLUMN);
            SendCommand(CMD_SET_HIGH_COLUMN);
            i2c_.Write(zeros);
        }
    }

    // Pushes vram to the panel one page per transaction. SH110x has no
    // horizontal addressing.
    void FlushVram() {
        bool full = forceFullFlush_;
        forceFullFlush_ = false;

        for (int page = 0; page < pageCount_; ++page) {
            int shadowStart = page * pageWidth_;

            if (rotate90_) BuildRotatedPage(page);
            else {
                int src = page * width_;
                std::copy(vram_.begin() + src,
                          vram_.begin() + src + pageWidth_,
                          pageBuffer_.begin() + 1);
            }

            if (!full && skipUnchangedPages_ && PageUnchanged(shadowStart)) continue;

            std::copy(pageBuffer_.begin() + 1,
                      pageBuffer_.begin() + 1 + pageWidth_,
                      shadow_.begin() + shadowStart);

            SendCommand((uint8_t)(CMD_SET_PAGE | page));
            SendCommand((uint8_t)(CMD_SET_LOW_COLUMN  | (columnOffset_ & 0x0F)));
            SendCommand((uint8_t)(CMD_SET_HIGH_COLUMN | ((columnOffset_ >> 4) & 0x0F)));

            i2c_.Write(pageBuffer_);
        }
    }

    bool PageUnchanged(int shadowStart) const {
        for (int i = 0; i < pageWidth_; ++i) {
            if (pageBuffer_[i + 1] != shadow_[shadowStart + i]) return false;
        }
        return true;
    }

    // Transposes a landscape frame into the portrait layout an SH1107
    // 64x128 panel expects.
    void BuildRotatedPage(int page) {
        int baseX = page * 8;
        for (int c = 0; c < pageWidth_; ++c) {
            int rowStart = (c >> 3) * width_;
            uint8_t mask = (uint8_t)(1 << (c & 7));
            uint8_t value = 0;
            for (int bit = 0; bit < 8; ++bit) {
                if (vram_[rowStart + baseX + bit] & mask) value |= (uint8_t)(1 << bit);
            }
            pageBuffer_[c + 1] = value;
        }
    }
};
