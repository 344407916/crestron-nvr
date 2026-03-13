# Crestron Home OS NVR Driver - Implementation Design

## 1. Architecture Overview

### 1.1 SDK Selection: V2 (Entity Model)

**Recommended: SDK V2 (Entity Model)**，理由如下：

| 对比项 | SDK V1 (RAD Framework) | SDK V2 (Entity Model) |
|--------|----------------------|----------------------|
| Camera 设备类型 | 不支持（仅支持 Display/CableBox/AVR 等） | 原生支持（v23.x 起） |
| Platform Driver（父子设备） | 支持，通过 `AddPairedDevice` | 支持，通过 managed device list |
| Events/Actions 可编程 | 通过 Extension Events | 原生 `Programmable = true` 属性装饰 |
| 配置流程 | UserAttributes + Initialize 接口 | 统一 Configuration Flow |
| SDK 维护 | 维护模式，不再新增设备类型 | 活跃开发，最新 v27 |
| 所需程序集 | 多个 RAD 程序集 | 仅 `EntityModel` + `SDK` |

**结论**：采用 SDK V2 (Entity Model) 架构，使用 **Platform Driver + Camera 子设备** 模式。

### 1.2 Driver 整体架构

```
┌─────────────────────────────────────────────────────┐
│              Crestron Home OS (CP4/VC4)              │
│  ┌───────────────────────────────────────────────┐   │
│  │          Actions & Events / Sequences          │   │
│  │  (告警联动、一键布防触发场景等)                    │   │
│  └──────────────────┬────────────────────────────┘   │
│                     │ Programmable Events/Commands    │
│  ┌──────────────────▼────────────────────────────┐   │
│  │        NVR Platform Driver (主驱动)             │   │
│  │  ┌─────────────┐  ┌────────────────────────┐  │   │
│  │  │ NVR 连接管理  │  │  告警接收 & 布撤防       │  │   │
│  │  │ (IP/Auth)    │  │  (直接上报 Crestron)     │  │   │
│  │  └─────────────┘  └────────────────────────┘  │   │
│  │         │                                      │   │
│  │         │ 接收 NVR 告警 → 直接触发 Programmable│   │
│  │         │ Events → Crestron Home OS (MC4-R)    │   │
│  │         │ 在 Actions & Events 中配置联动        │   │
│  │                                                │   │
│  │  ┌─ Managed Devices (自动发现) ──────────────┐ │   │
│  │  │                                            │ │   │
│  │  │  ┌──────────┐ ┌──────────┐ ┌──────────┐  │ │   │
│  │  │  │ Camera 1  │ │ Camera 2  │ │ Camera N  │  │ │   │
│  │  │  │ PTZ/Zoom  │ │ PTZ/Zoom  │ │ PTZ/Zoom  │  │ │   │
│  │  │  │ LED       │ │ LED       │ │ LED       │  │ │   │
│  │  │  │ Arm/Dis   │ │ Arm/Dis   │ │ Arm/Dis   │  │ │   │
│  │  │  └──────────┘ └──────────┘ └──────────┘  │ │   │
│  │  └────────────────────────────────────────────┘ │   │
│  └────────────────────────────────────────────────┘   │
│                     │ TCP/IP                          │
└─────────────────────┼────────────────────────────────┘
                      │
              ┌───────▼───────┐
              │   NVR Device   │
              │  (Your NVR)    │
              └───────────────┘
```

### 1.3 设备类型映射

| 概念 | Crestron SDK V2 设备类型 | 说明 |
|------|------------------------|------|
| NVR 主机 | **Platform** | 网关/平台设备，管理子设备列表 |
| NVR 下属相机 | **Camera** (managed device) | 原生 Camera 类型，支持 PTZ/Zoom |

---

## 2. 项目结构

```
CrestronNvrDriver/
├── CrestronNvrDriver.sln
│
├── NvrPlatformDriver/                        # ★ 平台驱动项目 (主项目)
│   ├── NvrPlatformDriver.csproj              #   SIMPL# Library 项目
│   ├── NvrGatewayProtocol.cs                 #   继承 AGatewayProtocol (V1)
│   ├── NvrPlatformDriver.cs                  #   主驱动入口，连接管理/告警上报
│   ├── NvrAlertListener.cs                   #   告警监听 → 直接上报 Crestron
│   ├── Resources/
│   │   └── NvrPlatformDriver.json            #   平台驱动 JSON (含 dependencies)
│   └── Translations/
│       └── en-US.json
│
├── NvrCameraPairedDriver/                    # ★ 相机子驱动项目 (独立项目!)
│   ├── NvrCameraPairedDriver.csproj          #   SIMPL# Library 项目
│   ├── NvrCameraPairedDriver.cs              #   继承 ABasicDriver，PTZ/Zoom/LED/布撤防
│   ├── Resources/
│   │   └── NvrCameraPairedDriver.json        #   子驱动自己的 JSON 数据文件
│   └── Translations/
│       └── en-US.json
│
├── NvrApi/                                   # 共享 NVR API 层 (类库项目)
│   ├── NvrApi.csproj
│   ├── INvrApiClient.cs                      #   NVR API 接口定义
│   ├── NvrApiClient.cs                       #   NVR API 实现（伪代码）
│   └── NvrModels.cs                          #   NVR 数据模型
│
└── CrestronNvrDriver.Tests/                  # 单元测试项目
    ├── NvrPlatformDriverTests.cs
    └── NvrCameraPairedDriverTests.cs
```

