// Raspberry Pi Zero 2 W board support for GHI Endpoint (C/C++).
//
// Companion to GHIElectronics.Endpoint.Drivers.RaspberryPi (.NET and Python).
// Header-only; drop this file next to main.cpp and link against libendpoint.
//
// Including this header registers Zero2WBoardSupport with libendpoint's
// BoardSupport::Current() via a static initializer. User code then uses
// Core's chip-neutral verbs (I2cController, SpiController, PwmController,
// SerialPortController) with Zero2W pin / bus / port constants:
//
//     #include <endpoint.h>
//     #include "Zero2W.h"
//     using namespace GHIElectronics::Endpoint::Core;
//     using namespace GHIElectronics::Endpoint::Drivers::RaspberryPi;
//
//     I2cController::Initialize(Zero2W::I2c::I2c1);
//     SpiController::Initialize(Zero2W::Spi::Spi0);
//     PwmController::Initialize(Zero2W::Pwm::Pin::Pin12);
//     int chip = PwmController::GetChipId(Zero2W::Pwm::Pin::Pin12);
//     int ch   = PwmController::GetChannelId(Zero2W::Pwm::Pin::Pin12);
//     SerialPortController::Initialize(Zero2W::SerialPort::Uart1);
//
// Identical shape to Endpoint boards; swap EPDomino24:: for Zero2W::.
// Zero2WBoardSupport overrides only the peripherals Pi actually implements
// (I2C, SPI, PWM, SerialPort via endpoint-pinmux + modprobe); everything
// else (CAN, DAC, etc.) inherits UnknownBoardSupport's "not supported on
// this board" throw.

#pragma once

#include <cerrno>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fcntl.h>
#include <map>
#include <stdexcept>
#include <string>
#include <sys/wait.h>
#include <unistd.h>
#include <vector>

#include <endpoint/BoardSupport.h>

namespace GHIElectronics::Endpoint::Drivers::RaspberryPi {

namespace detail {

constexpr const char* PinmuxPath   = "/usr/sbin/endpoint-pinmux";
constexpr const char* ModprobePath = "/sbin/modprobe";

// fork+exec `path args...`; capture stdout (if wanted) and stderr. Returns
// child's exit code; -1 on fork/exec failure.
inline int RunExec(const char* path, const std::vector<std::string>& args,
                   std::string* stdoutOut, std::string* stderrOut) {
    int outPipe[2] = {-1, -1};
    int errPipe[2] = {-1, -1};
    if (pipe(outPipe) < 0) return -1;
    if (pipe(errPipe) < 0) { close(outPipe[0]); close(outPipe[1]); return -1; }

    pid_t child = fork();
    if (child < 0) {
        close(outPipe[0]); close(outPipe[1]);
        close(errPipe[0]); close(errPipe[1]);
        return -1;
    }
    if (child == 0) {
        dup2(outPipe[1], STDOUT_FILENO);
        dup2(errPipe[1], STDERR_FILENO);
        close(outPipe[0]); close(outPipe[1]);
        close(errPipe[0]); close(errPipe[1]);
        std::vector<char*> argv;
        argv.push_back(const_cast<char*>(path));
        for (const auto& a : args) argv.push_back(const_cast<char*>(a.c_str()));
        argv.push_back(nullptr);
        execv(path, argv.data());
        _exit(127);
    }
    close(outPipe[1]); close(errPipe[1]);
    auto drain = [](int fd, std::string* dst) {
        char buf[256]; ssize_t n;
        while ((n = read(fd, buf, sizeof buf)) > 0)
            if (dst) dst->append(buf, static_cast<size_t>(n));
    };
    drain(outPipe[0], stdoutOut);
    drain(errPipe[0], stderrOut);
    close(outPipe[0]); close(errPipe[0]);
    int status = 0;
    while (waitpid(child, &status, 0) < 0 && errno == EINTR) {}
    if (WIFEXITED(status)) return WEXITSTATUS(status);
    if (WIFSIGNALED(status)) return 128 + WTERMSIG(status);
    return -1;
}

inline std::string Rtrim(std::string s) {
    while (!s.empty() && (s.back() == '\n' || s.back() == '\r' || s.back() == ' ' || s.back() == '\t'))
        s.pop_back();
    return s;
}

inline std::string Join(const char* path, const std::vector<std::string>& args) {
    std::string s = path;
    for (const auto& a : args) { s += ' '; s += a; }
    return s;
}

// Fork+exec wrapper around /usr/sbin/endpoint-pinmux + /sbin/modprobe.
struct Pinmux {
    static void Set(int pin, uint32_t function) {
        char funcBuf[16];
        std::snprintf(funcBuf, sizeof funcBuf, "0x%X", function);
        std::vector<std::string> args{"set", std::to_string(pin), funcBuf};
        std::string err;
        int code = RunExec(PinmuxPath, args, nullptr, &err);
        if (code != 0)
            throw std::runtime_error(Join(PinmuxPath, args) + " failed (exit " +
                                     std::to_string(code) + "): " + Rtrim(err));
    }

