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
        {
            // Fire the native .so load first. libendpoint's C++
            // BoardSupport::Current() needs Zero2W too for any peripheral
            // dispatch that goes through libendpoint (GPIO today in the
            // Python path; .NET controllers are future-proofed). Safe no-op
            // if the .so is missing (dev PC, non-Pi RID).
            Zero2WNativeLoader.ForceLoad();
            BoardSupport.Register(new Zero2WBoardSupport());
        }
    }
}
