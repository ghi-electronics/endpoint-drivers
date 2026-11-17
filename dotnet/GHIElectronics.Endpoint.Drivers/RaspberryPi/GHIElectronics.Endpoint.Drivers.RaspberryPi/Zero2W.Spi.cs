// Pi Zero 2 W SPI support. Pay-for-use: the four SPI0 pins (MOSI/MISO/SCLK/CE0)
// stay free until Initialize(bus); UnInitialize restores them.
//
// Usage:
//     Zero2W.Spi.Initialize(Zero2W.Spi.Spi0);
//     using var spi = SpiDevice.Create(new SpiConnectionSettings(0, 0) {
//         ClockFrequency = 1_000_000 });
//     spi.TransferFullDuplex(tx, rx);
//     Zero2W.Spi.UnInitialize(Zero2W.Spi.Spi0);

namespace GHIElectronics.Endpoint.Drivers.RaspberryPi;

public static partial class Zero2W
{
    public static class Spi
    {
        /// <summary>One SPI controller: four pins + their ALT codes + the spidev bus id.</summary>
        public record Bus(
            string Name,
            int MosiPin, int MosiFunction,
            int MisoPin, int MisoFunction,
            int SclkPin, int SclkFunction,
            int Ce0Pin,  int Ce0Function,
            int BusId);

        /// <summary>Pi user SPI — MOSI=GPIO10 (pin 19), MISO=GPIO9 (pin 21),
        /// SCLK=GPIO11 (pin 23), CE0=GPIO8 (pin 24). All ALT0 (function 4).
        /// Maps to /dev/spidev0.0 (bus 0, chip-select 0).</summary>
        public static readonly Bus Spi0 = new(
            "spi0",
            MosiPin: 10, MosiFunction: 4,
            MisoPin: 9,  MisoFunction: 4,
            SclkPin: 11, SclkFunction: 4,
            Ce0Pin:  8,  Ce0Function:  4,
            BusId:   0);

        private const string Controller = "spi-bcm2835";
        private const string CharDev = "spidev";

        public static void Initialize(Bus bus)
        {
            // Save before modprobe so pre-Init state is captured before any
            // auto-mux the kernel driver might apply. See Zero2W.I2c.Initialize.
            Pinmux.Save(bus.MosiPin);
            Pinmux.Save(bus.MisoPin);
            Pinmux.Save(bus.SclkPin);
            Pinmux.Save(bus.Ce0Pin);
            Pinmux.Modprobe(Controller);
            Pinmux.Modprobe(CharDev);
            Pinmux.Set(bus.MosiPin, bus.MosiFunction);
            Pinmux.Set(bus.MisoPin, bus.MisoFunction);
            Pinmux.Set(bus.SclkPin, bus.SclkFunction);
            Pinmux.Set(bus.Ce0Pin,  bus.Ce0Function);
        }

        public static void UnInitialize(Bus bus)
        {
            Pinmux.Restore(bus.MosiPin);
            Pinmux.Restore(bus.MisoPin);
            Pinmux.Restore(bus.SclkPin);
            Pinmux.Restore(bus.Ce0Pin);
        }
    }
}
