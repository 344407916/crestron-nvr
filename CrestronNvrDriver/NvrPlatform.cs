using Crestron.DeviceDrivers.EntityModel;
using Crestron.DeviceDrivers.EntityModel.Data;
using Crestron.DeviceDrivers.SDK;
using Crestron.DeviceDrivers.SDK.EntityModel;
using Crestron.DeviceDrivers.SDK.EntityModel.Attributes;
using Crestron.SimplSharp;
using CrestronNvrDriver.Definitions;
using CrestronNvrDriver.NvrApi;
using System;
using System.Collections.Generic;

namespace CrestronNvrDriver
{
    /// <summary>
    /// NVR 平台驱动 (Platform Driver)
    ///
    /// 职责:
    /// 1. 连接 NVR 并认证
    /// 2. 自动发现并注册 NVR 下属相机为 managed devices
    /// 3. 接收 NVR 告警，直接上报 Crestron Home OS (MC4-R)
    /// 4. 全局布撤防操作
    /// 5. 定时轮询相机列表 (热插拔检测)
    ///
    /// 告警策略: NVR 推送告警 → Platform Driver 直接触发 Programmable Events
    ///           → Crestron Home OS 接收 → 用户在 Actions & Events 中配置联动
    ///           (不经过相机子驱动中转)
    /// </summary>
    public class NvrPlatform : ReflectedAttributeDriverEntity, IDisposable
    {
        private INvrApiClient _nvrClient;
        private NvrAlertListener _alertListener;
        private readonly Dictionary<string, NvrCamera> _cameraEntities = new Dictionary<string, NvrCamera>();
        private CTimer _pollingTimer;
        private bool _initialized;
        private int _retryCount;

        // NVR 连接参数 (配置回调中保存)
        private string _host;
        private ushort _port;
        private string _username;
        private string _password;

        public NvrPlatform(DriverControllerCreationArgs args, DriverImplementationResources resources)
            : base(DriverController.RootControllerId)
        {
            var cfgArgs = DataDrivenConfigurationControllerArgs.FromResources(args, resources, ControllerId);
            ConfigurationController = new DelegateDataDrivenConfigurationController(
                cfgArgs, ApplyConfigurationItems, null, null);
        }

        internal DataDrivenConfigurationController ConfigurationController { get; private set; }

        // ========================================================================================
        // platform:managedDevices — 子设备字典
        // Crestron Home 通过监听此属性的变化来自动更新设备列表
        // ========================================================================================

        [EntityProperty(
            Id = "platform:managedDevices",
            Type = DriverEntityValueType.DeviceDictionary,
            ItemTypeRef = "platform:ManagedDevice"
        )]
        public IDictionary<string, PlatformManagedDevice> ManagedDevices { get; private set; }

        // ========================================================================================
        // Programmable Events — 告警直接上报
        // [EntityEvent] 声明事件 ID
        // [EntityEventMetadata(Programmable = true)] 使其出现在 Actions & Events 页面
        // 触发方式: 先更新属性 + NotifyPropertyChanged(), 再 Invoke() 事件
        // ========================================================================================

        [EntityEvent(Id = "nvr:motionDetected")]
        [EntityEventMetadata(Programmable = true)]
        public event EventHandler MotionDetected;

        [EntityEvent(Id = "nvr:intrusionDetected")]
        [EntityEventMetadata(Programmable = true)]
        public event EventHandler IntrusionDetected;

        [EntityEvent(Id = "nvr:tamperDetected")]
        [EntityEventMetadata(Programmable = true)]
        public event EventHandler TamperDetected;

        [EntityEvent(Id = "nvr:lineCrossDetected")]
        [EntityEventMetadata(Programmable = true)]
        public event EventHandler LineCrossDetected;

        [EntityEvent(Id = "nvr:faceDetected")]
        [EntityEventMetadata(Programmable = true)]
        public event EventHandler FaceDetected;

        [EntityEvent(Id = "nvr:videoLoss")]
        [EntityEventMetadata(Programmable = true)]
        public event EventHandler VideoLoss;

        // ========================================================================================
        // Programmable Properties
        // ========================================================================================

        [EntityProperty(Id = "nvr:allArmed")]
        [EntityPropertyMetadata(Programmable = true)]
        public bool AllArmed { get; private set; }