> **重要约束**: Crestron SDK 要求子设备驱动必须是**独立的 SIMPL# Pro Library 项目**，
> 不能仅是主驱动项目中的一个类。构建后，平台驱动的 PKG 文件会自动包含所有子驱动 DLL。

---

## 3. NuGet 依赖

```xml
<PackageReference Include="Crestron.DeviceDrivers.DevKit" Version="27.*" />
<PackageReference Include="Crestron.DeviceDrivers.ManifestUtil" Version="27.*" />
```

> `DevKit` 包含 `Crestron.DeviceDrivers.EntityModel` 和 `Crestron.DeviceDrivers.SDK` 所有引用。
> `ManifestUtil` 在构建后自动生成 manifest。

---

## 4. Driver JSON 清单 (CrestronNvrDriver.json)

```json
{
  "driverSchemaVersion": "2.0",
  "manufacturer": "YourCompany",
  "model": "NVR-Series",
  "deviceType": "Platform",
  "version": "1.0.0",
  "sdkVersion": "27.0",
  "developer": {
    "name": "YourCompany",
    "url": "https://yourcompany.com"
  },
  "description": "NVR Driver for Crestron Home OS - manages IP cameras with PTZ, alerts and arm/disarm",
  "transport": "Ip",
  "configuration": {
    "isNotOfflineConfigurable": false
  },
  "managedDevices": {
    "deviceTypes": ["Camera"]
  }
}
```

---

## 5. 核心模块设计

### 5.1 NVR Platform Driver (主驱动)

```csharp
// NvrPlatformDriver.cs
// 主驱动 - 作为 Platform 类型，负责：
// 1. 连接 NVR 并认证
// 2. 自动发现并注册 NVR 下属相机为 managed devices
// 3. 接收 NVR 告警，直接上报 Crestron Home OS（如 MC4-R）
// 4. 全局布撤防操作
//
// 告警处理策略：NVR 推送告警 → Platform Driver 直接触发
// Programmable Events → Crestron Home OS 接收 → 用户在
// Actions & Events 中配置联动（无需经过相机子驱动中转）

using Crestron.DeviceDrivers.EntityModel;
using Crestron.DeviceDrivers.SDK;

public class NvrPlatformDriver
{
    private INvrApiClient _nvrClient;
    private NvrAlertListener _alertListener;
    private Dictionary<string, NvrCameraDriver> _cameraDrivers;

    // ========== 配置流程 (替代 V1 UserAttributes) ==========

    // 配置步骤1: NVR 连接信息
    // 通过 Entity Model Configuration Flow 统一处理
    // 配置项:
    //   - nvrHost: string (NVR IP 地址)
    //   - nvrPort: int (NVR 端口, 默认 8000)
    //   - username: string (认证用户名)
    //   - password: string (认证密码, 标记为密码类型)
    //   - pollingInterval: int (轮询间隔秒数, 默认 30)

    public ConfigurationStep GetNextConfigurationStep(string currentStepId)
    {
        // 步骤1: NVR 连接参数
        if (currentStepId == null)
        {
            return new ConfigurationStep("nvr_connection")
            {
                Items = {
                    new ConfigurationItem("nvrHost", ConfigType.String, "NVR IP Address"),
                    new ConfigurationItem("nvrPort", ConfigType.Integer, "NVR Port") { DefaultValue = 8000 },
                    new ConfigurationItem("username", ConfigType.String, "Username"),
                    new ConfigurationItem("password", ConfigType.Password, "Password"),
                }
            };
        }

        // 步骤2: 连接验证 & 相机发现
        if (currentStepId == "nvr_connection")
        {
            // 尝试连接 NVR 并发现相机
            ConnectAndDiscoverCameras();
            return null; // 配置完成
        }

        return null;
    }

    // ========== 相机自动发现与注册 ==========
    // 详见下方 "5.5 相机注册到 Crestron Home 的机制详解"

    public void ConnectAndDiscoverCameras()
    {
        // --- NVR 交互伪代码 (已实现部分) ---
        _nvrClient = new NvrApiClient(config.Host, config.Port, config.Username, config.Password);
        _nvrClient.Connect();

        List<NvrCameraInfo> cameras = _nvrClient.GetCameraList();
        // --- 伪代码结束 ---

        foreach (var camInfo in cameras)
        {
            var cameraDriver = new NvrCameraDriver(camInfo, _nvrClient);
            _cameraDrivers[camInfo.ChannelId] = cameraDriver;

            // V1 SDK 方式: 通过 AGatewayProtocol.AddPairedDevice 注册
            AddPairedDevice(
                cameraDriver.PairedDeviceInformation,  // GatewayPairedDeviceInformation
                cameraDriver                            // ABasicDriver 实例
            );

            // SDK 会自动通知 Crestron Home，将相机添加到设备列表
        }
    }

    // ========== 告警接收与直接上报 ==========
    // 设计要点：NVR 告警不经过相机子驱动分发，
    // 由 Platform Driver 直接触发 Programmable Events，
    // Crestron Home OS (MC4-R) 直接接收，
    // 用户在 Crestron Home Setup → Actions & Events 中配置联动。

    public void StartAlertListener()
    {
        _alertListener = new NvrAlertListener(_nvrClient);
        _alertListener.OnAlert += HandleNvrAlert;

        // --- NVR 交互伪代码 ---
        _alertListener.StartListening();  // 长连接监听 NVR 告警推送
        // --- 伪代码结束 ---
    }

    private void HandleNvrAlert(NvrAlertEvent alert)
    {
        // 直接在 Platform Driver 层触发 Programmable Event
        // Crestron Home OS (MC4-R) 会立即收到此事件
        // 无需经过相机子驱动中转

        // 触发告警事件 → Crestron Home OS 接收 → Actions & Events 联动
        RaiseAlertEvent(alert);
    }

    // ========== 全局布撤防操作 ==========

    // [EntityCommandMetadata(Programmable = true)]
    // 标记为 Programmable 使其出现在 Crestron Home Sequences 中
    public void ArmAll()
    {
        // --- NVR 交互伪代码 ---
        _nvrClient.SetAllCamerasArmed(true);
        // --- 伪代码结束 ---

        foreach (var cam in _cameraDrivers.Values)
            cam.UpdateArmState(true);
    }

    // [EntityCommandMetadata(Programmable = true)]
    public void DisarmAll()
    {
        // --- NVR 交互伪代码 ---
        _nvrClient.SetAllCamerasArmed(false);
        // --- 伪代码结束 ---

        foreach (var cam in _cameraDrivers.Values)
            cam.UpdateArmState(false);
    }

    // ========== Programmable Events (直接上报 Crestron Home OS) ==========
    // 以下所有事件标记为 Programmable = true，
    // 会自动出现在 Crestron Home Setup → Actions & Events 页面，
    // 用户可直接在 MC4-R 等设备上配置联动规则。

    // [EntityEventMetadata(Programmable = true)]
    // 事件: 移动侦测告警（携带相机名称、通道号）
    public event EventHandler<AlertEventArgs> OnMotionDetected;

    // [EntityEventMetadata(Programmable = true)]
    // 事件: 入侵检测告警
    public event EventHandler<AlertEventArgs> OnIntrusionDetected;

    // [EntityEventMetadata(Programmable = true)]
    // 事件: 遮挡检测告警
    public event EventHandler<AlertEventArgs> OnTamperDetected;

    // [EntityEventMetadata(Programmable = true)]
    // 事件: 越界检测告警
    public event EventHandler<AlertEventArgs> OnLineCrossDetected;

    // [EntityEventMetadata(Programmable = true)]
    // 事件: 人脸检测告警
    public event EventHandler<AlertEventArgs> OnFaceDetected;

    // [EntityEventMetadata(Programmable = true)]
    // 事件: 视频丢失告警
    public event EventHandler<AlertEventArgs> OnVideoLoss;

    // [EntityEventMetadata(Programmable = true)]
    // 事件: 全局布防状态变更
    public event EventHandler<ArmStateEventArgs> OnArmStateChanged;

    // 统一告警分发：NVR 告警 → 直接触发对应的 Programmable Event
    private void RaiseAlertEvent(NvrAlertEvent alert)
    {
        var args = new AlertEventArgs
        {
            CameraName = alert.CameraName,
            ChannelId = alert.ChannelId,
            AlertType = alert.AlertType.ToString(),
            Timestamp = alert.Timestamp,
            Message = alert.Message
        };

        // 按告警类型触发对应事件，Crestron Home OS 直接接收
        switch (alert.AlertType)
        {
            case AlertType.MotionDetection:
                OnMotionDetected?.Invoke(this, args);
                break;
            case AlertType.Intrusion:
                OnIntrusionDetected?.Invoke(this, args);
                break;
            case AlertType.Tamper:
                OnTamperDetected?.Invoke(this, args);
                break;
            case AlertType.LineCross:
                OnLineCrossDetected?.Invoke(this, args);
                break;
            case AlertType.FaceDetection:
                OnFaceDetected?.Invoke(this, args);
                break;
            case AlertType.VideoLoss:
                OnVideoLoss?.Invoke(this, args);
                break;
        }
    }
}
```

