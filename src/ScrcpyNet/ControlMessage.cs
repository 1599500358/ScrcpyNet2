using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

namespace ScrcpyNet
{
    /// <summary>
    /// Control message ids as defined by scrcpy-server 4.1
    /// (server/src/main/java/com/genymobile/scrcpy/control/ControlMessage.java).
    /// </summary>
    public enum ControlMessageType : byte
    {
        InjectKeycode,
        InjectText,
        InjectTouchEvent,
        InjectScrollEvent,
        BackOrScreenOn,
        ExpandNotificationPanel,
        ExpandSettingsPanel,
        CollapsePanels,
        GetClipboard,
        SetClipboard,
        SetDisplayPower,
        RotateDevice,
        UhidCreate,
        UhidInput,
        UhidDestroy,
        OpenHardKeyboardSettings,
        StartApp,
        ResetVideo,
        CameraSetTorch,
        CameraZoomIn,
        CameraZoomOut,
        ResizeDisplay,
        ScanFile,
    }

    public record ScreenSize
    {
        public ushort Width;
        public ushort Height;
    }

    public record Point
    {
        public int X;
        public int Y;
    }

    // Not sure whether to use struct, record, or class for this.
    public record Position
    {
        public ScreenSize ScreenSize = new();
        public Point Point = new();

        public Span<byte> ToBytes()
        {
            Span<byte> b = new byte[12];
            BinaryPrimitives.WriteInt32BigEndian(b[0..], Point.X);
            BinaryPrimitives.WriteInt32BigEndian(b[4..], Point.Y);
            BinaryPrimitives.WriteUInt16BigEndian(b[8..], ScreenSize.Width);
            BinaryPrimitives.WriteUInt16BigEndian(b[10..], ScreenSize.Height);
            return b;
        }
    }

    public interface IControlMessage
    {
        public ControlMessageType Type { get; }

        Span<byte> ToBytes();
    }

    public class KeycodeControlMessage : IControlMessage
    {
        public ControlMessageType Type => ControlMessageType.InjectKeycode;
        public AndroidKeyEventAction Action { get; set; }
        public AndroidKeycode KeyCode { get; set; }
        public uint Repeat { get; set; }
        public AndroidMetastate Metastate { get; set; }

        public Span<byte> ToBytes()
        {
            Span<byte> b = new byte[14];
            b[0] = (byte)Type;
            b[1] = (byte)Action;
            BinaryPrimitives.WriteInt32BigEndian(b[2..], (int)KeyCode);
            BinaryPrimitives.WriteInt32BigEndian(b[6..], (int)Repeat);
            BinaryPrimitives.WriteInt32BigEndian(b[10..], (int)Metastate);
            return b;
        }
    }

    /// <summary>
    /// Injects UTF-8 text. The server only accepts up to 300 bytes
    /// (ControlMessageReader.INJECT_TEXT_MAX_LENGTH) — note the limit is UTF-8
    /// bytes, not characters.
    /// </summary>
    public class InjectTextControlMessage : IControlMessage
    {
        public const int MaxLength = 300;

        public ControlMessageType Type => ControlMessageType.InjectText;
        public string Text { get; set; } = "";

        public Span<byte> ToBytes()
        {
            var utf8 = Encoding.UTF8.GetBytes(Text);
            if (utf8.Length > MaxLength)
                throw new ArgumentOutOfRangeException(nameof(Text), $"The text must not be longer than {MaxLength} bytes.");

            Span<byte> b = new byte[5 + utf8.Length];
            b[0] = (byte)Type;
            BinaryPrimitives.WriteInt32BigEndian(b[1..], utf8.Length);
            utf8.AsSpan().CopyTo(b[5..]);
            return b;
        }
    }

    public class BackOrScreenOnControlMessage : IControlMessage
    {
        public ControlMessageType Type => ControlMessageType.BackOrScreenOn;
        public AndroidKeyEventAction Action { get; set; }

        public Span<byte> ToBytes()
        {
            Span<byte> b = new byte[2];
            b[0] = (byte)Type;
            b[1] = (byte)Action;
            return b;
        }
    }

    /// <summary>
    /// Turns the display on or off (id 10, "SET_DISPLAY_POWER" since scrcpy 3.0).
    /// </summary>
    public class SetDisplayPowerControlMessage : IControlMessage
    {
        public ControlMessageType Type => ControlMessageType.SetDisplayPower;
        public bool On { get; set; }

        public Span<byte> ToBytes()
        {
            Span<byte> b = new byte[2];
            b[0] = (byte)Type;
            b[1] = (byte)(On ? 1 : 0);
            return b;
        }
    }