        /// <summary>全局告警订阅开关: 是否接收 NVR 告警</summary>
        [EntityProperty(Id = "nvr:alertSubscribed")]
        [EntityPropertyMetadata(Programmable = true)]
        public bool AlertSubscribed { get; private set; }

        /// <summary>HDMI 联动开关: 告警触发时是否自动切换 NVR HDMI 输出</summary>
        [EntityProperty(Id = "nvr:hdmiLinkageEnabled")]
        [EntityPropertyMetadata(Programmable = true)]
        public bool HdmiLinkageEnabled { get; private set; }

        [EntityProperty(Id = "nvr:lastAlertType")]
        public string LastAlertType { get; private set; }

        [EntityProperty(Id = "nvr:lastAlertCamera")]
        public string LastAlertCamera { get; private set; }

        [EntityProperty(Id = "nvr:connectedCameraCount")]
        public int ConnectedCameraCount { get; private set; }

        // ========================================================================================
        // Programmable Commands — 全局布撤防
        // ========================================================================================

        [EntityCommand(Id = "nvr:armAll")]
        [EntityCommandMetadata(Programmable = true)]
        public void ArmAll()
        {
            // --- NVR 交互伪代码 ---
            _nvrClient.SetAllCamerasArmed(true);
            // --- 伪代码结束 ---

            AllArmed = true;
            NotifyPropertyChanged("nvr:allArmed", new DriverEntityValue(true));

            foreach (var cam in _cameraEntities.Values)
            {
                cam.UpdateArmState(true);
            }
        }

        [EntityCommand(Id = "nvr:disarmAll")]
        [EntityCommandMetadata(Programmable = true)]
        public void DisarmAll()
        {
            // --- NVR 交互伪代码 ---
            _nvrClient.SetAllCamerasArmed(false);
            // --- 伪代码结束 ---

            AllArmed = false;
            NotifyPropertyChanged("nvr:allArmed", new DriverEntityValue(false));

            foreach (var cam in _cameraEntities.Values)
            {
                cam.UpdateArmState(false);
            }
        }

        // ========================================================================================
        // Programmable Commands — 全局告警订阅/取消订阅
        // ========================================================================================

        [EntityCommand(Id = "nvr:subscribeAlerts")]
        [EntityCommandMetadata(Programmable = true)]
        public void SubscribeAllAlerts()
        {
            // --- NVR 交互伪代码 ---
            _nvrClient.SubscribeAlerts();
            // --- 伪代码结束 ---

            AlertSubscribed = true;
            NotifyPropertyChanged("nvr:alertSubscribed", new DriverEntityValue(true));

            // 同步各相机告警开关
            foreach (var cam in _cameraEntities.Values)
            {
                cam.UpdateAlertEnabled(true);
            }
        }

        [EntityCommand(Id = "nvr:unsubscribeAlerts")]
        [EntityCommandMetadata(Programmable = true)]
        public void UnsubscribeAllAlerts()
        {
            // --- NVR 交互伪代码 ---
            _nvrClient.UnsubscribeAlerts();
            // --- 伪代码结束 ---

            AlertSubscribed = false;
            NotifyPropertyChanged("nvr:alertSubscribed", new DriverEntityValue(false));

            // 同步各相机告警开关
            foreach (var cam in _cameraEntities.Values)
            {
                cam.UpdateAlertEnabled(false);
            }
        }

        // ========================================================================================
        // Programmable Commands — HDMI 联动控制
        // ========================================================================================

        [EntityCommand(Id = "nvr:enableHdmiLinkage")]
        [EntityCommandMetadata(Programmable = true)]
        public void EnableHdmiLinkage()
        {
            HdmiLinkageEnabled = true;
            NotifyPropertyChanged("nvr:hdmiLinkageEnabled", new DriverEntityValue(true));
        }

        [EntityCommand(Id = "nvr:disableHdmiLinkage")]
        [EntityCommandMetadata(Programmable = true)]
        public void DisableHdmiLinkage()
        {
            HdmiLinkageEnabled = false;
            NotifyPropertyChanged("nvr:hdmiLinkageEnabled", new DriverEntityValue(false));
        }

        [EntityCommand(Id = "nvr:setHdmiOutput")]
        [EntityCommandMetadata(Programmable = true)]
        public void SetHdmiOutput(string channelId)
        {
            // --- NVR 交互伪代码 ---
            _nvrClient.SetHdmiOutputChannel(channelId);
            // --- 伪代码结束 ---
        }