### 5.2 NVR Camera Driver (相机子驱动)

```csharp
// NvrCameraDriver.cs
// 相机子驱动 - 作为 Camera 类型 managed device，仅负责控制：
// 1. PTZ 控制 (Pan/Tilt)
// 2. Zoom 控制
// 3. LED 灯控制
// 4. 单相机布撤防
//
// 注意：告警事件不在此处理，由 NVR Platform Driver 统一接收并
// 直接上报给 Crestron Home OS (MC4-R)

public class NvrCameraDriver
{
    private NvrCameraInfo _cameraInfo;
    private INvrApiClient _nvrClient;

    // ===================================================================
    // PTZ 控制 - 映射到 Entity Model Camera 能力
    // 对应 Camera API: cameraPan, cameraTilt 能力
    // ===================================================================

    // [EntityCommandMetadata(Programmable = true)]
    // 命令: cameraPan:left
    public void PanLeft(int speed = 50)
    {
        // --- NVR 交互伪代码 ---
        _nvrClient.PtzControl(_cameraInfo.ChannelId, PtzDirection.Left, speed);
        // --- 伪代码结束 ---
    }

    // [EntityCommandMetadata(Programmable = true)]
    // 命令: cameraPan:right
    public void PanRight(int speed = 50)
    {
        // --- NVR 交互伪代码 ---
        _nvrClient.PtzControl(_cameraInfo.ChannelId, PtzDirection.Right, speed);
        // --- 伪代码结束 ---
    }

    // 命令: cameraTilt:up
    public void TiltUp(int speed = 50)
    {
        // --- NVR 交互伪代码 ---
        _nvrClient.PtzControl(_cameraInfo.ChannelId, PtzDirection.Up, speed);
        // --- 伪代码结束 ---
    }

    // 命令: cameraTilt:down
    public void TiltDown(int speed = 50)
    {
        // --- NVR 交互伪代码 ---
        _nvrClient.PtzControl(_cameraInfo.ChannelId, PtzDirection.Down, speed);
        // --- 伪代码结束 ---
    }

    // 命令: 停止 PTZ 移动
    public void PtzStop()
    {
        // --- NVR 交互伪代码 ---
        _nvrClient.PtzStop(_cameraInfo.ChannelId);
        // --- 伪代码结束 ---
    }

    // ===================================================================
    // Zoom 控制 - 映射到 Entity Model Camera imageZoom 能力
    // ===================================================================

    // 命令: imageZoom:zoomIn
    public void ZoomIn(int speed = 50)
    {
        // --- NVR 交互伪代码 ---
        _nvrClient.ZoomControl(_cameraInfo.ChannelId, ZoomDirection.In, speed);
        // --- 伪代码结束 ---
    }

    // 命令: imageZoom:zoomOut
    public void ZoomOut(int speed = 50)
    {
        // --- NVR 交互伪代码 ---
        _nvrClient.ZoomControl(_cameraInfo.ChannelId, ZoomDirection.Out, speed);
        // --- 伪代码结束 ---
    }

    // 命令: imageZoom:zoomStop
    public void ZoomStop()
    {
        // --- NVR 交互伪代码 ---
        _nvrClient.ZoomStop(_cameraInfo.ChannelId);
        // --- 伪代码结束 ---
    }

    // 属性: imageZoom:level (当前缩放级别, 0.0-1.0)
    // [EntityPropertyMetadata(Programmable = true)]
    public double ZoomLevel
    {
        get
        {
            // --- NVR 交互伪代码 ---
            return _nvrClient.GetZoomLevel(_cameraInfo.ChannelId);
            // --- 伪代码结束 ---
        }
    }

    // ===================================================================
    // LED 灯控制 - 通过 Extension 自定义能力实现
    // (Camera API 无原生 LED 能力, 使用自定义命令)
    // ===================================================================

    // [EntityPropertyMetadata(Programmable = true)]
    // 自定义属性: LED 灯状态
    public bool IsLedEnabled { get; private set; }

    // [EntityCommandMetadata(Programmable = true)]
    // 自定义命令: 开启 LED 补光灯
    public void EnableLed()
    {
        // --- NVR 交互伪代码 ---
        _nvrClient.SetLedState(_cameraInfo.ChannelId, true);
        // --- 伪代码结束 ---

        IsLedEnabled = true;
        NotifyPropertyChanged(nameof(IsLedEnabled));
    }

    // [EntityCommandMetadata(Programmable = true)]
    // 自定义命令: 关闭 LED 补光灯
    public void DisableLed()
    {
        // --- NVR 交互伪代码 ---
        _nvrClient.SetLedState(_cameraInfo.ChannelId, false);
        // --- 伪代码结束 ---

        IsLedEnabled = false;
        NotifyPropertyChanged(nameof(IsLedEnabled));
    }

    // ===================================================================
    // 一键布撤防 - 通过自定义命令实现
    // ===================================================================

    // [EntityPropertyMetadata(Programmable = true)]
    // 自定义属性: 布防状态
    public bool IsArmed { get; private set; }

    // [EntityCommandMetadata(Programmable = true)]
    // 自定义命令: 布防 (开启移动侦测、入侵检测等)
    public void Arm()
    {
        // --- NVR 交互伪代码 ---
        _nvrClient.SetCameraArmed(_cameraInfo.ChannelId, true);
        // --- 伪代码结束 ---

        IsArmed = true;
        NotifyPropertyChanged(nameof(IsArmed));
        OnArmStateChanged?.Invoke(this, new ArmStateEventArgs { IsArmed = true });
    }

    // [EntityCommandMetadata(Programmable = true)]
    // 自定义命令: 撤防
    public void Disarm()
    {
        // --- NVR 交互伪代码 ---
        _nvrClient.SetCameraArmed(_cameraInfo.ChannelId, false);
        // --- 伪代码结束 ---

        IsArmed = false;
        NotifyPropertyChanged(nameof(IsArmed));
        OnArmStateChanged?.Invoke(this, new ArmStateEventArgs { IsArmed = false });
    }

    // ===================================================================
    // 注意：告警事件不在相机子驱动中处理
    // 所有 NVR 告警由 NvrPlatformDriver 统一接收并直接上报
    // Crestron Home OS (MC4-R)，无需经过相机子驱动中转
    // ===================================================================

    public void UpdateArmState(bool armed)
    {
        IsArmed = armed;
        NotifyPropertyChanged(nameof(IsArmed));
    }
}
```

