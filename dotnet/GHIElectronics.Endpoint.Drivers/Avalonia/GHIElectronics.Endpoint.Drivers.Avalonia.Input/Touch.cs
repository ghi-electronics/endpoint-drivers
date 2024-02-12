using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Input.Raw;
using Avalonia.Input;
using Avalonia;

namespace GHIElectronics.Endpoint.Drivers.Avalonia.Input {
    
    public class Touch {

        
        private IInputRoot inputRoot;
        private Action<RawInputEventArgs> onInput;

        private MouseDevice mouseDevice;
        public Touch() {
            this.mouseDevice = new MouseDevice();
        }

        public void Initialize(Action<RawInputEventArgs> onInput) {
            
            this.onInput = onInput;
        }

        public void SetInputRoot(IInputRoot root) {
            this.inputRoot = root;
        }


        private void ScheduleInput(RawInputEventArgs ev) => this.onInput.Invoke(ev);
        internal void HandlePointer(TouchPoint touchpoint) {
            var mousePosition = new Point(touchpoint.X, touchpoint.Y);

            var modifiers = RawInputModifiers.None;

            var button = EvKey.BTN_LEFT;
            var buttonState = (touchpoint.Evt == TouchEvent.Pressed) ? 1 : 0;

            RawPointerEventArgs? value = button switch {
                EvKey.BTN_LEFT when buttonState == 1
                    => new(this.mouseDevice, touchpoint.Timestamp, this.inputRoot, RawPointerEventType.LeftButtonDown, mousePosition, modifiers),
                EvKey.BTN_LEFT when buttonState == 0
                    => new(this.mouseDevice, touchpoint.Timestamp, this.inputRoot, RawPointerEventType.LeftButtonUp, mousePosition, modifiers),
                EvKey.BTN_RIGHT when buttonState == 1
                    => new(this.mouseDevice, touchpoint.Timestamp, this.inputRoot, RawPointerEventType.RightButtonUp, mousePosition, modifiers),
                EvKey.BTN_RIGHT when buttonState == 2
                    => new(this.mouseDevice, touchpoint.Timestamp, this.inputRoot, RawPointerEventType.RightButtonDown, mousePosition, modifiers),
                EvKey.BTN_MIDDLE when buttonState == 1
                    => new(this.mouseDevice, touchpoint.Timestamp, this.inputRoot, RawPointerEventType.MiddleButtonDown, mousePosition, modifiers),
                EvKey.BTN_MIDDLE when buttonState == 2
                    => new(this.mouseDevice, touchpoint.Timestamp, this.inputRoot, RawPointerEventType.MiddleButtonUp, mousePosition, modifiers),
                EvKey.BTN_TOUCH => throw new NotImplementedException(),
                _ => default,
            };

            if (value is not null) {
                this.ScheduleInput(value);
            }
        }

    }
}
