// Zero2WBoardSupport : UnknownBoardSupport
//
// Pi Zero 2 W implementation of Core's IBoardSupport. Only overrides the
// peripheral methods that the Pi actually implements (I2C, SPI, PWM,
// SerialPort via endpoint-pinmux + modprobe); everything else inherits
// the "not supported on this board" throw from UnknownBoardSupport (CAN,
// DAC, Watchdog, full ADC table, STM32-style pin-mux primitives).
//
// Registered at startup by ModuleInit when ChipDetect doesn't identify
// an Endpoint chip. Pi driver is the only one that depends on Core via
// this file; user code calls Core's I2cController / SpiController etc.
// as if it were on any Endpoint board.

#nullable enable
using System.Collections.Generic;
using GHIElectronics.Endpoint.Core;

namespace GHIElectronics.Endpoint.Drivers.RaspberryPi;

internal sealed class Zero2WBoardSupport : UnknownBoardSupport
{
    public override string Name => "Zero2W";

    // GPIO: single gpiochip0 covers all 54 BCM lines (0..53). Pins 0..27
    // are the ones on the 40-pin header; 28..53 are SoC-internal (like
    // GPIO 29 = ACT LED).
    public override int MaxPort => 0;
    public override string GpioChipLabel(int port) => "gpiochip0";
    public override int GpioChipId(int pin) => 0;
    public override int GpioLineId(int pin) => pin;
    public override string GpioName(int pin) => $"GPIO{pin}";

    // Shared per-pin pre-Initialize state so UnInitialize restores exactly
    // what was there before, even across peripherals that happen to share a
    // pin in some future config.
    private readonly Dictionary<int, uint> saved = new();

    // ---------- I2C ----------

    public override void I2cInitialize(int bus, int frequency)
    {
        if (bus != Zero2W.I2c.I2c1)
            throw new System.ArgumentOutOfRangeException(
                nameof(bus), bus, "Pi Zero 2 W exposes only I2c1 (/dev/i2c-1).");

        // Snapshot BEFORE modprobe: the kernel i2c-bcm2835 bind may mux
        // SDA/SCL on its own (pinctrl-framework auto-default), which would
        // make "pre-Initialize state" look like ALT0 instead of Input.
        this.saved[2] = Pinmux.Get(2);  // SDA
        this.saved[3] = Pinmux.Get(3);  // SCL
        Pinmux.Modprobe("i2c-bcm2835");
        Pinmux.Modprobe("i2c-dev");
        Pinmux.Set(2, 4);  // ALT0 = SDA1
        Pinmux.Set(3, 4);  // ALT0 = SCL1
    }

    public override void I2cUnInitialize(int bus)
    {
        if (this.saved.Remove(2, out var sda)) Pinmux.Set(2, sda);
        if (this.saved.Remove(3, out var scl)) Pinmux.Set(3, scl);
    }

    public override int I2cGetFrequency(int bus) => Zero2W.I2c.DefaultFrequency;

    // ---------- SPI ----------

    public override void SpiInitialize(int bus, int bufferSize)
    {
        if (bus != Zero2W.Spi.Spi0)
            throw new System.ArgumentOutOfRangeException(
                nameof(bus), bus, "Pi Zero 2 W exposes only Spi0 (/dev/spidev0.0).");

        // GPIO 10=MOSI, 9=MISO, 11=SCLK, 8=CE0; all ALT0
        foreach (var p in new[] { 10, 9, 11, 8 }) this.saved[p] = Pinmux.Get(p);
        Pinmux.Modprobe("spi-bcm2835");
        Pinmux.Modprobe("spidev");
        foreach (var p in new[] { 10, 9, 11, 8 }) Pinmux.Set(p, 4);
    }

    public override void SpiUnInitialize(int bus)
    {
        foreach (var p in new[] { 10, 9, 11, 8 })
            if (this.saved.Remove(p, out var v)) Pinmux.Set(p, v);
    }

    // ---------- PWM ----------

    private static (int Chip, int Channel, uint Alt) PwmLookup(int pin) => pin switch
    {
        12 => (0, 0, 4),  // ALT0 = PWM0 ch0
        13 => (0, 1, 4),  // ALT0 = PWM0 ch1
        18 => (0, 0, 2),  // ALT5 = PWM0 ch0 (alt pin)
        19 => (0, 1, 2),  // ALT5 = PWM0 ch1 (alt pin)
        _ => throw new System.ArgumentOutOfRangeException(
            nameof(pin), pin, "Pi Zero 2 W PWM pins are 12, 13, 18, or 19."),
    };

    public override void PwmInitialize(int pin)
    {
        var (_, _, alt) = PwmLookup(pin);
        this.saved[pin] = Pinmux.Get(pin);
        Pinmux.Modprobe("pwm-bcm2835");
        Pinmux.Set(pin, alt);
    }

    public override void PwmUnInitialize(int pin)
    {
        _ = PwmLookup(pin);  // validate
        if (this.saved.Remove(pin, out var v)) Pinmux.Set(pin, v);
    }

    public override int PwmGetChipId(int pin) => PwmLookup(pin).Chip;
    public override int PwmGetChannelId(int pin) => PwmLookup(pin).Channel;

    // ---------- SerialPort ----------

    public override void SerialPortInitialize(string portName, bool enableHardwareFlowControl)
    {
        if (portName != Zero2W.SerialPort.Uart1)
            throw new System.ArgumentOutOfRangeException(
                nameof(portName), portName, "Pi Zero 2 W exposes only Uart1 (/dev/ttyS0).");
        // GPIO 14=TXD1 (mini-UART), GPIO 15=RXD1; both ALT5 (function code 2)
        this.saved[14] = Pinmux.Get(14);
        this.saved[15] = Pinmux.Get(15);
        Pinmux.Set(14, 2);
        Pinmux.Set(15, 2);
    }

    public override void SerialPortUnInitialize(string portName)
    {
        if (this.saved.Remove(14, out var tx)) Pinmux.Set(14, tx);
        if (this.saved.Remove(15, out var rx)) Pinmux.Set(15, rx);
    }
}