### 5.3 NVR API 接口层 (伪代码)

```csharp
// INvrApiClient.cs
// NVR API 接口定义 - 所有与 NVR 的交互抽象

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
    public string ChannelId { get; set; }    // 通道 ID
    public string Name { get; set; }          // 相机名称
    public string IpAddress { get; set; }     // 相机 IP
    public string Model { get; set; }         // 相机型号
    public bool SupportsPtz { get; set; }     // 是否支持 PTZ
    public bool SupportsLed { get; set; }     // 是否支持 LED
    public bool IsOnline { get; set; }        // 是否在线
}

public class NvrAlertEvent
{
    public string ChannelId { get; set; }
    public string CameraName { get; set; }
    public AlertType AlertType { get; set; }
    public DateTime Timestamp { get; set; }
    public string Message { get; set; }
}

public enum AlertType
{
    MotionDetection,    // 移动侦测
    Intrusion,          // 入侵检测
    Tamper,             // 遮挡检测
    LineCross,          // 越界检测
    FaceDetection,      // 人脸检测
    AudioException,     // 音频异常
    VideoLoss           // 视频丢失
}

public enum PtzDirection { Up, Down, Left, Right, UpLeft, UpRight, DownLeft, DownRight }
public enum ZoomDirection { In, Out }
```

