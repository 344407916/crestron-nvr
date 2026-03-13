using System;

namespace CrestronNvrDriver.NvrApi
{
    /// <summary>
    /// NVR 相机信息
    /// </summary>
    public class NvrCameraInfo
    {
        public string ChannelId { get; set; }
        public string Name { get; set; }
        public string IpAddress { get; set; }
        public string Model { get; set; }
        public bool SupportsPtz { get; set; }
        public bool SupportsLed { get; set; }
        public bool IsOnline { get; set; }
    }

    /// <summary>
    /// NVR 告警事件
    /// </summary>
    public class NvrAlertEvent : EventArgs
    {
        public string ChannelId { get; set; }
        public string CameraName { get; set; }
        public AlertType AlertType { get; set; }
        public DateTime Timestamp { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// 告警类型
    /// </summary>
    public enum AlertType
    {
        MotionDetection,
        Intrusion,
        Tamper,
        LineCross,
        FaceDetection,
        AudioException,
        VideoLoss
    }

    /// <summary>
    /// PTZ 方向
    /// </summary>
    public enum PtzDirection
    {
        Up,
        Down,
        Left,
        Right,
        UpLeft,
        UpRight,
        DownLeft,
        DownRight
    }

    /// <summary>
    /// 缩放方向
    /// </summary>
    public enum ZoomDirection
    {
        In,
        Out
    }
}
