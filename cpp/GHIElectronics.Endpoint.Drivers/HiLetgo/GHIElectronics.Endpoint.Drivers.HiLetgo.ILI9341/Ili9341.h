// HiLetgo ILI9341 240x320 SPI TFT driver for GHI Endpoint (C/C++).
//
// Companion to GHIElectronics.Endpoint.Drivers.HiLetgo.ILI9341 (.NET).
// Header-only; drop this file into a project and include it.
//
// Wiring (any free GPIOs for CS/DC/RST):
//   ILI9341     EPDomino24
//   -------     ----------
//   VCC         mikroBUS 3.3V
//   GND         mikroBUS GND
//   SCK         mikroBUS SCK   (e.g. PZ0 on mikroBUS #2)
//   SDI/MOSI    mikroBUS MOSI  (e.g. PZ2 on mikroBUS #2)
//   CS          any free GPIO  (driven in software by this driver)
//   DC / RS     any free GPIO  (low = command, high = data)
//   RST         any free GPIO  (active-low pulse at init)
//   LED / BL    tie to +3.3V   (backlight always on)
//
// Usage:
//
//     #include <endpoint.h>
//     #include <endpoint/SpiController.h>
//     #include "Ili9341.h"
//
//     namespace Core = GHIElectronics::Endpoint::Core;
//     namespace Spi  = GHIElectronics::Endpoint::Devices::Spi;
//     namespace Gpio = GHIElectronics::Endpoint::Devices::Gpio;
//
//     Core::SpiController::Initialize(Core::EPDomino24::Spi::Spi1, 4096);
//     Spi::SpiConnectionSettings settings(Core::EPDomino24::Spi::Spi1);
//     settings.Mode = Spi::SpiMode::Mode0;
//     settings.ClockFrequency = 10'000'000;
//     auto spi = Spi::SpiDevice::Create(settings);
//
//     Gpio::GpioController gpio;
//     Ili9341 tft(gpio, spi,
//                 Core::EPM815::Gpio::Pin::PA14,   // CS
//                 Core::EPM815::Gpio::Pin::PF14,   // DC
//                 Core::EPM815::Gpio::Pin::PF4);   // RST
//     tft.SwapRedBlue(true);   // set for modules where yellow shows as cyan
//     tft.Initialize();
//
//     // Canvas drawing...
//     tft.Flush(canvas.Buffer());
#pragma once
#include <algorithm>
#include <chrono>
#include <cstdint>
#include <initializer_list>
#include <span>
#include <thread>
#include <vector>

#include <endpoint.h>

class Ili9341 {
public:
    Ili9341(GHIElectronics::Endpoint::Devices::Gpio::GpioController& gpio,
            GHIElectronics::Endpoint::Devices::Spi::SpiDevice& spi,
            int cs, int dc, int rst,
            int width = 240, int height = 320)
        : gpio_(gpio), spi_(spi), cs_(cs), dc_(dc), rst_(rst),
          width_(width), height_(height) {
        using namespace GHIElectronics::Endpoint::Devices::Gpio;
        for (int p : {cs_, dc_, rst_}) gpio_.OpenPin(p, PinMode::Output, PinValue::High);
        swap_.resize((size_t)width_ * height_ * 2);
    }

    int  Width()  const { return width_; }
    int  Height() const { return height_; }

    // Swap R and B channels before pushing pixels. Some HiLetgo / eBay
    // ILI9341 modules have the physical colour filter wired as BGR
    // regardless of the chip's MADCTL BGR bit, so colours look swapped
    // (yellow renders as cyan, red as blue). Flip this on for those
    // modules; leave off for a "normal" RGB-filter panel.
    void SwapRedBlue(bool on) { swapRedBlue_ = on; }

