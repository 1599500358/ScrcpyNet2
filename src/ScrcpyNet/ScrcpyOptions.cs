namespace ScrcpyNet
{
    /// <summary>
    /// Value of the scrcpy-server (&gt;= 3.0) "capture_orientation" parameter.
    /// Source: server/src/main/java/com/genymobile/scrcpy/model/Orientation.java
    /// </summary>
    public enum ScrcpyCaptureOrientation
    {
        /// <summary>
        /// Don't send the parameter, the captured video may rotate with the device.
        /// </summary>
        Unlocked = -1,

        /// <summary>
        /// "capture_orientation=@": lock to the orientation the device has when scrcpy starts.
        /// </summary>
        Initial = -2,

        /// <summary>"capture_orientation=@0": natural orientation.</summary>
        Orientation0,

        /// <summary>"capture_orientation=@90": 90 degrees clockwise.</summary>
        Orientation90,

        /// <summary>"capture_orientation=@180": 180 degrees, upside-down.</summary>
        Orientation180,

        /// <summary>"capture_orientation=@270": 270 degrees clockwise (90 degrees counterclockwise).</summary>
        Orientation270,
    }
}
