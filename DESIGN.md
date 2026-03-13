# Crestron Home OS NVR Driver — 设计与实现方案

> **SDK**: V2 Entity Model (v27)
> **驱动类型**: Platform Driver + Camera 子设备
> **目标处理器**: CP4 / MC4-R / VC-4

---

## 1. 系统架构

### 1.1 整体架构图

```
┌─────────────────────────────────────────────────────────────┐
│                  Crestron Home OS (MC4-R)                    │
│                                                             │
│  ┌───────────────────────────────────────────────────────┐  │
│  │           Actions & Events / Sequences                │  │
│  │  (移动侦测→开灯, 离家→全局布防, 入侵→安防联动)         │  │
│  └────────────────────┬──────────────────────────────────┘  │
│                       │ Programmable Events/Commands        │
│  ┌────────────────────▼──────────────────────────────────┐  │
│  │          NvrPlatform (Platform Driver)                 │  │
│  │  ┌──────────────┐  ┌───────────────────────────────┐  │  │
│  │  │ NVR 连接管理   │  │ 告警接收 → Programmable Event │  │  │
│  │  │ (IP/Auth)     │  │ 直接上报 Crestron Home OS     │  │  │
│  │  └──────────────┘  └───────────────────────────────┘  │  │
│  │  ┌──────────────┐  ┌───────────────────────────────┐  │  │
│  │  │ ArmAll       │  │ DisarmAll                     │  │  │
│  │  └──────────────┘  └───────────────────────────────┘  │  │
│  │                                                       │  │
│  │  ┌─ platform:managedDevices (自动发现) ─────────────┐  │  │
│  │  │  ┌──────────┐  ┌──────────┐  ┌──────────┐       │  │  │
│  │  │  │ Camera 1  │  │ Camera 2  │  │ Camera N  │      │  │  │
│  │  │  │ PTZ/Zoom  │  │ PTZ/Zoom  │  │ PTZ/Zoom  │      │  │  │
│  │  │  │ LED       │  │ LED       │  │ LED       │      │  │  │
│  │  │  │ Arm/Dis   │  │ Arm/Dis   │  │ Arm/Dis   │      │  │  │
│  │  │  └──────────┘  └──────────┘  └──────────┘       │  │  │
│  │  └──────────────────────────────────────────────────┘  │  │
│  └───────────────────────────────────────────────────────┘  │
│                       │ TCP/IP                              │
└───────────────────────┼─────────────────────────────────────┘
                        │
                 ┌──────▼──────┐
                 │  NVR 设备    │
                 │  (Your NVR) │
                 └─────────────┘
```

### 1.2 设备类型映射

| 概念 | SDK V2 类型 | 基类 | 说明 |
|------|------------|------|------|
| NVR 主机 | `DeviceType: Platform` | `ReflectedAttributeDriverEntity` | 管理子设备列表, 告警上报 |
| 下属相机 | `DeviceUxCategory.Camera` | `ReflectedAttributeDriverEntity` | PTZ/Zoom/LED/布撤防 |

### 1.3 告警数据流（关键设计决策）

```
NVR 设备 ──TCP/IP──► NvrPlatform (Platform Driver)
                          │
                          │ 直接触发 [EntityEvent(Programmable=true)]
                          │ (不经过 Camera 子驱动中转)
                          ▼
                     Crestron Home OS (MC4-R)
                          │
                          ▼
                     Actions & Events 联动
```

> **设计要点**: 所有告警由 Platform Driver 统一接收并直接上报 Crestron Home OS，
> 不分发到 Camera 子驱动。事件参数中携带 CameraName/ChannelId 用于区分来源。

---

## 2. 项目结构

```
CrestronNvrDriver/
├── CrestronNvrDriver.csproj            # SIMPL# Library 项目
│
├── EntryPoint.cs                       # 驱动入口 [DriverAssemblyEntryPoint]
├── NvrPlatform.cs                      # 平台驱动 (managedDevices / 告警 / 布撤防)
├── NvrCamera.cs                        # 相机子设备 (PTZ / Zoom / LED / 单机布撤防)
├── NvrAlertListener.cs                 # 告警监听 → 回调 Platform 上报
│
├── Definitions/
│   ├── PlatformManagedDevice.cs        # managed device 数据结构
│   └── DeviceUxCategory.cs             # 设备类型枚举 (含 Camera)
│
├── NvrApi/
│   ├── INvrApiClient.cs                # NVR API 接口定义
│   ├── NvrApiClient.cs                 # NVR API 实现 (伪代码)
│   └── NvrModels.cs                    # NVR 数据模型
│
├── Resources/
│   └── CrestronNvrDriver.json          # Driver JSON (SchemaVersion 2.0)
│
└── Translations/
    └── en-US.json
```