        [EntityCommand(Id = "nvr:setHdmiMultiView")]
        [EntityCommandMetadata(Programmable = true)]
        public void SetHdmiMultiView()
        {
            // --- NVR 交互伪代码 ---
            _nvrClient.SetHdmiOutputMultiView();
            // --- 伪代码结束 ---
        }

        [EntityCommand(Id = "nvr:resetHdmiOutput")]
        [EntityCommandMetadata(Programmable = true)]
        public void ResetHdmiOutput()
        {
            // --- NVR 交互伪代码 ---
            _nvrClient.ResetHdmiOutput();
            // --- 伪代码结束 ---
        }

        // ========================================================================================
        // Configuration Flow
        // 用户在 Crestron Home Setup 中输入 NVR IP/Port/Username/Password 后触发
        // ========================================================================================

        private ConfigurationItemErrors ApplyConfigurationItems(
            DataDrivenConfigurationController.ApplyConfigurationAction action,
            string stepId,
            IDictionary<string, DriverEntityValue?> values)
        {
            switch (action)
            {
                case DataDrivenConfigurationController.ApplyConfigurationAction.ApplyAll:
                case DataDrivenConfigurationController.ApplyConfigurationAction.ApplyStep:
                    DriverEntityValue? value;

                    if (values.TryGetValue("_Host_", out value) && value.HasValue)
                        _host = value.Value.GetValue<string>();
                    if (values.TryGetValue("_Port_", out value) && value.HasValue)
                        _port = (ushort)value.Value.GetValue<long>();
                    if (values.TryGetValue("_Username_", out value) && value.HasValue)
                        _username = value.Value.GetValue<string>();
                    if (values.TryGetValue("_Password_", out value) && value.HasValue)
                        _password = value.Value.GetValue<string>();

                    if (string.IsNullOrEmpty(_host))
                    {
                        return new ConfigurationItemErrors(
                            new Dictionary<string, string> { { "_Host_", "NVR IP address is required" } },
                            null);
                    }

                    // --- NVR 交互伪代码 ---
                    _nvrClient = new NvrApiClient(_host, _port, _username, _password);
                    _nvrClient.Connect();
                    // --- 伪代码结束 ---

                    InitializeDriver();
                    return null;

                case DataDrivenConfigurationController.ApplyConfigurationAction.ClearValues:
                    // 用户在配置向导中点"返回"时触发
                    break;
            }

            return null;
        }

        // ========================================================================================
        // 驱动初始化
        // ========================================================================================

        private void InitializeDriver()
        {
            if (_initialized) return;
            _initialized = true;

            // 默认启用告警订阅
            AlertSubscribed = true;
            NotifyPropertyChanged("nvr:alertSubscribed", new DriverEntityValue(true));

            // 默认关闭 HDMI 联动
            HdmiLinkageEnabled = false;
            NotifyPropertyChanged("nvr:hdmiLinkageEnabled", new DriverEntityValue(false));

            DiscoverAndRegisterCameras();
            StartAlertListener();
            StartPolling();
        }

        // ========================================================================================
        // 相机发现与注册
        // ========================================================================================

        private void DiscoverAndRegisterCameras()
        {
            // --- NVR 交互伪代码 ---
            var cameras = _nvrClient.GetCameraList();
            // --- 伪代码结束 ---

            var controllersToAdd = new List<ConfigurableDriverEntity>();
            var managedDevices = new Dictionary<string, PlatformManagedDevice>();

            foreach (var camInfo in cameras)
            {
                // 跳过已注册的相机
                if (_cameraEntities.ContainsKey(camInfo.ChannelId))
                    continue;

                // 1. 创建相机子设备 Entity
                var cameraEntity = new NvrCamera(camInfo.ChannelId, _nvrClient, camInfo);
                _cameraEntities[camInfo.ChannelId] = cameraEntity;

                // 2. 注册子控制器 (无需独立配置)
                controllersToAdd.Add(new ConfigurableDriverEntity(
                    cameraEntity.ControllerId, cameraEntity, null));

                // 3. 创建设备描述 → DeviceUxCategory.Camera
                managedDevices[camInfo.ChannelId] = new PlatformManagedDevice(
                    DeviceUxCategory.Camera,
                    camInfo.Name,
                    "YourCompany",
                    camInfo.Model,
                    camInfo.ChannelId
                );
            }

            if (controllersToAdd.Count == 0)
                return;

            // 4. 批量注册子控制器
            UpdateSubControllers(controllersToAdd, null);

            // 5. 通知 Crestron Home 更新设备列表
            if (ManagedDevices == null)
            {
                ManagedDevices = managedDevices;
            }
            else
            {
                // 合并到现有字典 (重连场景)
                var merged = new Dictionary<string, PlatformManagedDevice>(ManagedDevices);
                foreach (var kvp in managedDevices)
                    merged[kvp.Key] = kvp.Value;
                ManagedDevices = merged;
            }

            NotifyPropertyChanged("platform:managedDevices", CreateValueForEntries(ManagedDevices));

            ConnectedCameraCount = _cameraEntities.Count;
            NotifyPropertyChanged("nvr:connectedCameraCount", new DriverEntityValue(ConnectedCameraCount));
        }