    void Initialize() {
        using namespace GHIElectronics::Endpoint::Devices::Gpio;
        gpio_.Write(rst_, PinValue::Low);
        std::this_thread::sleep_for(std::chrono::milliseconds(20));
        gpio_.Write(rst_, PinValue::High);
        std::this_thread::sleep_for(std::chrono::milliseconds(150));
        Command(0x01); std::this_thread::sleep_for(std::chrono::milliseconds(150));   // SWRESET
        Command(0x11); std::this_thread::sleep_for(std::chrono::milliseconds(120));   // SLPOUT
        Command(0x3A, {0x55});                                                        // COLMOD 16bpp
        // MADCTL: MX only (0x40). Portrait 240x320, RGB filter. BGR-wired
        // panels are handled via SwapRedBlue in Flush, not here.
        Command(0x36, {0x40});
        Command(0x29); std::this_thread::sleep_for(std::chrono::milliseconds(20));    // DISPON
    }

    // Push a full frame to the panel.
    //
    // `pixels` is an RGB565 little-endian buffer (as produced by
    // Canvas.Buffer() or LVGL's custom flush callback). ILI9341 reads
    // 16 bpp big-endian over SPI, so we always byte-swap; SwapRedBlue
    // additionally swaps R and B for BGR-wired modules.
    void Flush(std::span<const uint8_t> pixels) {
        if (swap_.size() != pixels.size()) swap_.resize(pixels.size());
        if (swapRedBlue_) {
            for (size_t i = 0; i + 1 < pixels.size(); i += 2) {
                uint8_t lo = pixels[i], hi = pixels[i + 1];
                uint8_t r = hi >> 3;
                uint8_t g = ((hi & 0x07) << 3) | (lo >> 5);
                uint8_t b = lo & 0x1F;
                swap_[i]     = (uint8_t)((b << 3) | (g >> 3));
                swap_[i + 1] = (uint8_t)(((g & 0x07) << 5) | r);
            }
        } else {
            for (size_t i = 0; i + 1 < pixels.size(); i += 2) {
                swap_[i] = pixels[i + 1]; swap_[i + 1] = pixels[i];
            }
        }
        SetWindow(0, 0, width_, height_);
        Command(0x2C);                                                                // RAMWR
        WriteData(swap_);
    }

private:
    GHIElectronics::Endpoint::Devices::Gpio::GpioController& gpio_;
    GHIElectronics::Endpoint::Devices::Spi::SpiDevice&       spi_;
    int cs_, dc_, rst_, width_, height_;
    bool swapRedBlue_ = false;
    std::vector<uint8_t> swap_;

    void Command(uint8_t cmd, std::initializer_list<uint8_t> data = {}) {
        using namespace GHIElectronics::Endpoint::Devices::Gpio;
        gpio_.Write(cs_, PinValue::Low);
        gpio_.Write(dc_, PinValue::Low);
        spi_.WriteByte(cmd);
        if (data.size()) {
            gpio_.Write(dc_, PinValue::High);
            spi_.Write(std::vector<uint8_t>(data));
        }
        gpio_.Write(cs_, PinValue::High);
    }

    void WriteData(const std::vector<uint8_t>& data) {
        using namespace GHIElectronics::Endpoint::Devices::Gpio;
        gpio_.Write(cs_, PinValue::Low);
        gpio_.Write(dc_, PinValue::High);
        // Linux spidev default bufsiz tops out near 4 KB; chunk at 2 KB.
        for (size_t off = 0; off < data.size(); off += 2048) {
            size_t n = std::min<size_t>(2048, data.size() - off);
            spi_.Write(std::span<const uint8_t>(data.data() + off, n));
        }
        gpio_.Write(cs_, PinValue::High);
    }

    void SetWindow(int x, int y, int w, int h) {
        int x1 = x, x2 = x + w - 1, y1 = y, y2 = y + h - 1;
        Command(0x2A, {(uint8_t)(x1 >> 8), (uint8_t)(x1 & 0xFF),
                       (uint8_t)(x2 >> 8), (uint8_t)(x2 & 0xFF)});
        Command(0x2B, {(uint8_t)(y1 >> 8), (uint8_t)(y1 & 0xFF),
                       (uint8_t)(y2 >> 8), (uint8_t)(y2 & 0xFF)});
    }
};