> **V2 优势**: 子设备 (NvrCamera) 是同项目中的类，无需独立 SIMPL# Library 项目。
> 构建后生成单个 PKG 文件。

---

## 3. NuGet 依赖

```xml
<PackageReference Include="Crestron.DeviceDrivers.DevKit" Version="27.*" />
<PackageReference Include="Crestron.DeviceDrivers.ManifestUtil" Version="27.*" />
```

---

## 4. 实现流程图

### 4.1 驱动初始化与相机注册流程

```
┌─────────────────────┐
│  Crestron Home Setup │
│  添加 NVR 设备       │
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│  EntryPoint          │
│  CreateDriverController│
│  Instance()          │
└──────────┬──────────┘
           │ new NvrPlatform(args, resources)
           ▼
┌─────────────────────┐
│  NvrPlatform 构造    │
│  创建 Configuration  │
│  Controller          │
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐     ┌─────────────────────┐
│  用户输入配置:        │     │  ConfigurationSteps  │
│  - NVR IP Address    │────►│  JSON 定义 (声明式)   │
│  - Port              │     │  _Host_, _Port_,     │
│  - Username          │     │  _Username_, _Pass_  │
│  - Password          │     └─────────────────────┘
└──────────┬──────────┘
           │ ApplyConfigurationItems() 回调
           ▼
┌─────────────────────┐
│  连接 NVR            │
│  _nvrClient.Connect()│  ◄── NVR 交互伪代码
└──────────┬──────────┘
           │ 连接成功
           ▼
┌─────────────────────┐
│  GetCameraList()     │  ◄── NVR 交互伪代码
│  获取相机列表         │
└──────────┬──────────┘
           │ 返回 N 个相机
           ▼
┌─────────────────────────────────────────┐
│  DiscoverAndRegisterCameras()            │
│                                          │
│  foreach camera:                         │
│    1. new NvrCamera(channelId, ...)      │
│    2. controllersToAdd.Add(              │
│         ConfigurableDriverEntity)        │
│    3. managedDevices[id] =               │
│         PlatformManagedDevice(           │
│           DeviceUxCategory.Camera, ...)  │
│                                          │
│  UpdateSubControllers(controllersToAdd)  │
│  ManagedDevices = managedDevices         │
│  NotifyPropertyChanged(                  │
│    "platform:managedDevices",            │
│    CreateValueForEntries(ManagedDevices)) │
└──────────┬──────────────────────────────┘
           │
           ▼
┌─────────────────────┐
│  StartAlertListener()│
│  订阅 NVR 告警推送    │
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│  StartPolling()      │
│  定期刷新相机列表     │
│  (热插拔检测)        │
└─────────────────────┘
           │
           ▼
   ┌───────────────────────────────────┐
   │  Crestron Home 设备列表自动刷新    │
   │  显示 N 个 Camera 类型设备         │
   │  用户无需手动添加每个相机           │
   └───────────────────────────────────┘
```

### 4.2 告警处理流程

```
┌─────────────┐
│  NVR 设备    │
│  推送告警    │
└──────┬──────┘
       │ TCP/长连接/WebSocket
       ▼
┌──────────────────────┐
│  NvrAlertListener     │
│  OnAlertReceived      │
└──────┬───────────────┘
       │ 回调
       ▼
┌──────────────────────────────────────────┐
│  NvrPlatform.HandleNvrAlert()             │
│                                           │
│  解析 AlertType:                          │
│  ┌──────────────────┬────────────────┐   │
│  │ MotionDetection  │→ 触发 motionDetected event │
│  │ Intrusion        │→ 触发 intrusionDetected event │
│  │ Tamper           │→ 触发 tamperDetected event    │
│  │ LineCross        │→ 触发 lineCrossDetected event │
│  │ FaceDetection    │→ 触发 faceDetected event      │
│  │ VideoLoss        │→ 触发 videoLoss event         │
│  └──────────────────┴────────────────┘   │
│                                           │
│  NotifyEvent("nvr:<eventId>",             │
│    { cameraName, channelId, timestamp })  │
└──────────────────┬───────────────────────┘
                   │ Programmable Event
                   ▼
┌──────────────────────────────────────────┐
│  Crestron Home OS (MC4-R)                 │
│  Actions & Events 自动接收                │
│  用户配置联动规则 (开灯/通知/布防等)        │
└──────────────────────────────────────────┘
```

### 4.3 相机控制流程

