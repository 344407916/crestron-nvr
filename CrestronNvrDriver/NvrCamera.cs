using Crestron.DeviceDrivers.EntityModel.Data;
using Crestron.DeviceDrivers.SDK.EntityModel;
using Crestron.DeviceDrivers.SDK.EntityModel.Attributes;
using CrestronNvrDriver.NvrApi;

namespace CrestronNvrDriver
{
    /// <summary>
    /// NVR 相机子设备 Entity
    ///
    /// 作为 Camera 类型 managed device，负责:
    /// 1. PTZ 控制 (Pan/Tilt/Stop)
    /// 2. Zoom 控制 (ZoomIn/ZoomOut/ZoomStop)
    /// 3. LED 灯控制 (Enable/Disable)
    /// 4. 单相机布撤防 (Arm/Disarm)
    ///
    /// 注意: 告警事件不在此处理，由 NvrPlatform 统一接收并直接上报 Crestron Home OS
    /// </summary>
    public class NvrCamera : ReflectedAttributeDriverEntity
    {
        private readonly INvrApiClient _nvrClient;
        private readonly NvrCameraInfo _camInfo;

        public NvrCamera(string controllerId, INvrApiClient nvrClient, NvrCameraInfo camInfo)
            : base(controllerId)
        {
            _nvrClient = nvrClient;
            _camInfo = camInfo;

            // 初始化状态
            IsArmed = false;
            LedEnabled = false;
            AlertEnabled = true; // 默认接收告警
            ZoomLevel = 0.0;
            IsOnline = camInfo.IsOnline;
        }

        // ========================================================================================
        // 设备状态
        // ========================================================================================

        [EntityProperty(Id = "camera:isOnline")]
        public bool IsOnline { get; private set; }

        // ========================================================================================
        // PTZ 控制
        // ========================================================================================

        [EntityCommand(Id = "camera:panLeft")]
        public void PanLeft()
        {
            if (!_camInfo.SupportsPtz) return;
            // --- NVR 交互伪代码 ---
            _nvrClient.PtzControl(_camInfo.ChannelId, PtzDirection.Left, 50);
            // --- 伪代码结束 ---
        }

        [EntityCommand(Id = "camera:panRight")]
        public void PanRight()
        {
            if (!_camInfo.SupportsPtz) return;
            // --- NVR 交互伪代码 ---
            _nvrClient.PtzControl(_camInfo.ChannelId, PtzDirection.Right, 50);
            // --- 伪代码结束 ---
        }

        [EntityCommand(Id = "camera:tiltUp")]
        public void TiltUp()
        {
            if (!_camInfo.SupportsPtz) return;
            // --- NVR 交互伪代码 ---
            _nvrClient.PtzControl(_camInfo.ChannelId, PtzDirection.Up, 50);
            // --- 伪代码结束 ---
        }

        [EntityCommand(Id = "camera:tiltDown")]
        public void TiltDown()
        {
            if (!_camInfo.SupportsPtz) return;
            // --- NVR 交互伪代码 ---
            _nvrClient.PtzControl(_camInfo.ChannelId, PtzDirection.Down, 50);
            // --- 伪代码结束 ---
        }

        [EntityCommand(Id = "camera:ptzStop")]
        public void PtzStop()
        {
            if (!_camInfo.SupportsPtz) return;
            // --- NVR 交互伪代码 ---
            _nvrClient.PtzStop(_camInfo.ChannelId);
            // --- 伪代码结束 ---
        }

        // ========================================================================================
        // Zoom 控制
        // ========================================================================================

        [EntityCommand(Id = "camera:zoomIn")]
        public void ZoomIn()
        {
            if (!_camInfo.SupportsPtz) return;
            // --- NVR 交互伪代码 ---
            _nvrClient.ZoomControl(_camInfo.ChannelId, ZoomDirection.In, 50);
            // --- 伪代码结束 ---
        }

        [EntityCommand(Id = "camera:zoomOut")]
        public void ZoomOut()
        {
            if (!_camInfo.SupportsPtz) return;
            // --- NVR 交互伪代码 ---
            _nvrClient.ZoomControl(_camInfo.ChannelId, ZoomDirection.Out, 50);
            // --- 伪代码结束 ---
        }

        [EntityCommand(Id = "camera:zoomStop")]
        public void ZoomStop()
        {
            if (!_camInfo.SupportsPtz) return;
            // --- NVR 交互伪代码 ---
            _nvrClient.ZoomStop(_camInfo.ChannelId);
            // --- 伪代码结束 ---
        }

        [EntityProperty(Id = "camera:zoomLevel")]
        public double ZoomLevel { get; private set; }

