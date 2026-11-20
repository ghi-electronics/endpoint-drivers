// libendpoint-zero2w.so: the Pi Zero 2 W board support, delivered as a
// loadable shared library so Python and .NET can dlopen it and get libendpoint
// to see the Pi as the active board. Same Zero2WBoardSupport class the
// header-only Zero2W.h ships to single-file C/C++ users; the only difference
// is HOW the registration fires:
//
//   * C/C++ user: #include <endpoint/Zero2W.h> in their main.cpp. The header's
//     inline _pi_registered initializer runs at program start. Since
//     libendpoint.a is statically linked into the user's binary, both
//     BoardSupport::Register (inline C++) and Zero2WBoardSupport resolve
//     inside one monolithic image and the inline path works.
//
//   * Python: GHIElectronics.Endpoint.Drivers.RaspberryPi/__init__.py does
//     ctypes.CDLL("libendpoint-zero2w-<arch>.so"). dlopen fires the
//     __attribute__((constructor)) below. The constructor looks up
//     endpoint_register_board_support in libendpoint.so (via dlsym, since
//     libendpoint was loaded earlier by GHIElectronics.Endpoint.Core's own
//     native loader with RTLD_GLOBAL) and calls it. Registration lands in
//     libendpoint.so's REAL slot, which Python's GPIO dispatch reads.
//
//   * .NET: Drivers.RaspberryPi's type initializer touches a DllImport to
//     "endpoint-zero2w", forcing the CLR to dlopen the .so. Same constructor
//     path as Python. CLR's P/Invoke dlopen uses RTLD_GLOBAL by default so
//     endpoint_register_board_support is reachable.
//
// We cannot use Zero2W.h's inline self-register path from this .cpp because
// BoardSupport::Register is header-only inline C++. If the inline fired here
// it would operate on libendpoint-zero2w.so's OWN copy of the registry slot,
// not libendpoint.so's real one, and nothing would change for the user.
// ENDPOINT_ZERO2W_NO_AUTO_REGISTER suppresses the inline path so this file
// has sole responsibility for the registration via the C ABI entry point.

#define ENDPOINT_ZERO2W_NO_AUTO_REGISTER

#include "../Zero2W.h"

#include <endpoint/Core.h>

#include <dlfcn.h>

extern "C" __attribute__((constructor))
void zero2w_register(void) {
    using namespace GHIElectronics::Endpoint::Core;
    if (ChipDetect::Current() != Chip::Unknown) {
        // On an Endpoint chip (EPM815, EPN256, EPN128): libendpoint's own
        // BoardSupportInit.cpp already registered the matching board at its
        // own load time. Do not fight it.
        return;
    }

    using RegisterFn = void(*)(void*);
    auto reg = reinterpret_cast<RegisterFn>(
        dlsym(RTLD_DEFAULT, "endpoint_register_board_support"));
    if (!reg) {
        // libendpoint.so not loaded with RTLD_GLOBAL, or not loaded at all.
        // The caller forgot to load Core first. Bail silently; the user's
        // peripheral calls will still get libendpoint's "Unknown board" throw
        // which points at exactly this situation ("import the driver
        // package").
        return;
    }

    reg(new GHIElectronics::Endpoint::Drivers::RaspberryPi::Zero2WBoardSupport());
}

// Touch symbol so .NET's DllImport (which looks up a named entry point to
// force the CLR to dlopen this .so) has something to resolve. The call is
// a no-op; the real work happened in zero2w_register above when dlopen ran.
extern "C" __attribute__((visibility("default")))
void endpoint_zero2w_touch(void) { }