```
┌─────────────────┐
│  Crestron Home   │
│  UI / Sequence   │
│  触发命令         │
└────────┬────────┘
         │ EntityCommand 调用
         ▼
┌─────────────────────────────────────────────┐
│  NvrCamera (ReflectedAttributeDriverEntity)  │
│                                              │
│  [EntityCommand] PanLeft/Right/TiltUp/Down   │
│  [EntityCommand] ZoomIn/ZoomOut/ZoomStop     │
│  [EntityCommand] EnableLed/DisableLed        │
│  [EntityCommand] Arm/Disarm                  │
│                                              │
│  → _nvrClient.PtzControl(channelId, ...)     │  ← NVR 交互伪代码
│  → NotifyPropertyChanged() 更新反馈          │
└─────────────────────────────────────────────┘
```

### 4.4 相机热插拔流程

```
┌──────────────────────┐
│  定时轮询 / NVR 通知   │
│  检测到新相机接入       │
└──────────┬───────────┘
           │
           ▼
┌──────────────────────────────────────────────┐
│  NvrPlatform.AddCamera(camInfo)               │
│                                               │
│  1. new NvrCamera(channelId, ...)             │
│  2. UpdateSubControllers([new entity], null)  │
│  3. ManagedDevices[id] = new PlatformManaged  │
│       Device(Camera, name, ...)              │
│  4. NotifyPropertyChanged(                    │
│       "platform:managedDevices",              │
│       DriverEntityValueUpdate.Create(         │   ← 增量通知
│         DriverEntityValueUpdate.Create(       │
│           channelId, CreateValueForObject()   │
│         )))                                   │
└──────────────────────────────────────────────┘

┌──────────────────────┐
│  检测到相机移除        │
└──────────┬───────────┘
           │
           ▼
┌──────────────────────────────────────────────┐
│  NvrPlatform.RemoveCamera(channelId)          │
│                                               │
│  1. ManagedDevices.Remove(channelId)          │
│  2. NotifyPropertyChanged(                    │
│       "platform:managedDevices",              │
│       DriverEntityValueUpdate.Create(         │   ← 增量删除通知
│         DriverEntityValueUpdate              │
│           .CreateDeletion(channelId)))         │
│  3. UpdateSubControllers(null, [channelId])   │
└──────────────────────────────────────────────┘
```

---

## 5. 核心模块实现

### 5.1 EntryPoint (驱动入口)

```csharp
using Crestron.DeviceDrivers.EntityModel;
using Crestron.DeviceDrivers.SDK;
using Crestron.DeviceDrivers.SDK.EntityModel;

[assembly: DriverAssemblyEntryPoint(typeof(EntryPoint))]

public sealed class EntryPoint : DriverAssemblyEntryPoint
{
    public override DriverController CreateDriverControllerInstance(DriverControllerCreationArgs args)
    {
        var resources = DriverImplementationResources.FromCreationArgs(args, typeof(EntryPoint));
        var driverEntity = new NvrPlatform(args, resources);
        var entity = new ConfigurableDriverEntity(
            driverEntity.ControllerId, driverEntity, driverEntity.ConfigurationController);
        return new DispatchingDeviceController(entity, args, null);
    }
}
```

### 5.2 NvrPlatform (平台驱动)

