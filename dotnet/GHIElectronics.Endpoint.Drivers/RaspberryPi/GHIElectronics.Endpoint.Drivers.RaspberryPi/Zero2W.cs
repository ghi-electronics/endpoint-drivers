// Raspberry Pi Zero 2 W board support. Thin layer on top of standard .NET IoT
// (System.Device.Gpio). Intentionally does not depend on GHIElectronics.Endpoint.Core
// so this package is usable on a plain Pi image without Endpoint-chip assumptions.
//
// Usage:
//     using System.Device.Gpio;
//     using GHIElectronics.Endpoint.Drivers.RaspberryPi;
//
//     using var gpio = new GpioController(new Zero2W.Gpio.Driver());
//     gpio.OpenPin(Zero2W.Led, PinMode.Output);
//     gpio.Write(Zero2W.Led, PinValue.Low);   // LED ON  (active-low)
//     gpio.Write(Zero2W.Led, PinValue.High);  // LED OFF
//
// The 40-pin header pins are plain BCM GPIO numbers: pass 25 for header pin 22,
// 17 for pin 11, etc. See https://pinout.xyz for the full map.

namespace GHIElectronics.Endpoint.Drivers.RaspberryPi;

/// <summary>Raspberry Pi Zero 2 W (BCM2710 SoC, Cortex-A53 quad, 512 MB).</summary>
public static partial class Zero2W
{
    /// <summary>On-board green ACT LED (BCM GPIO 29, active-low).
    /// The Endpoint Pi image's /etc/init.d/S29actled releases this pin from the
    /// kernel LED driver at boot so <see cref="System.Device.Gpio.GpioController"/>
    /// can claim it like any other pin.</summary>
    public const int Led = 29;
}
