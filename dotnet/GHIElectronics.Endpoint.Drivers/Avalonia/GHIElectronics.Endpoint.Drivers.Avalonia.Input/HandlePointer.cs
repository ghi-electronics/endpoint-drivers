using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Input.Raw;
using Avalonia.Input;
using Avalonia;

namespace GHIElectronics.Endpoint.Drivers.Avalonia.Input {
    public partial class InputDevice {

        //private void HandlePointer(LibInputEventType type) {
        //    var mousePosition = new Point(this.TouchX, this.TouchY);

        //    var modifiers = RawInputModifiers.None; 


        //    var ts = (ulong)(DateTime.Now.Ticks / 10000);

        //    var button = EvKey.BTN_LEFT;
        //    var buttonState = (type == LibInputEventType.LIBINPUT_EVENT_TOUCH_DOWN) ? 1 : 0;

            

        //    RawPointerEventArgs? value = button switch {
        //        EvKey.BTN_LEFT when buttonState == 1
        //            => new(this.mouse, ts, this.inputRoot, RawPointerEventType.LeftButtonDown, mousePosition, modifiers),
        //        EvKey.BTN_LEFT when buttonState == 0
        //            => new(this.mouse, ts, this.inputRoot, RawPointerEventType.LeftButtonUp,mousePosition, modifiers),
        //        EvKey.BTN_RIGHT when buttonState == 1
        //            => new(this.mouse, ts, this.inputRoot, RawPointerEventType.RightButtonUp, mousePosition, modifiers),
        //        EvKey.BTN_RIGHT when buttonState == 2
        //            => new(this.mouse, ts, this.inputRoot, RawPointerEventType.RightButtonDown, mousePosition, modifiers),
        //        EvKey.BTN_MIDDLE when buttonState == 1
        //            => new(this.mouse, ts, this.inputRoot, RawPointerEventType.MiddleButtonDown, mousePosition, modifiers),
        //        EvKey.BTN_MIDDLE when buttonState == 2
        //            => new(this.mouse, ts, this.inputRoot, RawPointerEventType.MiddleButtonUp, mousePosition, modifiers),
        //        EvKey.BTN_TOUCH => throw new NotImplementedException(),
        //        _ => default,
        //    };

        //    if (value is not null) {
        //        this.ScheduleInput(value);
        //    }



        //}
    }
}