```csharp
using Crestron.DeviceDrivers.EntityModel;
using Crestron.DeviceDrivers.EntityModel.Data;
using Crestron.DeviceDrivers.SDK;
using Crestron.DeviceDrivers.SDK.EntityModel;
using Crestron.DeviceDrivers.SDK.EntityModel.Attributes;
using Definitions;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public class NvrPlatform : ReflectedAttributeDriverEntity
{
    private INvrApiClient _nvrClient;
    private NvrAlertListener _alertListener;
    private readonly Dictionary<string, NvrCamera> _cameraEntities = new Dictionary<string, NvrCamera>();
    private bool _initialized;

    public NvrPlatform(DriverControllerCreationArgs args, DriverImplementationResources resources)
        : base(DriverController.RootControllerId)
    {
        var cfgArgs = DataDrivenConfigurationControllerArgs.FromResources(args, resources, ControllerId);
        ConfigurationController = new DelegateDataDrivenConfigurationController(
            cfgArgs, ApplyConfigurationItems, null, null);
    }

    internal DataDrivenConfigurationController ConfigurationController { get; private set; }

    // ★ 关键属性：managed devices 字典
    // Crestron Home 通过监听此属性的变化来更新设备列表
    [EntityProperty(
        Id = "platform:managedDevices",
        Type = DriverEntityValueType.DeviceDictionary,
        ItemTypeRef = "platform:ManagedDevice"
    )]
    public IDictionary<string, PlatformManagedDevice> ManagedDevices { get; private set; }

    // ==================== Programmable Events (告警直接上报) ====================
    // 标记 Programmable = true → 自动出现在 Crestron Home Actions & Events 页面

    [EntityEvent(Id = "nvr:motionDetected", Programmable = true)]
    public event EventHandler MotionDetected;

    [EntityEvent(Id = "nvr:intrusionDetected", Programmable = true)]
    public event EventHandler IntrusionDetected;

    [EntityEvent(Id = "nvr:tamperDetected", Programmable = true)]
    public event EventHandler TamperDetected;

    [EntityEvent(Id = "nvr:lineCrossDetected", Programmable = true)]
    public event EventHandler LineCrossDetected;

    [EntityEvent(Id = "nvr:faceDetected", Programmable = true)]
    public event EventHandler FaceDetected;

    [EntityEvent(Id = "nvr:videoLoss", Programmable = true)]
    public event EventHandler VideoLoss;

    // ==================== Programmable Properties ====================

    [EntityProperty(Id = "nvr:allArmed", Programmable = true)]
    public bool AllArmed { get; private set; }

    [EntityProperty(Id = "nvr:connectedCameraCount")]
    public int ConnectedCameraCount { get; private set; }

    // ==================== Programmable Commands (全局布撤防) ====================

    [EntityCommand(Id = "nvr:armAll", Programmable = true)]
    public void ArmAll()
    {
        // --- NVR 交互伪代码 ---
        _nvrClient.SetAllCamerasArmed(true);
        // --- 伪代码结束 ---

        AllArmed = true;
        NotifyPropertyChanged("nvr:allArmed", new DriverEntityValue(true));

        foreach (var cam in _cameraEntities.Values)
            cam.UpdateArmState(true);
    }

    [EntityCommand(Id = "nvr:disarmAll", Programmable = true)]
    public void DisarmAll()
    {
        // --- NVR 交互伪代码 ---
        _nvrClient.SetAllCamerasArmed(false);
        // --- 伪代码结束 ---

        AllArmed = false;
        NotifyPropertyChanged("nvr:allArmed", new DriverEntityValue(false));

        foreach (var cam in _cameraEntities.Values)
            cam.UpdateArmState(false);
    }

    // ==================== Configuration Flow ====================

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

                string host = null;
                ushort port = 8000;
                string username = null;
                string password = null;

                if (values.TryGetValue("_Host_", out value) && value.HasValue)
                    host = value.Value.GetValue<string>();
                if (values.TryGetValue("_Port_", out value) && value.HasValue)
                    port = (ushort)value.Value.GetValue<long>();
                if (values.TryGetValue("_Username_", out value) && value.HasValue)
                    username = value.Value.GetValue<string>();
                if (values.TryGetValue("_Password_", out value) && value.HasValue)
                    password = value.Value.GetValue<string>();

                if (string.IsNullOrEmpty(host))
                    return new ConfigurationItemErrors(
                        new Dictionary<string, string> { { "_Host_", "NVR IP address is required" } }, null);

                // --- NVR 交互伪代码 ---
                _nvrClient = new NvrApiClient(host, port, username, password);
                _nvrClient.Connect();
                // --- 伪代码结束 ---

                InitializeDriver();
                return null;

            case DataDrivenConfigurationController.ApplyConfigurationAction.ClearValues:
                break;
        }
        return null;
    }

    // ==================== 驱动初始化 ====================

    private void InitializeDriver()
    {
        if (_initialized) return;
        _initialized = true;

        DiscoverAndRegisterCameras();
        StartAlertListener();
        StartPolling();
    }

    // ==================== 相机发现与注册 ====================

    private void DiscoverAndRegisterCameras()
    {
        // --- NVR 交互伪代码 ---
        var cameras = _nvrClient.GetCameraList();
        // --- 伪代码结束 ---

        var controllersToAdd = new List<ConfigurableDriverEntity>();
        var managedDevices = new Dictionary<string, PlatformManagedDevice>();

        foreach (var camInfo in cameras)
        {
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

        // 4. 批量注册子控制器
        UpdateSubControllers(controllersToAdd, null);

        // 5. 通知 Crestron Home 更新设备列表
        ManagedDevices = managedDevices;
        NotifyPropertyChanged("platform:managedDevices", CreateValueForEntries(ManagedDevices));

        ConnectedCameraCount = cameras.Count;
        NotifyPropertyChanged("nvr:connectedCameraCount", new DriverEntityValue(ConnectedCameraCount));
    }

    // ★ 动态新增相机 (NVR 新接入相机时)
    public void AddCamera(NvrCameraInfo camInfo)
    {
        var cameraEntity = new NvrCamera(camInfo.ChannelId, _nvrClient, camInfo);
        _cameraEntities[camInfo.ChannelId] = cameraEntity;

        UpdateSubControllers(
            new[] { new ConfigurableDriverEntity(cameraEntity.ControllerId, cameraEntity, null) },
            null);

        var device = new PlatformManagedDevice(
            DeviceUxCategory.Camera, camInfo.Name, "YourCompany", camInfo.Model, camInfo.ChannelId);

        var copy = new Dictionary<string, PlatformManagedDevice>(ManagedDevices);
        copy[camInfo.ChannelId] = device;
        ManagedDevices = copy;

        // 增量通知 (仅通知新增的设备)
        NotifyPropertyChanged("platform:managedDevices",
            DriverEntityValueUpdate.Create(
                DriverEntityValueUpdate.Create(camInfo.ChannelId, CreateValueForObject(device))
            ));
    }

    // ★ 动态移除相机
    public void RemoveCamera(string channelId)
    {
        var copy = new Dictionary<string, PlatformManagedDevice>(ManagedDevices);
        copy.Remove(channelId);
        ManagedDevices = copy;

        // 通知 Crestron Home 移除
        NotifyPropertyChanged("platform:managedDevices",
            DriverEntityValueUpdate.Create(
                DriverEntityValueUpdate.CreateDeletion(channelId)
            ));

        UpdateSubControllers(null, new[] { channelId });
        _cameraEntities.Remove(channelId);
    }

    // ==================== 告警监听 ====================

    private void StartAlertListener()
    {
        _alertListener = new NvrAlertListener(_nvrClient);
        _alertListener.OnAlert += HandleNvrAlert;
        _alertListener.StartListening();
    }

    private void HandleNvrAlert(NvrAlertEvent alert)
    {
        // 直接在 Platform Driver 层触发 Programmable Event
        // Crestron Home OS (MC4-R) 直接接收, 无需经过相机子驱动中转
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

    // ==================== 轮询 (热插拔检测) ====================

    private async void StartPolling()
    {
        while (true)
        {
            await Task.Delay(30000); // 每 30 秒

            if (!_nvrClient.IsConnected)
            {
                await TryReconnect();
                continue;
            }

            // --- NVR 交互伪代码 ---
            var currentCameras = _nvrClient.GetCameraList();
            // --- 伪代码结束 ---

            SyncCameraList(currentCameras);
        }
    }

    private void SyncCameraList(List<NvrCameraInfo> currentCameras)
    {
        var currentIds = new HashSet<string>();
        foreach (var cam in currentCameras)
        {
            currentIds.Add(cam.ChannelId);
            if (!_cameraEntities.ContainsKey(cam.ChannelId))
                AddCamera(cam); // 新增
        }

        var toRemove = new List<string>();
        foreach (var id in _cameraEntities.Keys)
        {
            if (!currentIds.Contains(id))
                toRemove.Add(id);
        }
        foreach (var id in toRemove)
            RemoveCamera(id); // 移除
    }

    // ==================== 重连策略 ====================

    private async Task TryReconnect()
    {
        const int maxRetry = 5;
        const int baseDelayMs = 5000;

        for (int i = 0; i < maxRetry; i++)
        {
            try
            {
                // --- NVR 交互伪代码 ---
                _nvrClient.Connect();
                // --- 伪代码结束 ---

                DiscoverAndRegisterCameras();
                StartAlertListener();
                return;
            }
            catch
            {
                int delay = baseDelayMs * (int)Math.Pow(2, i);
                await Task.Delay(delay);
            }
        }
    }
}
```