### 5.4 NVR 告警监听器

```csharp
// NvrAlertListener.cs
// 长连接监听 NVR 推送的告警事件
// 收到告警后直接回调 Platform Driver，由其触发 Programmable Event
// Crestron Home OS (MC4-R) 直接接收事件，用户配置联动即可

public class NvrAlertListener
{
    private INvrApiClient _nvrClient;
    private bool _isListening;

    public event Action<NvrAlertEvent> OnAlert;

    public NvrAlertListener(INvrApiClient nvrClient)
    {
        _nvrClient = nvrClient;
    }

    public void StartListening()
    {
        _isListening = true;

        // --- NVR 交互伪代码 ---
        // 订阅 NVR 告警推送 (通常为长连接 HTTP/WebSocket/私有协议)
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

### 5.5 相机注册到 Crestron Home 的机制详解

> **重要说明**：这是实现方案中最关键、也存在一定不确定性的部分。
> 以下基于 Crestron SDK 官方文档的公开信息整理，部分 V2 API 细节需下载 SDK 后确认。

#### 5.5.1 核心机制：Platform Driver (网关驱动)

Crestron SDK 提供了 **Platform Driver（平台/网关驱动）** 的概念，专门用于：
- 一个物理网关设备（如 NVR）管理多个子设备（如相机）
- 子设备可以动态增加、删除
- 子设备会自动出现在 Crestron Home 设备列表中

**这正是 NVR → 相机 的使用场景。** Crestron 官方文档明确支持此模式。

#### 5.5.2 SDK V1 (RAD Framework) 的实现方式 —— 已有明确代码

V1 SDK 的 Platform Driver 机制**文档最完整**，核心流程如下：

```
步骤1: 主驱动继承 AGatewayProtocol
步骤2: 子驱动继承 ABasicDriver，包含 GatewayPairedDeviceInformation
步骤3: 主驱动调用 AddPairedDevice() 注册子设备
步骤4: SDK 自动通知 Crestron Home，子设备出现在设备列表
```

**核心代码（基于官方文档）：**

```csharp
// ===== 1. 平台驱动 (NVR) - 继承 AGatewayProtocol =====

public class NvrGatewayProtocol : AGatewayProtocol
{
    public NvrGatewayProtocol(ISerialTransport transport, byte id)
        : base(transport, id)
    {
    }

    // 发现并注册相机
    private void DiscoverAndAddCameras()
    {
        // --- NVR 交互伪代码 ---
        var cameras = _nvrClient.GetCameraList();
        // --- 伪代码结束 ---

        foreach (var camInfo in cameras)
        {
            // 创建相机子驱动实例（独立的 SIMPL# Library 项目）
            var cameraPairedDriver = new NvrCameraPairedDriver(
                camInfo.ChannelId,   // 唯一 ID
                camInfo.Name         // 显示名称，如 "前门摄像头"
            );

            // ★ 关键调用：注册子设备到 Crestron Home ★
            // AddPairedDevice 是 AGatewayProtocol 基类提供的方法
            // 调用后，SDK 自动通知 Crestron Home，相机出现在设备列表中
            AddPairedDevice(
                cameraPairedDriver.PairedDeviceInformation,  // 设备标识信息
                cameraPairedDriver                            // 驱动实例
            );
        }
    }

    // 移除相机（相机离线或被删除时）
    private void RemoveCamera(string channelId)
    {
        RemovePairedDevice(channelId);
        // SDK 自动从 Crestron Home 设备列表中移除
    }

    // 平台驱动被移除时，必须清理所有子设备
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // 移除所有已注册的子设备
            foreach (var camera in _pairedCameras)
            {
                RemovePairedDevice(camera.Key);
                camera.Value.Dispose();
            }
        }
        base.Dispose(disposing);
    }
}

// ===== 2. 相机子驱动 - 独立的 SIMPL# Library 项目 =====

public class NvrCameraPairedDriver : ABasicDriver  // 或具体的 Extension Driver
{
    private GatewayPairedDeviceInformation _pairedDeviceInfo;

    public NvrCameraPairedDriver(string deviceId, string deviceName)
    {
        // GatewayPairedDeviceInformation 用于在网关中标识此设备
        _pairedDeviceInfo = new GatewayPairedDeviceInformation(deviceId, deviceName);
    }

    // 平台驱动通过此属性获取设备标识
    public GatewayPairedDeviceInformation PairedDeviceInformation
    {
        get { return _pairedDeviceInfo; }
    }

    // ... PTZ、Zoom、LED、布撤防 等控制方法 ...
}
```

#### 5.5.3 项目结构要求（重要约束）

Crestron SDK 要求 **子设备驱动必须是独立的 SIMPL# Pro Library 项目**，不能仅是主驱动项目中的一个类：

```
CrestronNvrDriver.sln
├── NvrPlatformDriver/                    # 平台驱动项目 (主项目)
│   ├── NvrPlatformDriver.csproj
│   ├── NvrGatewayProtocol.cs            # 继承 AGatewayProtocol
│   ├── NvrPlatformDriver.json           # 平台 JSON，包含 Dependencies 引用子驱动
│   └── ...
│
├── NvrCameraPairedDriver/                # 相机子驱动项目 (独立项目!)
│   ├── NvrCameraPairedDriver.csproj
│   ├── NvrCameraPairedDriver.cs         # 继承 ABasicDriver
│   ├── NvrCameraPairedDriver.json       # 子驱动自己的 JSON 数据文件
│   └── ...
│
└── NvrApi/                               # 共享的 NVR API 层（可选独立项目）
    ├── INvrApiClient.cs
    └── NvrModels.cs