    static uint32_t Get(int pin) {
        std::vector<std::string> args{"get", std::to_string(pin)};
        std::string out, err;
        int code = RunExec(PinmuxPath, args, &out, &err);
        if (code != 0)
            throw std::runtime_error(Join(PinmuxPath, args) + " failed (exit " +
                                     std::to_string(code) + "): " + Rtrim(err));
        constexpr const char* key = "function=";
        auto i = out.find(key);
        if (i == std::string::npos)
            throw std::runtime_error("endpoint-pinmux get " + std::to_string(pin) +
                                     ": unexpected output '" + Rtrim(out) + "'");
        auto start = i + std::strlen(key);
        auto end = start;
        while (end < out.size() && out[end] != ' ' && out[end] != '\t' && out[end] != '\n' && out[end] != '\r')
            ++end;
        std::string value = out.substr(start, end - start);
        try {
            return static_cast<uint32_t>(std::stoul(value, nullptr, 0));
        } catch (const std::exception&) {
            throw std::runtime_error("endpoint-pinmux get " + std::to_string(pin) +
                                     ": cannot parse function '" + value + "'");
        }
    }

    static void Modprobe(const std::string& module) {
        std::vector<std::string> args{module};
        std::string err;
        int code = RunExec(ModprobePath, args, nullptr, &err);
        if (code != 0)
            throw std::runtime_error(Join(ModprobePath, args) + " failed (exit " +
                                     std::to_string(code) + "): " + Rtrim(err));
    }
};

}  // namespace detail

// Zero2W: Raspberry Pi Zero 2 W (BCM2710). Pin / bus / port / channel
// constants only. The chip-neutral verbs are in
// GHIElectronics::Endpoint::Core (I2cController etc.).
struct Zero2W {
    // BCM GPIO pin constants. All numbers are BCM GPIO numbers (not
    // physical header pin numbers).
    struct Gpio {
        static constexpr int Led = 29;  // on-board ACT LED, active-low
        static constexpr int Pin0  = 0,  Pin1  = 1,  Pin2  = 2,  Pin3  = 3;
        static constexpr int Pin4  = 4,  Pin5  = 5,  Pin6  = 6,  Pin7  = 7;
        static constexpr int Pin8  = 8,  Pin9  = 9,  Pin10 = 10, Pin11 = 11;
        static constexpr int Pin12 = 12, Pin13 = 13, Pin14 = 14, Pin15 = 15;
        static constexpr int Pin16 = 16, Pin17 = 17, Pin18 = 18, Pin19 = 19;
        static constexpr int Pin20 = 20, Pin21 = 21, Pin22 = 22, Pin23 = 23;
        static constexpr int Pin24 = 24, Pin25 = 25, Pin26 = 26, Pin27 = 27;
        static constexpr int GpioChip = 0;  // /dev/gpiochip0
    };

