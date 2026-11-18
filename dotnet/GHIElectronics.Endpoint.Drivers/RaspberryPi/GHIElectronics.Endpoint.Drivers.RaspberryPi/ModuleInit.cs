// Runs at assembly load. If ChipDetect doesn't identify an Endpoint chip
// (STM32MP15x, MA35D0, MA35D1), assume we're on a Raspberry Pi Zero 2 W
// and register Zero2WBoardSupport. Mirrors Endpoint.Pins' ModuleInit.

using System.Runtime.CompilerServices;
using GHIElectronics.Endpoint.Core;
using GHIElectronics.Endpoint.Core.Internal;

namespace GHIElectronics.Endpoint.Drivers.RaspberryPi;

internal static class ModuleInit
{
    [ModuleInitializer]
    internal static void Init()
    {
        // Only register when nothing more specific did. On an Endpoint
        // board (Chip.Epm815 / Epn256 / Epn128), the matching Pins package
        // already registered its BoardSupport earlier in the same process;
        // Zero2WBoardSupport should not fight that.
        if (ChipDetect.Current == Chip.Unknown)
            BoardSupport.Register(new Zero2WBoardSupport());
    }
}
