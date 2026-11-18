// Pi Zero 2 W serial port (UART) constants. The chip-neutral verb
// (GHIElectronics.Endpoint.Core.SerialPortController.Initialize) takes the
// port path string; Zero2WBoardSupport maps it to GPIO 14/15 ALT5 mux at
// runtime.
//
// Usage:
//     using GHIElectronics.Endpoint.Core;
//     using GHIElectronics.Endpoint.Drivers.RaspberryPi;
//
//     SerialPortController.Initialize(Zero2W.SerialPort.Uart1);
//     using var port = new System.IO.Ports.SerialPort(Zero2W.SerialPort.Uart1, 115200);
//     port.Open();
//     port.WriteLine("hello");
//     SerialPortController.UnInitialize(Zero2W.SerialPort.Uart1);
//
// Mini-UART (uart1) on GPIO 14 (TX, ALT5) / GPIO 15 (RX, ALT5). The Pi
// firmware image keeps PL011 for the on-board Bluetooth so the mini-UART
// owns the header pins. enable_uart=1 pins the core clock so baud stays
// stable. No serial console on /dev/ttyS0 (handled by cmdline.txt).

namespace GHIElectronics.Endpoint.Drivers.RaspberryPi;

public static partial class Zero2W
{
    public static class SerialPort
    {
        /// <summary>Mini-UART on GPIO 14 (TX) / GPIO 15 (RX), ALT5. Maps to /dev/ttyS0.</summary>
        // static readonly (not const) so the reference forces the Drivers.RaspberryPi
        // assembly to load, which runs [ModuleInitializer] and registers Zero2WBoardSupport.
        public static readonly string Uart1 = "/dev/ttyS0";
    }
}
