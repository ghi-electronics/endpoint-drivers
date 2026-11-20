// Forces the CLR to dlopen libendpoint-zero2w.so so its
// __attribute__((constructor)) fires and registers Zero2WBoardSupport with
// libendpoint's C++ BoardSupport::Current().
//
// Needed so Pi GPIO works from .NET for any code path that eventually reaches
// libendpoint. .NET's chip-neutral controllers (I2cController.Initialize etc.)
// currently route through the .NET-side BoardSupport (Zero2WBoardSupport.cs),
// and .NET's Pi GPIO today uses the custom Zero2W.Gpio.Driver that bypasses
// libendpoint, so loading the native .so is future-proofing: if a later
// release routes .NET peripherals through libendpoint uniformly (matching
// Python and C++), everything already works with no further change.
//
// The native .so is shipped per-RID in the nupkg's runtimes/<rid>/native/
// folder (see the csproj's None Include entries). `dotnet publish -r
// linux-arm[64]` picks the matching one and places it next to the app.

using System.Runtime.InteropServices;

namespace GHIElectronics.Endpoint.Drivers.RaspberryPi;

internal static class Zero2WNativeLoader
{
    // The CLR translates "endpoint-zero2w" into "libendpoint-zero2w.so" on
    // Linux and runs dlopen on it; that dlopen fires the .so's constructor
    // which calls endpoint_register_board_support in libendpoint.so. The
    // body of this function is deliberately empty; the side effect is in
    // dlopen, not in the call itself.
    [DllImport("endpoint-zero2w")]
    private static extern void endpoint_zero2w_touch();

    // Called from ModuleInit so the first use of anything in this assembly
    // triggers the native load. Any failure (no .so for this RID, running
    // on dev PC) is swallowed: Python-side peripheral dispatch still works
    // and .NET GPIO on Pi has its own non-libendpoint path too.
    public static void ForceLoad()
    {
        try { endpoint_zero2w_touch(); }
        catch { /* missing .so or dev PC; non-fatal */ }
    }
}