    // I2C: SDA on GPIO 2 (pin 3), SCL on GPIO 3 (pin 5), ALT0. /dev/i2c-1.
    struct I2c {
        static constexpr int I2c1 = 1;
        static constexpr int DefaultFrequency = 400'000;
    };

    // SPI: MOSI=GPIO10, MISO=GPIO9, SCLK=GPIO11, CE0=GPIO8. All ALT0.
    // /dev/spidev0.0.
    struct Spi {
        static constexpr int Spi0 = 0;
        static constexpr int DefaultBufferSize = 4096;
    };

    // PWM: pwmchip0, two channels. Each pin uniquely identifies (chip, ch, alt).
    struct Pwm {
        struct Pin {
            static constexpr int Pin12 = 12;  // chip 0 ch 0 ALT0 (header pin 32)
            static constexpr int Pin13 = 13;  // chip 0 ch 1 ALT0 (header pin 33)
            static constexpr int Pin18 = 18;  // chip 0 ch 0 ALT5 (header pin 12)
            static constexpr int Pin19 = 19;  // chip 0 ch 1 ALT5 (header pin 35)
        };
    };

    // Mini-UART on GPIO 14 (TX) / GPIO 15 (RX), ALT5. /dev/ttyS0.
    struct SerialPort {
        static inline const std::string Uart1 = "/dev/ttyS0";
    };
};

// Zero2W implementation of IBoardSupport. Overrides only the peripherals
// Pi actually implements; everything else keeps UnknownBoardSupport's
// throwing default.
struct Zero2WBoardSupport : GHIElectronics::Endpoint::Core::UnknownBoardSupport {
    const std::string& Name() const override {
        static const std::string n = "Zero2W";
        return n;
    }

    // ---------- GPIO ----------
    // Pi exposes every BCM GPIO on a single /dev/gpiochip0 (labelled
    // pinctrl-bcm2835) with the BCM pin number matching the line number.
    // Overriding these bypasses the Endpoint-boards' gpio<letter> label
    // lookup, which would fail on the Pi's kernel.
    int GpioChipId(int /*pin*/) override { return 0; }
    int GpioLineId(int pin) override     { return pin; }

    // ---------- I2C ----------
    void I2cInitialize(int bus, int /*frequency*/) override {
        if (bus != Zero2W::I2c::I2c1)
            throw std::runtime_error("Pi Zero 2 W exposes only I2c1 (/dev/i2c-1).");
        saved[2] = detail::Pinmux::Get(2);
        saved[3] = detail::Pinmux::Get(3);
        detail::Pinmux::Modprobe("i2c-bcm2835");
        detail::Pinmux::Modprobe("i2c-dev");
        detail::Pinmux::Set(2, 4);  // ALT0 = SDA1
        detail::Pinmux::Set(3, 4);  // ALT0 = SCL1
    }
    void I2cUnInitialize(int /*bus*/) override {
        Restore(2); Restore(3);
    }
    int I2cGetFrequency(int /*bus*/) override { return Zero2W::I2c::DefaultFrequency; }

    // ---------- SPI ----------
    void SpiInitialize(int bus, int /*bufferSize*/) override {
        if (bus != Zero2W::Spi::Spi0)
            throw std::runtime_error("Pi Zero 2 W exposes only Spi0 (/dev/spidev0.0).");
        for (int p : {10, 9, 11, 8}) saved[p] = detail::Pinmux::Get(p);
        detail::Pinmux::Modprobe("spi-bcm2835");
        detail::Pinmux::Modprobe("spidev");
        for (int p : {10, 9, 11, 8}) detail::Pinmux::Set(p, 4);
    }
    void SpiUnInitialize(int /*bus*/) override {
        for (int p : {10, 9, 11, 8}) Restore(p);
    }