### 5.3 NvrCamera (相机子设备)

```csharp
using Crestron.DeviceDrivers.EntityModel.Data;
using Crestron.DeviceDrivers.SDK.EntityModel;
using Crestron.DeviceDrivers.SDK.EntityModel.Attributes;

public class NvrCamera : ReflectedAttributeDriverEntity
{
    private readonly INvrApiClient _nvrClient;
    private readonly NvrCameraInfo _camInfo;

    public NvrCamera(string controllerId, INvrApiClient nvrClient, NvrCameraInfo camInfo)
        : base(controllerId)
    {
        _nvrClient = nvrClient;
        _camInfo = camInfo;
    }

    // ==================== PTZ 控制 ====================

    [EntityCommand(Id = "camera:panLeft")]
    public void PanLeft()
    {
        // --- NVR 交互伪代码 ---
        _nvrClient.PtzControl(_camInfo.ChannelId, PtzDirection.Left, 50);
        // --- 伪代码结束 ---
    }

    [EntityCommand(Id = "camera:panRight")]
    public void PanRight()
    {
        // --- NVR 交互伪代码 ---
        _nvrClient.PtzControl(_camInfo.ChannelId, PtzDirection.Right, 50);
        // --- 伪代码结束 ---
    }

    [EntityCommand(Id = "camera:tiltUp")]
    public void TiltUp()
    {
        // --- NVR 交互伪代码 ---
        _nvrClient.PtzControl(_camInfo.ChannelId, PtzDirection.Up, 50);
        // --- 伪代码结束 ---
    }

    [EntityCommand(Id = "camera:tiltDown")]
    public void TiltDown()
    {
        // --- NVR 交互伪代码 ---
        _nvrClient.PtzControl(_camInfo.ChannelId, PtzDirection.Down, 50);
        // --- 伪代码结束 ---
    }

    [EntityCommand(Id = "camera:ptzStop")]
    public void PtzStop()
    {
        // --- NVR 交互伪代码 ---
        _nvrClient.PtzStop(_camInfo.ChannelId);
        // --- 伪代码结束 ---
    }

    // ==================== Zoom 控制 ====================

    [EntityCommand(Id = "camera:zoomIn")]
    public void ZoomIn()
    {
        // --- NVR 交互伪代码 ---
        _nvrClient.ZoomControl(_camInfo.ChannelId, ZoomDirection.In, 50);
        // --- 伪代码结束 ---
    }

    [EntityCommand(Id = "camera:zoomOut")]
    public void ZoomOut()
    {
        // --- NVR 交互伪代码 ---
        _nvrClient.ZoomControl(_camInfo.ChannelId, ZoomDirection.Out, 50);
        // --- 伪代码结束 ---
    }

    [EntityCommand(Id = "camera:zoomStop")]
    public void ZoomStop()
    {
        // --- NVR 交互伪代码 ---
        _nvrClient.ZoomStop(_camInfo.ChannelId);
        // --- 伪代码结束 ---
    }

    [EntityProperty(Id = "camera:zoomLevel")]
    public double ZoomLevel { get; private set; }

    // ==================== LED 灯控制 ====================

    [EntityProperty(Id = "camera:ledEnabled", Programmable = true)]
    public bool LedEnabled { get; private set; }

    [EntityCommand(Id = "camera:enableLed", Programmable = true)]
    public void EnableLed()
    {
        // --- NVR 交互伪代码 ---
        _nvrClient.SetLedState(_camInfo.ChannelId, true);
        // --- 伪代码结束 ---

        LedEnabled = true;
        NotifyPropertyChanged("camera:ledEnabled", new DriverEntityValue(true));
    }

    [EntityCommand(Id = "camera:disableLed", Programmable = true)]
    public void DisableLed()
    {
        // --- NVR 交互伪代码 ---
        _nvrClient.SetLedState(_camInfo.ChannelId, false);
        // --- 伪代码结束 ---

        LedEnabled = false;
        NotifyPropertyChanged("camera:ledEnabled", new DriverEntityValue(false));
    }

    // ==================== 布撤防 ====================

    [EntityProperty(Id = "camera:armed", Programmable = true)]
    public bool IsArmed { get; private set; }

    [EntityCommand(Id = "camera:arm", Programmable = true)]
    public void Arm()
    {
        // --- NVR 交互伪代码 ---
        _nvrClient.SetCameraArmed(_camInfo.ChannelId, true);
        // --- 伪代码结束 ---

        IsArmed = true;
        NotifyPropertyChanged("camera:armed", new DriverEntityValue(true));
    }

    [EntityCommand(Id = "camera:disarm", Programmable = true)]
    public void Disarm()
    {
        // --- NVR 交互伪代码 ---
        _nvrClient.SetCameraArmed(_camInfo.ChannelId, false);
        // --- 伪代码结束 ---

        IsArmed = false;
        NotifyPropertyChanged("camera:armed", new DriverEntityValue(false));
    }

    // 供 Platform Driver 调用 (全局布撤防时同步状态)
    internal void UpdateArmState(bool armed)
    {
        IsArmed = armed;
        NotifyPropertyChanged("camera:armed", new DriverEntityValue(armed));
    }
}
```