```

**平台驱动 JSON 中必须声明子驱动依赖：**

```json
{
  "deviceType": "Platform",
  "dependencies": [
    {
      "fileName": "NvrCameraPairedDriver.dll",
      "type": "PairedDevice"
    }
  ]
}
```

**构建输出：** 构建平台驱动时，会生成一个 PKG 文件，其中**包含所有子驱动的 DLL**。只需加载平台驱动的 PKG 文件即可，子驱动的 PKG 可以忽略。

#### 5.5.4 SDK V2 (Entity Model) 的情况

V2 SDK 同样支持 Platform Driver + managed devices 模式：
- 官方文档明确提到："A driver supporting the platform capabilities can advertise multiple managed subdevices"
- "The list of managed devices may change at any time"
- SDK v25 新增了 "Configure Child Devices" 文档

**但存在的不确定性：**
- V2 的具体 API 方法名（等价于 V1 的 `AddPairedDevice`）未在公开搜索结果中找到
- V2 Platform Driver 的完整代码示例在 SDK 下载包的 Sample 目录中，需要下载 Crestron Drivers SDK v27 查看
- V2 的 Camera 子设备类型是否可以作为 Platform 的 managed device，需实际验证

#### 5.5.5 推荐方案：采用 V1 SDK (RAD Framework)

基于以上分析，**相机注册部分建议使用 V1 SDK**，理由：

| 因素 | V1 (RAD) | V2 (Entity Model) |
|------|---------|-------------------|
| Platform Driver API | ✅ `AGatewayProtocol.AddPairedDevice` 明确 | ⚠️ 具体 API 需下载 SDK 确认 |
| 代码示例 | ✅ 官方文档有完整代码 | ⚠️ 需从 SDK Sample 获取 |
| Camera 子设备类型 | ⚠️ 无原生 Camera 类型，用 Extension | ✅ 原生 Camera 类型 |
| 子设备动态增删 | ✅ Add/Remove/UpdatePairedDevice | ✅ managed device list 动态变化 |

**最终建议：**
1. **如果 Camera 原生类型不是刚需** → 用 V1 SDK，子设备作为 Extension 类型，开发确定性最高
2. **如果必须用 Camera 原生类型** → 用 V2 SDK，但需要先下载 SDK v27 查看 Platform Sample 确认 API
3. **混合方案不可行** → 官方明确说明 "Platform drivers cannot contain both V1 and V2 child device types"

#### 5.5.6 完整注册流程图

```
┌─────────────────────────────────────────────────────────────┐
│                    Crestron Home OS (MC4-R)                  │
│                                                              │
│  设备列表自动更新 ◄──── SDK 内部机制自动通知                   │
│  ┌─────────┐ ┌─────────┐ ┌─────────┐                       │
│  │ 前门相机  │ │ 后院相机  │ │ 车库相机  │  ← 自动出现          │
│  └─────────┘ └─────────┘ └─────────┘                       │
└──────────────────────────┬──────────────────────────────────┘
                           │
            SDK 自动管理设备注册/注销
                           │
┌──────────────────────────▼──────────────────────────────────┐
│            NVR Platform Driver (网关驱动)                     │
│                                                              │
│  1. 安装到 Crestron 处理器 (CP4/MC4-R)                       │
│  2. 用户在 Home Setup 中添加 NVR 设备                        │
│  3. 输入 NVR IP/Port/用户名/密码                             │
│  4. 驱动连接 NVR → 获取相机列表                              │
│  5. 对每个相机调用 AddPairedDevice()                         │
│     ┌──────────────────────────────────┐                     │
│     │ AddPairedDevice(                  │                     │
│     │   pairedDeviceInfo,  // 设备标识  │                     │
│     │   cameraDriver       // 驱动实例  │                     │
│     │ )                                 │                     │
│     └──────────────────────────────────┘                     │
│  6. SDK 自动将相机注册到 Crestron Home 设备列表               │
│  7. 用户无需手动添加每个相机                                  │
│                                                              │
│  相机热插拔:                                                  │
│  - 新增相机 → AddPairedDevice() → 自动出现                   │
│  - 移除相机 → RemovePairedDevice() → 自动消失                │
│  - 信息变更 → UpdatePairedDevice() → 自动更新                │
└──────────────────────────┬──────────────────────────────────┘
                           │ TCP/IP
                    ┌──────▼──────┐
                    │  NVR 设备    │
                    │  (你的 NVR)  │
                    └─────────────┘
