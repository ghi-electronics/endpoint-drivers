// Pi Zero 2 W SPI bus constants. The chip-neutral verb
// (GHIElectronics.Endpoint.Core.SpiController.Initialize) takes the bus id
// below; Zero2WBoardSupport maps it to MOSI/MISO/SCLK/CE0 pins + ALT mux
// + modprobe at runtime.
//
// Usage:
//     using GHIElectronics.Endpoint.Core;
//     using GHIElectronics.Endpoint.Drivers.RaspberryPi;
//
//     SpiController.Initialize(Zero2W.Spi.Spi0);
//     using var spi = SpiDevice.Create(new SpiConnectionSettings(Zero2W.Spi.Spi0, 0) {
//         ClockFrequency = 1_000_000 });
//     spi.TransferFullDuplex(tx, rx);
//     SpiController.UnInitialize(Zero2W.Spi.Spi0);

namespace GHIElectronics.Endpoint.Drivers.RaspberryPi;

public static partial class Zero2W
{
    public static class Spi
    {
        /// <summary>Pi user SPI — MOSI=GPIO10 (pin 19), MISO=GPIO9 (pin 21), SCLK=GPIO11 (pin 23), CE0=GPIO8 (pin 24). All ALT0. Maps to /dev/spidev0.0.</summary>
        // static readonly (not const) so the reference forces the Drivers.RaspberryPi
        // assembly to load, which runs [ModuleInitializer] and registers Zero2WBoardSupport.
        public static readonly int Spi0 = 0;

        /// <summary>Default SPI transfer buffer size.</summary>
        public static readonly int DefaultBufferSize = 4096;
    }
}
