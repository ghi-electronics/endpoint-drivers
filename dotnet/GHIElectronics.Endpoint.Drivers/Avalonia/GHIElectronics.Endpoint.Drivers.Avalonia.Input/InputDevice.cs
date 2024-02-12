using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LinuxFramebuffer.Input;
using Avalonia.Threading;
using GHIElectronics.Endpoint.Devices.Display;

namespace GHIElectronics.Endpoint.Drivers.Avalonia.Input {

    public enum TouchEvent {
        None = 0,
        Pressed = 500,
        Released,
        Move,
    }
    public class TouchPoint {
        internal int X { get; set; }
        internal int Y { get; set; }
        internal TouchEvent Evt { get; set; }
        internal ulong Timestamp { get; set; }
        public TouchPoint() => this.Evt = TouchEvent.None;



        public void Clear() {
            this.Evt = TouchEvent.None;
            this.X = 0;
            this.Y = 0;
            this.Timestamp = 0;
        }
    }
    public partial class InputDevice : IInputBackend {

        //private IScreenInfoProvider screen;
        private IInputRoot inputRoot;

        MouseDevice mouse;
        KeyboardDevice keyboard;
        private Action<RawInputEventArgs> onInput;
        private object objectLocked;
        internal Touch TouchDevice { get; set; }

        private DisplayController displayController;
        private bool enableOnscreenKeyboard;
        private bool showOnscreenKeyboard;
        private OnScreenKeyboard onscreenKeyboard;

        internal TouchPoint TouchPoint { get; set; }




        public InputDevice() {
            this.TouchDevice = new Touch();
            this.TouchPoint = new TouchPoint();
            this.objectLocked = new object();
            this.keyboard = new KeyboardDevice();   

            new Thread(() => InputThread()) {
                IsBackground = true
            }.Start();
        }






        public void Initialize(IScreenInfoProvider screen, Action<RawInputEventArgs> onInput) {
            this.onInput = onInput;
            this.TouchDevice.Initialize(this.onInput);


        }

        public void SetInputRoot(IInputRoot root) {
            this.inputRoot = root;
            this.TouchDevice.SetInputRoot(this.inputRoot);

        }

        public void UpdateTouchPoint(int x, int y, TouchEvent ev) {

            this.TouchPoint.X = x;
            this.TouchPoint.Y = y;

            lock (this.objectLocked) {
                    
                this.TouchPoint.Evt = ev;

                if (this.enableOnscreenKeyboard && this.showOnscreenKeyboard) {
                    this.onscreenKeyboard.UpdateTouchPoint(x,y, ev);

                }

            }
        }

        public void EnableOnscreenKeyboard(DisplayController displayController) {
            this.displayController = displayController;
            this.enableOnscreenKeyboard = true;
            this.showOnscreenKeyboard = false;

            this.onscreenKeyboard = new OnScreenKeyboard(displayController, 0, 0);
        }