        /// <summary>
        /// 动态新增相机 (NVR 新接入相机时)
        /// 使用增量通知，仅通知新增的设备
        /// </summary>
        public void AddCamera(NvrCameraInfo camInfo)
        {
            if (_cameraEntities.ContainsKey(camInfo.ChannelId))
                return;

            var cameraEntity = new NvrCamera(camInfo.ChannelId, _nvrClient, camInfo);
            _cameraEntities[camInfo.ChannelId] = cameraEntity;

            UpdateSubControllers(
                new[] { new ConfigurableDriverEntity(cameraEntity.ControllerId, cameraEntity, null) },
                null);

            var device = new PlatformManagedDevice(
                DeviceUxCategory.Camera, camInfo.Name, "YourCompany", camInfo.Model, camInfo.ChannelId);

            // copy-on-write: 并发安全
            var copy = new Dictionary<string, PlatformManagedDevice>(ManagedDevices);
            copy[camInfo.ChannelId] = device;
            ManagedDevices = copy;

            // 增量通知 (仅通知新增的设备)
            NotifyPropertyChanged("platform:managedDevices",
                DriverEntityValueUpdate.Create(
                    DriverEntityValueUpdate.Create(camInfo.ChannelId, CreateValueForObject(device))
                ));

            ConnectedCameraCount = _cameraEntities.Count;
            NotifyPropertyChanged("nvr:connectedCameraCount", new DriverEntityValue(ConnectedCameraCount));
        }

        /// <summary>
        /// 动态移除相机 (相机从 NVR 移除时)
        /// 使用增量删除通知
        /// </summary>
        public void RemoveCamera(string channelId)
        {
            if (!_cameraEntities.ContainsKey(channelId))
                return;

            // copy-on-write: 并发安全
            var copy = new Dictionary<string, PlatformManagedDevice>(ManagedDevices);
            copy.Remove(channelId);
            ManagedDevices = copy;

            // 通知 Crestron Home 移除设备
            NotifyPropertyChanged("platform:managedDevices",
                DriverEntityValueUpdate.Create(
                    DriverEntityValueUpdate.CreateDeletion(channelId)
                ));

            // 移除子控制器
            UpdateSubControllers(null, new[] { channelId });
            _cameraEntities.Remove(channelId);

            ConnectedCameraCount = _cameraEntities.Count;
            NotifyPropertyChanged("nvr:connectedCameraCount", new DriverEntityValue(ConnectedCameraCount));
        }

        // ========================================================================================
        // 告警监听
        // ========================================================================================

        private void StartAlertListener()
        {
            if (_alertListener != null)
            {
                _alertListener.StopListening();
            }

            _alertListener = new NvrAlertListener(_nvrClient);
            _alertListener.OnAlert += HandleNvrAlert;
            _alertListener.StartListening();
        }