### 5.4 NVR API 接口层 (伪代码)

```csharp
// INvrApiClient.cs
public interface INvrApiClient
{
    // 连接与认证
    void Connect();
    void Disconnect();
    bool IsConnected { get; }

    // 相机发现
    List<NvrCameraInfo> GetCameraList();

    // PTZ 控制
    void PtzControl(string channelId, PtzDirection direction, int speed);
    void PtzStop(string channelId);

    // Zoom 控制
    void ZoomControl(string channelId, ZoomDirection direction, int speed);
    void ZoomStop(string channelId);
    double GetZoomLevel(string channelId);

    // LED 控制
    void SetLedState(string channelId, bool enabled);
    bool GetLedState(string channelId);

    // 布撤防
    void SetCameraArmed(string channelId, bool armed);
    void SetAllCamerasArmed(bool armed);
    bool GetCameraArmState(string channelId);

    // 告警订阅
    event EventHandler<NvrAlertEvent> OnAlertReceived;
    void SubscribeAlerts();
    void UnsubscribeAlerts();
}

// NvrModels.cs
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

public class NvrAlertEvent : EventArgs
{
    public string ChannelId { get; set; }
    public string CameraName { get; set; }
    public AlertType AlertType { get; set; }
    public DateTime Timestamp { get; set; }
    public string Message { get; set; }
}

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

public enum PtzDirection { Up, Down, Left, Right, UpLeft, UpRight, DownLeft, DownRight }
public enum ZoomDirection { In, Out }
```