        /// <summary>
        /// 更新缩放级别反馈 (由轮询或 NVR 回调触发)
        /// </summary>
        internal void UpdateZoomLevel(double level)
        {
            if (ZoomLevel != level)
            {
                ZoomLevel = level;
                NotifyPropertyChanged("camera:zoomLevel", new DriverEntityValue(level));
            }
        }

        // ========================================================================================
        // LED 灯控制
        // ========================================================================================

        [EntityProperty(Id = "camera:ledEnabled")]
        [EntityPropertyMetadata(Programmable = true)]
        public bool LedEnabled { get; private set; }

        [EntityCommand(Id = "camera:enableLed")]
        [EntityCommandMetadata(Programmable = true)]
        public void EnableLed()
        {
            if (!_camInfo.SupportsLed) return;

            // --- NVR 交互伪代码 ---
            _nvrClient.SetLedState(_camInfo.ChannelId, true);
            // --- 伪代码结束 ---

            LedEnabled = true;
            NotifyPropertyChanged("camera:ledEnabled", new DriverEntityValue(true));
        }

        [EntityCommand(Id = "camera:disableLed")]
        [EntityCommandMetadata(Programmable = true)]
        public void DisableLed()
        {
            if (!_camInfo.SupportsLed) return;

            // --- NVR 交互伪代码 ---
            _nvrClient.SetLedState(_camInfo.ChannelId, false);
            // --- 伪代码结束 ---

            LedEnabled = false;
            NotifyPropertyChanged("camera:ledEnabled", new DriverEntityValue(false));
        }

        // ========================================================================================
        // 单相机告警开关
        // 控制是否接收该相机的告警 (NvrPlatform 在 HandleNvrAlert 中检查此属性)
        // ========================================================================================

        [EntityProperty(Id = "camera:alertEnabled")]
        [EntityPropertyMetadata(Programmable = true)]
        public bool AlertEnabled { get; private set; }

        [EntityCommand(Id = "camera:enableAlert")]
        [EntityCommandMetadata(Programmable = true)]
        public void EnableAlert()
        {
            // --- NVR 交互伪代码 ---
            _nvrClient.SetCameraAlertEnabled(_camInfo.ChannelId, true);
            // --- 伪代码结束 ---

            AlertEnabled = true;
            NotifyPropertyChanged("camera:alertEnabled", new DriverEntityValue(true));
        }

        [EntityCommand(Id = "camera:disableAlert")]
        [EntityCommandMetadata(Programmable = true)]
        public void DisableAlert()
        {
            // --- NVR 交互伪代码 ---
            _nvrClient.SetCameraAlertEnabled(_camInfo.ChannelId, false);
            // --- 伪代码结束 ---

            AlertEnabled = false;
            NotifyPropertyChanged("camera:alertEnabled", new DriverEntityValue(false));
        }

        /// <summary>
        /// 供 NvrPlatform 调用: 全局告警开关时同步各相机状态
        /// </summary>
        internal void UpdateAlertEnabled(bool enabled)
        {
            if (AlertEnabled != enabled)
            {
                AlertEnabled = enabled;
                NotifyPropertyChanged("camera:alertEnabled", new DriverEntityValue(enabled));
            }
        }

        // ========================================================================================
        // 布撤防
        // ========================================================================================

        [EntityProperty(Id = "camera:armed")]
        [EntityPropertyMetadata(Programmable = true)]
        public bool IsArmed { get; private set; }

        [EntityCommand(Id = "camera:arm")]
        [EntityCommandMetadata(Programmable = true)]
        public void Arm()
        {
            // --- NVR 交互伪代码 ---
            _nvrClient.SetCameraArmed(_camInfo.ChannelId, true);
            // --- 伪代码结束 ---

            IsArmed = true;
            NotifyPropertyChanged("camera:armed", new DriverEntityValue(true));
        }

        [EntityCommand(Id = "camera:disarm")]
        [EntityCommandMetadata(Programmable = true)]
        public void Disarm()
        {
            // --- NVR 交互伪代码 ---
            _nvrClient.SetCameraArmed(_camInfo.ChannelId, false);
            // --- 伪代码结束 ---

            IsArmed = false;
            NotifyPropertyChanged("camera:armed", new DriverEntityValue(false));
        }

        /// <summary>
        /// 供 NvrPlatform 调用: 全局布撤防时同步各相机状态
        /// </summary>
        internal void UpdateArmState(bool armed)
        {
            if (IsArmed != armed)
            {
                IsArmed = armed;
                NotifyPropertyChanged("camera:armed", new DriverEntityValue(armed));
            }
        }

        /// <summary>
        /// 更新在线状态 (由轮询触发)
        /// </summary>
        internal void UpdateOnlineState(bool online)
        {
            if (IsOnline != online)
            {
                IsOnline = online;
                NotifyPropertyChanged("camera:isOnline", new DriverEntityValue(online));
            }
        }
    }
}