    // ---------- PWM ----------
    void PwmInitialize(int pin) override {
        auto [chip, ch, alt] = PwmLookup(pin);
        (void)chip; (void)ch;
        saved[pin] = detail::Pinmux::Get(pin);
        detail::Pinmux::Modprobe("pwm-bcm2835");
        detail::Pinmux::Set(pin, alt);
    }
    void PwmUnInitialize(int pin) override {
        (void)PwmLookup(pin);
        Restore(pin);
    }
    int PwmGetChipId(int pin) override    { return std::get<0>(PwmLookup(pin)); }
    int PwmGetChannelId(int pin) override { return std::get<1>(PwmLookup(pin)); }

    // ---------- SerialPort ----------
    void SerialPortInitialize(const std::string& portName, bool /*hwFlow*/) override {
        if (portName != Zero2W::SerialPort::Uart1)
            throw std::runtime_error("Pi Zero 2 W exposes only Uart1 (/dev/ttyS0).");
        saved[14] = detail::Pinmux::Get(14);
        saved[15] = detail::Pinmux::Get(15);
        detail::Pinmux::Set(14, 2);  // ALT5 = TXD1 (mini-UART)
        detail::Pinmux::Set(15, 2);  // ALT5 = RXD1
    }
    void SerialPortUnInitialize(const std::string& /*portName*/) override {
        Restore(14); Restore(15);
    }

private:
    std::map<int, uint32_t> saved;

    void Restore(int pin) {
        auto it = saved.find(pin);
        if (it != saved.end()) { detail::Pinmux::Set(pin, it->second); saved.erase(it); }
    }

    static std::tuple<int, int, uint32_t> PwmLookup(int pin) {
        switch (pin) {
            case 12: return {0, 0, 4};  // ALT0 chip0 ch0
            case 13: return {0, 1, 4};  // ALT0 chip0 ch1
            case 18: return {0, 0, 2};  // ALT5 chip0 ch0 (alt pin)
            case 19: return {0, 1, 2};  // ALT5 chip0 ch1 (alt pin)
        }
        throw std::runtime_error("Pi Zero 2 W PWM pins are 12, 13, 18, 19 — got " + std::to_string(pin));
    }
};

// Register Zero2WBoardSupport at include time, but only if ChipDetect
// doesn't already identify an Endpoint chip. On an Endpoint board,
// libendpoint's BoardSupportInit.cpp registered the right board earlier;
// Zero2WBoardSupport should not fight that.
//
// ENDPOINT_ZERO2W_NO_AUTO_REGISTER: if defined before including this header,
// the inline self-register below is suppressed. The .so build
// (endpoint-drivers/cpp/.../RaspberryPi/.../native/libendpoint-zero2w.cpp)
// defines it so the shared-library's __attribute__((constructor)) is the
// only thing that registers the board. Needed because the inline path
// calls libendpoint's C++ BoardSupport::Register (which is header-only
// inline, so each .so gets its OWN copy of the registry slot); across .so
// boundaries that would write into the driver's private slot instead of
// libendpoint's real one. The .cpp uses extern "C"
// endpoint_register_board_support instead, which is the ONE function
// libendpoint.so exports, always touching the ONE real slot inside it.
// Single-file C/C++ users link libendpoint statically into their binary
// so the inline path still wins there with no duplication.
#ifndef ENDPOINT_ZERO2W_NO_AUTO_REGISTER
namespace detail {
    inline int RegisterIfPi() {
        if (GHIElectronics::Endpoint::Core::ChipDetect::Current() == GHIElectronics::Endpoint::Core::Chip::Unknown) {
            GHIElectronics::Endpoint::Core::BoardSupport::Register(new Zero2WBoardSupport());
        }
        return 0;
    }
    inline const int _pi_registered = RegisterIfPi();
}
#endif

}  // namespace GHIElectronics::Endpoint::Drivers::RaspberryPi