        /// <summary>
        /// 告警处理:
        /// 1. 检查全局告警订阅开关 (AlertSubscribed)
        /// 2. 检查该相机的告警开关 (AlertEnabled)
        /// 3. 更新属性 + NotifyPropertyChanged()
        /// 4. 触发 HDMI 联动 (如果启用)
        /// 5. 触发 Programmable Event
        /// </summary>
        private void HandleNvrAlert(NvrAlertEvent alert)
        {
            // 1. 全局告警开关检查: 未订阅则忽略所有告警
            if (!AlertSubscribed)
                return;

            // 2. 单相机告警开关检查
            NvrCamera camera;
            if (_cameraEntities.TryGetValue(alert.ChannelId, out camera))
            {
                if (!camera.AlertEnabled)
                    return;
            }

            // 3. 更新告警状态属性
            LastAlertType = alert.AlertType.ToString();
            LastAlertCamera = alert.CameraName;
            NotifyPropertyChanged("nvr:lastAlertType", new DriverEntityValue(LastAlertType));
            NotifyPropertyChanged("nvr:lastAlertCamera", new DriverEntityValue(LastAlertCamera));

            // 4. HDMI 联动: 告警触发时自动切换 NVR HDMI 输出到告警相机画面
            if (HdmiLinkageEnabled && !string.IsNullOrEmpty(alert.ChannelId))
            {
                // --- NVR 交互伪代码 ---
                _nvrClient.SetHdmiOutputChannel(alert.ChannelId);
                // --- 伪代码结束 ---
            }

            // 5. 触发对应的 Programmable Event
            switch (alert.AlertType)
            {
                case AlertType.MotionDetection:
                    MotionDetected?.Invoke(this, EventArgs.Empty);
                    break;
                case AlertType.Intrusion:
                    IntrusionDetected?.Invoke(this, EventArgs.Empty);
                    break;
                case AlertType.Tamper:
                    TamperDetected?.Invoke(this, EventArgs.Empty);
                    break;
                case AlertType.LineCross:
                    LineCrossDetected?.Invoke(this, EventArgs.Empty);
                    break;
                case AlertType.FaceDetection:
                    FaceDetected?.Invoke(this, EventArgs.Empty);
                    break;
                case AlertType.VideoLoss:
                    VideoLoss?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }

        // ========================================================================================
        // 轮询 (热插拔检测)
        // 使用 CTimer 替代 CrestronEnvironment.Sleep(), 避免创建多余线程
        // ========================================================================================

        private void StartPolling()
        {
            _pollingTimer?.Stop();
            _pollingTimer?.Dispose();
            _pollingTimer = new CTimer(PollingCallback, null, 30000, 30000);
        }

        private void PollingCallback(object state)
        {
            if (_nvrClient == null)
                return;

            if (!_nvrClient.IsConnected)
            {
                TryReconnect();
                return;
            }

            try
            {
                // --- NVR 交互伪代码 ---
                var currentCameras = _nvrClient.GetCameraList();
                // --- 伪代码结束 ---

                SyncCameraList(currentCameras);
            }
            catch
            {
                // 获取列表失败，下次轮询重试
            }
        }

        /// <summary>
        /// 同步相机列表: 检测新增和移除的相机
        /// </summary>
        private void SyncCameraList(List<NvrCameraInfo> currentCameras)
        {
            var currentIds = new HashSet<string>();

            // 检测新增
            foreach (var cam in currentCameras)
            {
                currentIds.Add(cam.ChannelId);
                if (!_cameraEntities.ContainsKey(cam.ChannelId))
                {
                    AddCamera(cam);
                }
            }

            // 检测移除
            var toRemove = new List<string>();
            foreach (var id in _cameraEntities.Keys)
            {
                if (!currentIds.Contains(id))
                    toRemove.Add(id);
            }

            foreach (var id in toRemove)
            {
                RemoveCamera(id);
            }
        }

        // ========================================================================================
        // 重连策略 (指数退避)
        // ========================================================================================

        private void TryReconnect()
        {
            const int maxRetry = 5;
            const int baseDelayMs = 5000;

            if (_retryCount >= maxRetry)
                return;

            try
            {
                // --- NVR 交互伪代码 ---
                _nvrClient.Connect();
                // --- 伪代码结束 ---

                _retryCount = 0;
                DiscoverAndRegisterCameras();
                StartAlertListener();
            }
            catch
            {
                _retryCount++;
                // 指数退避: 5s, 10s, 20s, 40s, 80s
                int delay = baseDelayMs * (int)Math.Pow(2, _retryCount);
                new CTimer(o => TryReconnect(), null, delay);
            }
        }

        // ========================================================================================
        // 资源清理
        // ========================================================================================

        public void Dispose()
        {
            _pollingTimer?.Stop();
            _pollingTimer?.Dispose();
            _pollingTimer = null;

            _alertListener?.StopListening();
            _alertListener = null;

            foreach (var cam in _cameraEntities.Values)
            {
                if (cam is IDisposable disposable)
                    disposable.Dispose();
            }
            _cameraEntities.Clear();

            // --- NVR 交互伪代码 ---
            _nvrClient?.Disconnect();
            // --- 伪代码结束 ---
            _nvrClient = null;
        }
    }
}
