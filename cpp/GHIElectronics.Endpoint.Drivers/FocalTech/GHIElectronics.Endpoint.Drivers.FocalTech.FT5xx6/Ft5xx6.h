// FocalTech FT5xx6 capacitive-touch driver for GHI Endpoint (C/C++).
//
// Companion to GHIElectronics.Endpoint.Drivers.FocalTech.FT5xx6 (.NET).
// Header-only; drop this file next to main.cpp. Reusable across any Endpoint
// board: the caller opens a GpioController and passes it in; this header
// does no chip-specific pin math.
//
// Usage:
//
//     #include <endpoint.h>
//     #include <endpoint/I2cController.h>
//     #include "Ft5xx6.h"
//
//     namespace Core = GHIElectronics::Endpoint::Core;
//     namespace Gpio = GHIElectronics::Endpoint::Devices::Gpio;
//
//     Core::I2cController::Initialize(Core::EPDomino24::I2c::I2c5);
//     Gpio::GpioController gpio;
//     Ft5xx6Controller touch(Core::EPDomino24::I2c::I2c5,
//                            Core::EPDomino24::Gpio::Pin::PB11,
//                            gpio);
//     touch.SetOnTouchDown([](int x, int y){ std::printf("down (%d, %d)\n", x, y); });
#pragma once

#include <array>
#include <cstdint>
#include <cstdio>
#include <functional>

#include <endpoint.h>

class Ft5xx6Controller {
public:
    static constexpr uint8_t DeviceAddress = 0x38;

    enum class Gesture : uint8_t {
        MoveUp = 0x10,
        MoveLeft = 0x14,
        MoveDown = 0x18,
        MoveRight = 0x1C,
        ZoomIn = 0x48,
        ZoomOut = 0x49,
    };

    enum class TouchOrientation {
        Degrees0 = 0,
        Degrees90 = 1,
        Degrees180 = 2,
        Degrees270 = 3,
    };

    using TouchHandler = std::function<void(int x, int y)>;
    using GestureHandler = std::function<void(Gesture g)>;

    Ft5xx6Controller(int i2cBus, int interruptPin,
                     GHIElectronics::Endpoint::Devices::Gpio::GpioController& gpio)
        : gpio_(gpio), interruptPin_(interruptPin),
          i2c_(GHIElectronics::Endpoint::Devices::I2c::I2cDevice::Create(
                   GHIElectronics::Endpoint::Devices::I2c::I2cConnectionSettings{i2cBus, DeviceAddress})) {
        using namespace GHIElectronics::Endpoint::Devices::Gpio;
        gpio_.OpenPin(interruptPin_, PinMode::Input);
        callbackId_ = gpio_.RegisterCallbackForPinValueChangedEvent(
            interruptPin_, PinEventTypes::Falling,
            [this](GpioController&, const PinValueChangedEventArgs&) { this->OnInterrupt(); });
    }

    ~Ft5xx6Controller() {
        try { gpio_.UnregisterCallbackForPinValueChangedEvent(interruptPin_, callbackId_); } catch (...) {}
        try { gpio_.ClosePin(interruptPin_); } catch (...) {}
    }

    Ft5xx6Controller(const Ft5xx6Controller&) = delete;
    Ft5xx6Controller& operator=(const Ft5xx6Controller&) = delete;

    void SetOnTouchDown(TouchHandler h)   { touchDown_ = std::move(h); }
    void SetOnTouchUp(TouchHandler h)     { touchUp_   = std::move(h); }
    void SetOnTouchMove(TouchHandler h)   { touchMove_ = std::move(h); }
    void SetOnGesture(GestureHandler h)   { gesture_   = std::move(h); }

    int  Width  = 0;
    int  Height = 0;
    TouchOrientation Orientation = TouchOrientation::Degrees0;
    int  SampleCount = 5;

private:
    GHIElectronics::Endpoint::Devices::Gpio::GpioController& gpio_;
    int interruptPin_;
    int callbackId_ = -1;
    GHIElectronics::Endpoint::Devices::I2c::I2cDevice i2c_;
    std::array<uint8_t, 1>  addressBuffer_{0};
    std::array<uint8_t, 32> read32_{};

    TouchHandler   touchDown_;
    TouchHandler   touchUp_;
    TouchHandler   touchMove_;
    GestureHandler gesture_;

    void OnInterrupt() {
        try {
            i2c_.WriteRead(addressBuffer_, read32_);
            auto& buf = read32_;

            if (buf[1] != 0 && gesture_) {
                gesture_(static_cast<Gesture>(buf[1]));
            }

            // We do not read TD_STATUS: it excludes touch-up events even
            // though the per-sample registers contain the correct data.
            for (int i = 0; i < SampleCount; ++i) {
                int idx = i * 6 + 3;
                int flag = (buf[idx] & 0xC0) >> 6;
                int x = ((buf[idx]     & 0x0F) << 8) | buf[idx + 1];
                int y = ((buf[idx + 2] & 0x0F) << 8) | buf[idx + 3];

                if (Orientation != TouchOrientation::Degrees0) {
                    if (Width == 0 || Height == 0) {
                        throw GHIElectronics::Endpoint::Core::ArgumentException(
                            "Width and Height must be set before rotating");
                    }
                    switch (Orientation) {
                        case TouchOrientation::Degrees180:
                            x = Width - x;
                            y = Height - y;
                            break;
                        case TouchOrientation::Degrees270: {
                            int temp = x;
                            x = Width - y;
                            y = temp;
                            break;
                        }
                        case TouchOrientation::Degrees90: {
                            int tmp = x;
                            x = y;
                            y = Width - tmp;
                            break;
                        }
                        default: break;
                    }
                }

                if      (flag == 0 && touchDown_) touchDown_(x, y);
                else if (flag == 1 && touchUp_)   touchUp_(x, y);
                else if (flag == 2 && touchMove_) touchMove_(x, y);
                else if (flag == 3) break;
            }
        } catch (...) {
            // Don't tear down the IRQ handler on a single bad read.
        }
    }
};