### 5.5 告警监听器

```csharp
// NvrAlertListener.cs
public class NvrAlertListener
{
    private readonly INvrApiClient _nvrClient;
    private bool _isListening;

    public event Action<NvrAlertEvent> OnAlert;

    public NvrAlertListener(INvrApiClient nvrClient)
    {
        _nvrClient = nvrClient;
    }

    public void StartListening()
    {
        if (_isListening) return;
        _isListening = true;

        // --- NVR 交互伪代码 ---
        _nvrClient.OnAlertReceived += (sender, alert) =>
        {
            OnAlert?.Invoke(alert);
        };
        _nvrClient.SubscribeAlerts();
        // --- 伪代码结束 ---
    }

    public void StopListening()
    {
        _isListening = false;
        // --- NVR 交互伪代码 ---
        _nvrClient.UnsubscribeAlerts();
        // --- 伪代码结束 ---
    }
}
```

---

## 6. Driver JSON 清单

```json
{
  "SchemaVersion": "2.0",
  "GeneralInformation": {
    "MinSdkVersion": "21.0000.0000",
    "DeviceType": "Platform",
    "Manufacturer": "YourCompany",
    "BaseModel": "NVR-Series",
    "OtherSupportedModels": [],
    "SupportedSeries": [],
    "DependencyGroup": "",
    "Developer": {
      "Company": "YourCompany",
      "Contact": "Your Name",
      "Email": "dev@yourcompany.com",
      "Website": "www.yourcompany.com",
      "PhoneNumber": "(555) 555-5555"
    },
    "DriverVersion": "1.0000.0001",
    "VersionDate": "2026-01-01 00:00:00.000"
  },
  "ConfigurationSteps": {
    "Items": [
      {
        "Id": "_Host_",
        "UsageContext": "Network.Address",
        "Title": "NVR IP Address / Hostname",
        "Required": true,
        "ValueType": "String"
      },
      {
        "Id": "_Port_",
        "UsageContext": "Network.Port",
        "Title": "NVR Port",
        "Required": true,
        "MaxValue": 65535,
        "MinValue": 1,
        "Persistent": true,
        "ValueType": "Number",
        "DefaultValue": "8000"
      },
      {
        "Id": "_Username_",
        "Title": "Username",
        "Required": true,
        "ValueType": "String"
      },
      {
        "Id": "_Password_",
        "Title": "Password",
        "Required": true,
        "ValueType": "String",
        "Masked": true
      }
    ],
    "Steps": [
      {
        "StepId": "NvrConnection",
        "Items": ["_Host_", "_Port_", "_Username_", "_Password_"],
        "NextStep": null
      }
    ],
    "FirstStep": "NvrConnection"
  }
}
```

---

## 7. Actions & Events 集成

### 7.1 可编程事件 (触发条件)

所有告警由 NvrPlatform 直接上报，Crestron Home OS 直接接收：

| 事件 ID | 说明 | 级别 |
|---------|------|------|
| `nvr:motionDetected` | 移动侦测告警 | 平台 |
| `nvr:intrusionDetected` | 入侵检测告警 | 平台 |
| `nvr:tamperDetected` | 遮挡检测告警 | 平台 |
| `nvr:lineCrossDetected` | 越界检测告警 | 平台 |
| `nvr:faceDetected` | 人脸检测告警 | 平台 |
| `nvr:videoLoss` | 视频丢失告警 | 平台 |

### 7.2 可编程命令 (执行动作)

| 命令 ID | 说明 | 级别 |
|---------|------|------|
| `nvr:armAll` | 全部相机一键布防 | 平台 |
| `nvr:disarmAll` | 全部相机一键撤防 | 平台 |
| `camera:arm` | 单相机布防 | 相机 |
| `camera:disarm` | 单相机撤防 | 相机 |
| `camera:enableLed` | 开启补光灯 | 相机 |
| `camera:disableLed` | 关闭补光灯 | 相机 |
| `camera:panLeft/Right` | PTZ 水平转动 | 相机 |
| `camera:tiltUp/Down` | PTZ 垂直转动 | 相机 |
| `camera:zoomIn/Out` | 缩放控制 | 相机 |

### 7.3 可编程属性 (条件判断)

| 属性 ID | 类型 | 说明 | 级别 |
|---------|------|------|------|
| `nvr:allArmed` | bool | 全局布防状态 | 平台 |
| `camera:armed` | bool | 单相机布防状态 | 相机 |
| `camera:ledEnabled` | bool | LED 灯状态 | 相机 |

### 7.4 典型联动场景

