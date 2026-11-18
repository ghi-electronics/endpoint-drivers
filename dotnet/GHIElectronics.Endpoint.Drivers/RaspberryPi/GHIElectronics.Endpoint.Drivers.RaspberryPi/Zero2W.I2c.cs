// Pi Zero 2 W I2C support. Pay-for-use: pins stay free until Initialize(bus)
// muxes them; UnInitialize(bus) restores whatever state they were in before.
//
// Usage:
//     Zero2W.I2c.Initialize(Zero2W.I2c.I2c1);
//     using var dev = I2cDevice.Create(new I2cConnectionSettings(1, 0x50));
//     dev.Read(buffer);
//     Zero2W.I2c.UnInitialize(Zero2W.I2c.I2c1);
//
// The firmware image's endpoint-i2c1 dtoverlay enables /dev/i2c-1 without
// applying pinctrl, so SDA (GPIO 2) and SCL (GPIO 3) stay unmapped until
// Initialize shells out to /usr/sbin/endpoint-pinmux to mux them to ALT0.

using System.Collections.Generic;

namespace GHIElectronics.Endpoint.Drivers.RaspberryPi;

public static partial class Zero2W
{
    public static class I2c
    {
        /// <summary>One I2C controller: pins, their ALT codes, and the kernel bus id.</summary>
        public record Bus(string Name, int SdaPin, int SdaFunction, int SclPin, int SclFunction, int BusId);

        /// <summary>Pi user I2C — SDA on GPIO 2 (pin 3), SCL on GPIO 3 (pin 5), ALT0, /dev/i2c-1.</summary>
        public static readonly Bus I2c1 = new("i2c1", SdaPin: 2, SdaFunction: 4, SclPin: 3, SclFunction: 4, BusId: 1);

        private const string KernelModule = "i2c-bcm2835";
        private const string DevModule = "i2c-dev";

        // Pre-Initialize pinmux state, remembered so UnInitialize can restore it
        // in the same process. Keyed on BCM GPIO number.
        private static readonly Dictionary<int, uint> _saved = new();

        /// <summary>Load the kernel driver and mux the bus's SDA + SCL pins to I2C.
        /// Idempotent; safe to call again. Pair with <see cref="UnInitialize"/>.</summary>
        public static void Initialize(Bus bus)
        {
            // Snapshot BEFORE modprobe: the kernel i2c-bcm2835 bind may mux
            // SDA/SCL on its own (pinctrl-framework auto-default), which would
            // make "pre-Initialize state" look like ALT0 instead of Input.
            // Capture the user's real pre-Init state first so UnInitialize
            // restores to that.
            _saved[bus.SdaPin] = Pinmux.Get(bus.SdaPin);
            _saved[bus.SclPin] = Pinmux.Get(bus.SclPin);
            Pinmux.Modprobe(KernelModule);
            Pinmux.Modprobe(DevModule);
            Pinmux.Set(bus.SdaPin, (uint)bus.SdaFunction);
            Pinmux.Set(bus.SclPin, (uint)bus.SclFunction);
        }

        /// <summary>Restore SDA + SCL to the state they were in before Initialize.</summary>
        public static void UnInitialize(Bus bus)
        {
            if (_saved.Remove(bus.SdaPin, out var sda)) Pinmux.Set(bus.SdaPin, sda);
            if (_saved.Remove(bus.SclPin, out var scl)) Pinmux.Set(bus.SclPin, scl);
        }
    }
}