```

#### 5.5.7 需要验证的事项

在正式开发前，建议先验证以下几点：

1. **下载 Crestron Drivers SDK v27**
   - 查看 `/SDK/Samples/Samples.zip` 中的 Platform Sample 项目
   - 确认 V2 Entity Model 的 Platform Driver 子设备注册 API

2. **确认 Camera 类型可否作为 Platform 子设备**
   - V2 SDK 的 Camera 类型是否能作为 Platform 的 managed device
   - 如果不行，需要改用 Extension 类型代替

3. **在真实 Crestron 处理器上测试**
   - AddPairedDevice 后，子设备是否立即出现在 Crestron Home App 中
   - 子设备是否可以被分配到不同房间
   - 子设备的 Programmable 事件/命令是否可用

---

## 6. Crestron Home 集成细节

### 6.1 Actions & Events 联动配置

通过 `Programmable = true` 属性标记，以下事件和命令会自动出现在 Crestron Home Setup 的 **Actions & Events** 页面：

#### 可编程事件 (Events) — 用作触发条件

所有告警事件均由 **NVR Platform Driver 直接上报** Crestron Home OS (MC4-R)，不经过相机子驱动分发。
事件参数中携带 `CameraName` 和 `ChannelId`，用户可在联动规则中区分来源相机。

| 事件名称 | 级别 | 说明 | 参数 |
|----------|------|------|------|
| `OnMotionDetected` | 平台 | 移动侦测告警 | CameraName, ChannelId, Timestamp |
| `OnIntrusionDetected` | 平台 | 入侵检测告警 | CameraName, ChannelId, Timestamp |
| `OnTamperDetected` | 平台 | 遮挡检测告警 | CameraName, ChannelId, Timestamp |
| `OnLineCrossDetected` | 平台 | 越界检测告警 | CameraName, ChannelId, Timestamp |
| `OnFaceDetected` | 平台 | 人脸检测告警 | CameraName, ChannelId, Timestamp |
| `OnVideoLoss` | 平台 | 视频丢失告警 | CameraName, ChannelId, Timestamp |
| `OnArmStateChanged` | 平台 | 全局布防状态变更 | IsArmed |

#### 可编程命令 (Commands) — 用作执行动作

| 命令名称 | 级别 | 说明 |
|----------|------|------|
| `Arm` | 相机 | 单相机布防 |
| `Disarm` | 相机 | 单相机撤防 |
| `ArmAll` | 平台 | 全部相机一键布防 |
| `DisarmAll` | 平台 | 全部相机一键撤防 |
| `EnableLed` | 相机 | 开启补光灯 |
| `DisableLed` | 相机 | 关闭补光灯 |
| `PanLeft/Right` | 相机 | PTZ 水平转动 |
| `TiltUp/Down` | 相机 | PTZ 垂直转动 |
| `ZoomIn/Out` | 相机 | 缩放控制 |

#### 可编程属性 (Properties) — 用作条件判断

| 属性名称 | 类型 | 说明 |
|----------|------|------|
| `IsArmed` | bool | 当前布防状态 |
| `IsLedEnabled` | bool | LED 灯状态 |
| `ZoomLevel` | double | 当前缩放级别 |

#### 典型联动场景示例

所有告警事件在 NVR Platform Driver 上触发，Crestron Home OS 直接接收：

```
场景1: 前门移动侦测 → 开灯 + 发送通知
  触发: NVR.OnMotionDetected (CameraName="前门摄像头")
  动作: Light["门廊灯"].TurnOn()
       Notification.Send("前门检测到移动")
  配置位置: Crestron Home Setup → Actions & Events

场景2: 离家模式 → 一键布防
  触发: Scene["离家模式"].Activated
  动作: NVR.ArmAll()
  配置位置: Crestron Home Setup → Sequences

场景3: 入侵告警 → 联动安防
  触发: NVR.OnIntrusionDetected (CameraName="后院摄像头")
  动作: Camera["后院"].EnableLed()     ← 控制命令走相机子驱动
       SecuritySystem.ArmAway()
       Notification.Send("后院入侵告警!")
  配置位置: Crestron Home Setup → Actions & Events

场景4: 视频丢失 → 报警
  触发: NVR.OnVideoLoss (CameraName="车库摄像头")
  动作: Notification.Send("车库摄像头离线!")
  配置位置: Crestron Home Setup → Actions & Events
```

> **数据流**：NVR 设备 → (TCP/IP) → NVR Platform Driver → (Programmable Event) → Crestron Home OS (MC4-R) → Actions & Events 联动执行

### 6.2 相机自动发现流程

```
┌──────────┐    配置 NVR IP/Port/Auth    ┌──────────────┐
│ Crestron  │ ──────────────────────────► │ NVR Platform  │
│ Home Setup│                             │ Driver        │
└──────────┘                             └───────┬──────┘
                                                  │
                                          Connect & Auth
                                                  │
                                          ┌───────▼──────┐
                                          │   NVR Device   │
                                          └───────┬──────┘
                                                  │
                                          GetCameraList()
                                                  │
                                          ┌───────▼──────────────────┐
                                          │ 返回相机列表:              │
                                          │  CH1: 前门 (PTZ, LED)     │
                                          │  CH2: 后院 (PTZ, LED)     │
                                          │  CH3: 车库 (固定)         │
                                          └───────┬──────────────────┘
                                                  │
                                     对每个相机调用 AddManagedDevice()
                                                  │