    /// <summary>
    /// Physically rotates the device display (id 11), the equivalent of scrcpy's
    /// MOD+r shortcut: the server calls WindowManager.freezeRotation() to toggle
    /// between portrait and landscape. The video stream resizes dynamically via a
    /// session meta packet, no reconnect needed.
    /// </summary>
    public class RotateDeviceControlMessage : IControlMessage
    {
        public ControlMessageType Type => ControlMessageType.RotateDevice;

        public Span<byte> ToBytes()
        {
            // The server reads a single byte for this message type.
            Span<byte> b = new byte[1];
            b[0] = (byte)Type;
            return b;
        }
    }

    public class TouchEventControlMessage : IControlMessage
    {
        public ControlMessageType Type => ControlMessageType.InjectTouchEvent;
        public AndroidMotionEventAction Action { get; set; }
        public AndroidMotionEventButtons Buttons { get; set; } = AndroidMotionEventButtons.AMOTION_EVENT_BUTTON_PRIMARY;
        public ulong PointerId { get; set; } = 0xFFFFFFFFFFFFFFFE;
        public Position Position { get; set; } = new();
        /// <summary>Touch pressure between 0 and 1.</summary>
        public float Pressure { get; set; } = 1f;

        /// <summary>
        /// The button that initiated this action (scrcpy's "action_button"). scrcpy itself
        /// sends 0 for finger events and the pressed button for mouse events; the server
        /// only evaluates it when injecting with <c>pointerId = POINTER_ID_MOUSE</c>.
        /// Defaults to AMOTION_EVENT_BUTTON_PRIMARY, the historical behavior of this
        /// library — set 0 when injecting plain finger events.
        /// </summary>
        public AndroidMotionEventButtons ActionButton { get; set; } = AndroidMotionEventButtons.AMOTION_EVENT_BUTTON_PRIMARY;

        public Span<byte> ToBytes()
        {
            Debug.WriteLine("Sending control message: " + Action);
            Span<byte> b = new byte[32];
            b[0] = (byte)Type;
            b[1] = (byte)Action;
            BinaryPrimitives.WriteUInt64BigEndian(b[2..], PointerId);

            // Position
            BinaryPrimitives.WriteInt32BigEndian(b[10..], Position.Point.X);
            BinaryPrimitives.WriteInt32BigEndian(b[14..], Position.Point.Y);
            BinaryPrimitives.WriteUInt16BigEndian(b[18..], Position.ScreenSize.Width);
            BinaryPrimitives.WriteUInt16BigEndian(b[20..], Position.ScreenSize.Height);

            // Pressure, unsigned 16-bit fixed point
            BinaryPrimitives.WriteUInt16BigEndian(b[22..], (ushort)Math.Clamp(Pressure * 65536f, 0, 0xFFFF));

            BinaryPrimitives.WriteInt32BigEndian(b[24..], (int)ActionButton);
            BinaryPrimitives.WriteInt32BigEndian(b[28..], (int)Buttons);

            return b;
        }
    }

    public class ScrollEventControlMessage : IControlMessage
    {
        /// <summary>The scroll values must be in [-16, 16] (scrcpy &gt;= 3.1, ControlMessageReader.parseInjectScrollEvent).</summary>
        public const float MaxValue = 16f;

        public ControlMessageType Type => ControlMessageType.InjectScrollEvent;
        public Position Position { get; set; } = new();
        public int HorizontalScroll { get; set; }
        public int VerticalScroll { get; set; }

        public Span<byte> ToBytes()
        {
            Span<byte> b = new byte[21];
            b[0] = (byte)Type;
            Position.ToBytes().CopyTo(b[1..]);
            BinaryPrimitives.WriteInt16BigEndian(b[13..], sc_float_to_i16fp(HorizontalScroll));
            BinaryPrimitives.WriteInt16BigEndian(b[15..], sc_float_to_i16fp(VerticalScroll));

            BinaryPrimitives.WriteInt32BigEndian(b[17..], (int)AndroidMotionEventButtons.AMOTION_EVENT_BUTTON_PRIMARY);
            return b;
        }

        /// <summary>
        /// Encodes a scroll value in [-16, 16] as a signed 16-bit fixed point number.
        /// The server decodes it with i16FixedPointToFloat(value) * 16.
        /// </summary>
        public static Int16 sc_float_to_i16fp(float f)
        {
            if (f < -MaxValue || f > MaxValue)
            {
                throw new ArgumentOutOfRangeException("f", $"Value must be between {-MaxValue}f and {MaxValue}f");
            }
            Int32 i = (Int32)(f / MaxValue * 32768f); // 2^15
            return (Int16)Math.Clamp(i, -0x8000, 0x7fff);
        }
    }
}