        private async  void InputThread() {
            while (true) {

                if (this.inputRoot != null && this.onInput != null) {

                    if (TouchPoint.Evt != TouchEvent.None) {
                        if (this.showOnscreenKeyboard == false) {
                            this.TouchDevice.HandlePointer(TouchPoint);
                            bool needShowOnScreenKeyboard = false;
                            bool actionDone = false;
                            Action x = () => {

                                if (TouchPoint.Evt == TouchEvent.Released && this.inputRoot.PointerOverElement != null) {

                                    var pointerOverElement = this.inputRoot.PointerOverElement;
                                    var elements = pointerOverElement.GetInputElementsAt(new Point(TouchPoint.X, TouchPoint.Y));
                                    var elementlast = elements.LastOrDefault(); 
                                    var element = pointerOverElement.GetInputElementsAt(new Point(TouchPoint.X, TouchPoint.Y)).FirstOrDefault();
                                    

                                    var type = pointerOverElement.GetType();
                                    var focusable = pointerOverElement.Focusable;

                                    
                                    if (type == typeof(TextPresenter) && pointerOverElement.IsEnabled) {
                                        
                                        this.keyboard.SetFocusedElement(this.inputRoot.PointerOverElement, NavigationMethod.Unspecified, KeyModifiers.None);
                                        needShowOnScreenKeyboard = true;
                                        focusable = pointerOverElement.Focusable;
                                        this.onscreenKeyboard.InitialText = ((TextPresenter)pointerOverElement).Text?? string.Empty; 

                                    }
                                    else {
                                        needShowOnScreenKeyboard = false;
                                    }
                                }
                                actionDone = true;
                            };

                            Dispatcher.UIThread.Invoke(x);

                            while (!actionDone) {
                                Thread.Sleep(1);
                            }

                            if (needShowOnScreenKeyboard && this.enableOnscreenKeyboard) {
                                this.showOnscreenKeyboard = true;
                            }
                        }
                        else {
                            this.onscreenKeyboard.HandleKeys(TouchPoint);
                        }

                        lock (this.objectLocked) {
                            TouchPoint.Clear();
                        }

                        continue;
                    }

                    if (this.showOnscreenKeyboard) {

                       
                        await Dispatcher.UIThread.InvokeAsync(this.onscreenKeyboard.Show);

                        var e = new RawTextInputEventArgs(this.keyboard, (ulong)(DateTime.Now.Ticks / 10000), this.inputRoot, this.onscreenKeyboard.SourceText);

                        ScheduleInput(e);

                        this.showOnscreenKeyboard = false;

                        lock (this.objectLocked) {
                            TouchPoint.Clear();
                        }
                        continue;
                    }
                }
                Thread.Sleep(10);
            }
        }

        private void ScheduleInput(RawInputEventArgs ev) => this.onInput.Invoke(ev);

    }

    enum EvKey {
        BTN_LEFT = 0x110,
        BTN_RIGHT = 0x111,
        BTN_MIDDLE = 0x112,
        BTN_TOUCH = 0x14a
    }
    enum LibInputEventType {
        LIBINPUT_EVENT_NONE = 0,
        LIBINPUT_EVENT_DEVICE_ADDED,
        LIBINPUT_EVENT_DEVICE_REMOVED,
        LIBINPUT_EVENT_KEYBOARD_KEY = 300,
        LIBINPUT_EVENT_POINTER_MOTION = 400,
        LIBINPUT_EVENT_POINTER_MOTION_ABSOLUTE,
        LIBINPUT_EVENT_POINTER_BUTTON,
        LIBINPUT_EVENT_POINTER_AXIS,
        LIBINPUT_EVENT_POINTER_SCROLL_WHEEL,
        LIBINPUT_EVENT_POINTER_SCROLL_FINGER,
        LIBINPUT_EVENT_POINTER_SCROLL_CONTINUOUS,
        LIBINPUT_EVENT_TOUCH_DOWN = 500,
        LIBINPUT_EVENT_TOUCH_UP,
        LIBINPUT_EVENT_TOUCH_MOTION,
        LIBINPUT_EVENT_TOUCH_CANCEL,
        LIBINPUT_EVENT_TOUCH_FRAME,
        LIBINPUT_EVENT_TABLET_TOOL_AXIS = 600,
        LIBINPUT_EVENT_TABLET_TOOL_PROXIMITY,
        LIBINPUT_EVENT_TABLET_TOOL_TIP,
        LIBINPUT_EVENT_TABLET_TOOL_BUTTON,
        LIBINPUT_EVENT_TABLET_PAD_BUTTON = 700,
        LIBINPUT_EVENT_TABLET_PAD_RING,
        LIBINPUT_EVENT_TABLET_PAD_STRIP,
        LIBINPUT_EVENT_GESTURE_SWIPE_BEGIN = 800,
        LIBINPUT_EVENT_GESTURE_SWIPE_UPDATE,
        LIBINPUT_EVENT_GESTURE_SWIPE_END,
        LIBINPUT_EVENT_GESTURE_PINCH_BEGIN,
        LIBINPUT_EVENT_GESTURE_PINCH_UPDATE,
        LIBINPUT_EVENT_GESTURE_PINCH_END,
        LIBINPUT_EVENT_SWITCH_TOGGLE = 900,
    }
}
