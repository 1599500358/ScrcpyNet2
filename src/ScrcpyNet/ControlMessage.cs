using System;
using System.Buffers.Binary;
using System.Diagnostics;

namespace ScrcpyNet
{
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
        SetScreenPowerMode,
        RotateDevice,
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

    public class TouchEventControlMessage : IControlMessage
    {
        public ControlMessageType Type => ControlMessageType.InjectTouchEvent;
        public AndroidMotionEventAction Action { get; set; }
        public AndroidMotionEventButtons Buttons { get; set; } = AndroidMotionEventButtons.AMOTION_EVENT_BUTTON_PRIMARY;
        public ulong PointerId { get; set; } = 0xFFFFFFFFFFFFFFFE;
        public Position Position { get; set; } = new();
        //public float Pressure { get; set; }

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

            // TODO: Pressure
            b[22] = 0xFF;
            b[23] = 0xFF;

            b[24] = 0x00;
            b[25] = 0x00;
            b[26] = 0x00;
            b[27] = 0x01;
            BinaryPrimitives.WriteInt32BigEndian(b[28..], (int)Buttons);
            
            return b;
        }
    }

    public class ScrollEventControlMessage : IControlMessage
    {
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

            b[17] = 0x00;
            b[18] = 0x00;
            b[19] = 0x00;
            b[20] = 0x01;
            return b;
        }

        public static Int16 sc_float_to_i16fp(float f)
        {
            if (f < -1.0f || f > 1.0f)
            {
                throw new ArgumentOutOfRangeException("f", "Value must be between -1.0f and 1.0f");
            }
            Int32 i = (Int32)(f * Math.Pow(2,15)); // 2^15
            if (i < -0x8000)
            {
                throw new ArgumentOutOfRangeException("f", "Value must be between -1.0f and 1.0f");
            }
            if (i >= 0x7fff)
            {
                if (i == 0x8000)
                {
                    i = 0x7fff;
                }
                else
                {
                    throw new ArgumentOutOfRangeException("f", "Value must be between -1.0f and 1.0f");
                }
            }
            return (Int16)i;
        }
    }
}