```
场景1: 前门移动侦测 → 开灯 + 通知
  触发: nvr:motionDetected
  动作: Light["门廊灯"].TurnOn()
       Notification.Send("前门检测到移动")

场景2: 离家模式 → 一键布防
  触发: Scene["离家模式"].Activated
  动作: NVR.ArmAll()

场景3: 入侵告警 → 联动安防
  触发: nvr:intrusionDetected
  动作: Camera["后院"].EnableLed()
       SecuritySystem.ArmAway()
       Notification.Send("后院入侵告警!")

场景4: 视频丢失 → 报警
  触发: nvr:videoLoss
  动作: Notification.Send("摄像头离线!")
```

---

## 8. Configuration Flow UI

```
┌─────────────────────────────────────────────┐
│         Step: NvrConnection                  │
│                                              │
│  NVR IP Address:  [192.168.1.100          ]  │
│  NVR Port:        [8000                   ]  │
│  Username:        [admin                  ]  │
│  Password:        [********               ]  │
│                                              │
│               [Complete]                     │
└─────────────────────────────────────────────┘
           │
           ▼ ApplyConfigurationItems → Connect → DiscoverCameras
           │
┌─────────────────────────────────────────────┐
│  Crestron Home 设备列表自动更新              │
│  ✓ 前门摄像头 (Camera, PTZ, LED)            │
│  ✓ 后院摄像头 (Camera, PTZ, LED)            │
│  ✓ 车库摄像头 (Camera, Fixed)               │
└─────────────────────────────────────────────┘
```

---

## 9. 关键注意事项

1. **不要使用 `CrestronEnvironment.Sleep()`** — 会创建新线程，用 `await Task.Delay()` 替代
2. **`HttpWebRequest.KeepAlive = false`** — 防止 .NET SDK 内存泄漏
3. **子设备统一 V2** — Platform Driver 不能混用 V1 和 V2 子设备
4. **Programmable 仅支持基础类型** — `bool`, `string`, `double`, `int` (不含 `ulong`)
5. **动态移除需谨慎** — 移除已在 Sequence 中使用的命令会破坏该 Sequence
6. **线程安全** — ManagedDevices 字典更新时使用 copy-on-write 模式（参见 SDK Sample）
7. **增量通知优先** — 单个相机增减使用 `DriverEntityValueUpdate`，全量刷新用 `CreateValueForEntries`

---

## 10. 实现路线图

### Phase 1: 项目骨架
1. 创建 SIMPL# Library 项目，引用 NuGet 包
2. 编写 Driver JSON 清单文件
3. 实现 EntryPoint + NvrPlatform 基础框架
4. 实现 INvrApiClient 接口定义及伪代码

### Phase 2: 相机自动发现
5. 实现 `DiscoverAndRegisterCameras()` — managed device 注册
6. 实现 NvrCamera 基础框架
7. 实现 `AddCamera()` / `RemoveCamera()` 热插拔

### Phase 3: 相机控制能力
8. 实现 PTZ 控制 (Pan/Tilt/Stop)
9. 实现 Zoom 控制 (ZoomIn/ZoomOut/Level)
10. 实现 LED 灯控制 (Enable/Disable)
11. 实现布撤防控制 (Arm/Disarm/ArmAll/DisarmAll)

### Phase 4: 告警事件
12. 实现 NvrAlertListener 告警监听
13. 实现 Programmable Events 告警直接上报
14. 验证 Actions & Events 在 Crestron Home Setup 中可配置

### Phase 5: 稳定性
15. 实现重连策略 (指数退避)
16. 实现相机列表定时轮询同步
17. 错误处理与日志

---

## 11. 待验证事项

在 Crestron 处理器上测试时需确认：

1. **Camera 子设备 UI** — `DeviceUxCategory.Camera` 是否有专属图标和 PTZ 控件
2. **子设备 Programmable** — 子设备的 `[EntityCommand]` / `[EntityProperty]` 是否出现在 Actions & Events
3. **设备分配** — 子设备是否可分配到不同房间、独立重命名
4. **事件参数** — Programmable Event 是否支持携带自定义参数 (CameraName 等)

---

## 参考资料

- [Crestron Drivers Developer Microsite](https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Home.htm)
- [Driver SDK V2 (Entity Model)](https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Driver-SDK-V2/Driver-SDK-V2.htm)
- [Platform Drivers](https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Driver-SDK-V2/Create-a-Driver/Device-Types/Platform/Platform-Drivers.htm)
- [Camera API](https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Driver-SDK-V2/Create-a-Driver/Device-Types/Camera/Camera-API.htm)
- [Crestron Home Programming](https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Driver-SDK-V2/Create-a-Driver/Crestron-Home-Sequences.htm)
- [Configuration Flow](https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Driver-SDK-V2/SDK-Framework/Configuration-Flow.htm)