┌──────────┐    设备列表自动刷新          ┌───────▼──────┐
│ Crestron  │ ◄──────────────────────── │ Crestron Home │
│ Home App  │    显示 3 个相机设备        │ Device List   │
└──────────┘                             └──────────────┘
```

### 6.3 动态能力适配

相机能力根据 NVR 返回的设备信息动态注册：

```csharp
// 根据相机实际能力动态注册/移除 capabilities
public void ConfigureCameraCapabilities(NvrCameraInfo camInfo)
{
    // PTZ 能力 - 仅 PTZ 相机注册
    if (camInfo.SupportsPtz)
    {
        AddCapability("cameraPan");    // Pan 控制
        AddCapability("cameraTilt");   // Tilt 控制
    }

    // Zoom 能力 - 仅支持变焦的相机注册
    if (camInfo.SupportsPtz)  // PTZ 相机通常支持 Zoom
    {
        AddCapability("imageZoom");
    }

    // LED 能力 - 仅支持 LED 的相机注册
    if (camInfo.SupportsLed)
    {
        AddCapability("led");  // 自定义能力
    }

    // 布撤防 - 所有相机都支持
    AddCapability("armDisarm");  // 自定义能力

    // 注意：告警事件不在相机子驱动注册，
    // 由 NVR Platform Driver 统一处理并直接上报 Crestron Home OS
}
```

---

## 7. Configuration Flow 详细设计

```
┌─────────────────────────────────────────────┐
│         Step 1: NVR Connection               │
│                                              │
│  NVR IP Address:  [192.168.1.100          ]  │
│  NVR Port:        [8000                   ]  │
│  Username:        [admin                  ]  │
│  Password:        [********               ]  │
│                                              │
│               [Next]                         │
└──────────────────┬──────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────┐
│    Step 2: Connection Test & Discovery       │
│                                              │
│  ✓ Connected to NVR successfully             │
│  ✓ Found 3 cameras:                          │
│    - CH1: 前门摄像头 (PTZ, LED)              │
│    - CH2: 后院摄像头 (PTZ, LED)              │
│    - CH3: 车库摄像头 (Fixed)                 │
│                                              │
│  Polling Interval: [30] seconds              │
│  Auto Arm on Connect: [No ▼]                 │
│                                              │
│               [Finish]                       │
└─────────────────────────────────────────────┘
```

---

## 8. 错误处理与重连策略

```csharp
// 连接状态管理
public class ConnectionManager
{
    private const int MAX_RETRY = 5;
    private const int BASE_RETRY_INTERVAL_MS = 5000;  // 5 秒

    public async void MaintainConnection()
    {
        int retryCount = 0;

        while (true)
        {
            if (!_nvrClient.IsConnected)
            {
                try
                {
                    // --- NVR 交互伪代码 ---
                    _nvrClient.Connect();
                    // --- 伪代码结束 ---

                    retryCount = 0;
                    RefreshCameraList();    // 重连后刷新相机列表
                    StartAlertListener();   // 重新订阅告警
                }
                catch (Exception ex)
                {
                    retryCount++;
                    int delay = BASE_RETRY_INTERVAL_MS * (int)Math.Pow(2, Math.Min(retryCount, MAX_RETRY));
                    // 指数退避重试, 最大约 160 秒
                    await Task.Delay(delay);
                }
            }

            await Task.Delay(30000);  // 每 30 秒检查连接
        }
    }
}
```

---

## 9. 实现路线图

### Phase 1: 基础骨架 (Driver 项目搭建)
1. 创建 SIMPL# Library 项目，引用 NuGet 包
2. 编写 Driver JSON 清单文件
3. 实现 NvrPlatformDriver 基础框架（配置流程）
4. 实现 NvrApiClient 接口定义及伪代码实现

### Phase 2: 相机自动发现
5. 实现 Platform Driver 的 managed device 注册
6. 实现 NvrCameraDriver 基础框架
7. 实现动态能力注册逻辑

### Phase 3: 相机控制能力
8. 实现 PTZ 控制 (Pan/Tilt/Stop)
9. 实现 Zoom 控制 (ZoomIn/ZoomOut/Level)
10. 实现 LED 灯控制 (Enable/Disable)
11. 实现布撤防控制 (Arm/Disarm/ArmAll/DisarmAll)

### Phase 4: 告警事件与联动
12. 实现 NvrAlertListener 告警监听（长连接）
13. 在 Platform Driver 中实现告警事件直接上报 Crestron Home OS
14. 标记所有 Programmable 事件/命令/属性
15. 验证 Actions & Events 在 Crestron Home Setup (MC4-R) 中可配置联动

### Phase 5: 稳定性与优化
16. 实现连接管理与自动重连
17. 实现相机列表动态更新（热插拔）
18. 错误处理与日志

---

## 10. 关键注意事项

1. **不要使用 `CrestronEnvironment.Sleep()`**：会创建新线程，多实例时有性能问题，使用 `await Task.Delay()` 或定时器替代。

2. **HttpWebRequest 设置 `KeepAlive = false`**：防止 .NET SDK 内存泄漏。

3. **Platform Driver 不能混用 V1 和 V2 子设备类型**：所有子设备必须统一为 V2 Entity Model。

4. **Programmable 属性仅支持基础类型**：`bool`, `string`, `double`, `int`（不含 `ulong`）。

5. **动态移除能力需谨慎**：移除一个已经在用户 Sequence 中使用的命令会破坏该 Sequence。建议仅在配置阶段进行能力增减。

6. **Driver JSON 中 `driverSchemaVersion` 必须为 `"2.0"`**。

---

## 参考资料

- [Crestron Drivers Developer Microsite](https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Home.htm)
- [Driver SDK V2 (Entity Model)](https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Driver-SDK-V2/Driver-SDK-V2.htm)
- [Entity Model API Reference](https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Driver-SDK-V2/API-Reference/Entity-Model-API-Reference.htm)
- [Camera API](https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Driver-SDK-V2/Create-a-Driver/Device-Types/Camera/Camera-API.htm)
- [Platform Drivers](https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Driver-SDK-V2/Create-a-Driver/Device-Types/Platform/Platform-Drivers.htm)
- [Crestron Home Programming (Sequences)](https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Driver-SDK-V2/Create-a-Driver/Crestron-Home-Sequences.htm)
- [Configuration Flow](https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Driver-SDK-V2/SDK-Framework/Configuration-Flow.htm)
- [SDK Architecture Versions](https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Overview/SDK-Architecture-Versions.htm)
