using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ScrcpyNet.Test
{
    [TestClass]
    public class ControlMessageTests
    {
        [TestMethod]
        public void KeyPressH()
        {
            var msg = new KeycodeControlMessage();
            msg.KeyCode = AndroidKeycode.AKEYCODE_H;
            msg.Metastate = AndroidMetastate.AMETA_NUM_LOCK_ON;

            var expected = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x24, 0x00, 0x00, 0x00, 0x00, 0x00, 0x20, 0x00, 0x00 };
            var actual = msg.ToBytes();
            CollectionAssert.AreEqual(expected, actual.ToArray());
        }

        [TestMethod]
        public void KeyPressE()
        {
            var msg = new KeycodeControlMessage();
            msg.KeyCode = AndroidKeycode.AKEYCODE_E;
            msg.Metastate = AndroidMetastate.AMETA_NONE;

            var expected = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x21, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            var actual = msg.ToBytes();
            CollectionAssert.AreEqual(expected, actual.ToArray());
        }

        [TestMethod]
        public void KeyPressL()
        {
            var msg = new KeycodeControlMessage();
            msg.KeyCode = AndroidKeycode.AKEYCODE_L;
            msg.Metastate = AndroidMetastate.AMETA_SHIFT_ON;

            var expected = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x28, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 };
            var actual = msg.ToBytes();
            CollectionAssert.AreEqual(expected, actual.ToArray());
        }

        [TestMethod]
        public void KeyReleaseO()
        {
            var msg = new KeycodeControlMessage();
            msg.Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_UP;
            msg.KeyCode = AndroidKeycode.AKEYCODE_O;
            msg.Metastate = AndroidMetastate.AMETA_NUM_LOCK_ON;

            var expected = new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x2B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x20, 0x00, 0x00 };
            var actual = msg.ToBytes();
            CollectionAssert.AreEqual(expected, actual.ToArray());
        }

        [TestMethod]
        public void TouchEvent()
        {
            var msg = new TouchEventControlMessage();
            msg.Action = AndroidMotionEventAction.AMOTION_EVENT_ACTION_DOWN;
            msg.PointerId = 0x1234567887654321;
            msg.Position.Point.X = 100;
            msg.Position.Point.Y = 200;
            msg.Position.ScreenSize.Width = 1080;
            msg.Position.ScreenSize.Height = 1920;

            // scrcpy 4.1 layout (ControlMessageReader.parseInjectTouchEvent):
            // type(1) + action(1) + pointer_id(8) + position(12) + pressure(2) + action_button(4) + buttons(4)
            var expected = new byte[] {
                (byte)ControlMessageType.InjectTouchEvent,
                0x00, // AKEY_EVENT_ACTION_DOWN
                0x12, 0x34, 0x56, 0x78, 0x87, 0x65, 0x43, 0x21, // pointer id
                0x00, 0x00, 0x00, 0x64, 0x00, 0x00, 0x00, 0xc8, // 100 200
                0x04, 0x38, 0x07, 0x80, // 1080 1920
                0xff, 0xff, // pressure
                0x00, 0x00, 0x00, 0x01, // action_button = AMOTION_EVENT_BUTTON_PRIMARY
                0x00, 0x00, 0x00, 0x01 // buttons = AMOTION_EVENT_BUTTON_PRIMARY
            };
            var actual = msg.ToBytes();
            CollectionAssert.AreEqual(expected, actual.ToArray());
        }

        [TestMethod]
        public void ScrollEvent()
        {
            var msg = new ScrollEventControlMessage();
            msg.Position.Point.X = 100;
            msg.Position.Point.Y = 200;
            msg.Position.ScreenSize.Width = 1080;
            msg.Position.ScreenSize.Height = 1920;
            msg.VerticalScroll = 1;

            // scrcpy >= 3.1 layout (ControlMessageReader.parseInjectScrollEvent):
            // type(1) + position(12) + hscroll i16fp(2) + vscroll i16fp(2) + buttons(4),
            // the scroll values cover the range [-16, 16].
            var expected = new byte[] {
                (byte)ControlMessageType.InjectScrollEvent,
                0x00, 0x00, 0x00, 0x64, 0x00, 0x00, 0x00, 0xc8, // 100 200
                0x04, 0x38, 0x07, 0x80, // 1080 1920
                0x00, 0x00, // hscroll = 0
                0x08, 0x00, // vscroll = 1.0 (1/16 * 2^15 = 2048)
                0x00, 0x00, 0x00, 0x01 // buttons = AMOTION_EVENT_BUTTON_PRIMARY
            };
            var actual = msg.ToBytes();
            CollectionAssert.AreEqual(expected, actual.ToArray());
        }

        [TestMethod]
        public void InjectText()
        {
            var msg = new InjectTextControlMessage() { Text = "hi" };

            // type(1) + byte length(4) + utf-8 payload
            var expected = new byte[] { (byte)ControlMessageType.InjectText, 0x00, 0x00, 0x00, 0x02, (byte)'h', (byte)'i' };
            var actual = msg.ToBytes();
            CollectionAssert.AreEqual(expected, actual.ToArray());
        }

        [TestMethod]
        public void SetDisplayPower()
        {
            var msg = new SetDisplayPowerControlMessage() { On = false };

            // type(1) + on/off flag(1)
            var expected = new byte[] { (byte)ControlMessageType.SetDisplayPower, 0x00 };
            var actual = msg.ToBytes();
            CollectionAssert.AreEqual(expected, actual.ToArray());
        }
    }
}
