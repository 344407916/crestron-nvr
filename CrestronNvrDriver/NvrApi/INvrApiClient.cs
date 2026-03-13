using System;
using System.Collections.Generic;

namespace CrestronNvrDriver.NvrApi
{
    /// <summary>
    /// NVR API 接口定义
    ///
    /// 所有与 NVR 的交互抽象为此接口。
    /// 实际实现 (NvrApiClient) 中替换伪代码为真实的 NVR 通信协议。
    /// </summary>
    public interface INvrApiClient
    {
        // ==================== 连接与认证 ====================

        void Connect();
        void Disconnect();
        bool IsConnected { get; }

        // ==================== 相机发现 ====================

        List<NvrCameraInfo> GetCameraList();

        // ==================== PTZ 控制 ====================

        void PtzControl(string channelId, PtzDirection direction, int speed);
        void PtzStop(string channelId);

        // ==================== Zoom 控制 ====================

        void ZoomControl(string channelId, ZoomDirection direction, int speed);
        void ZoomStop(string channelId);
        double GetZoomLevel(string channelId);

        // ==================== LED 控制 ====================

        void SetLedState(string channelId, bool enabled);
        bool GetLedState(string channelId);

        // ==================== 布撤防 ====================

        void SetCameraArmed(string channelId, bool armed);
        void SetAllCamerasArmed(bool armed);
        bool GetCameraArmState(string channelId);

        // ==================== 告警订阅 ====================

        event EventHandler<NvrAlertEvent> OnAlertReceived;
        void SubscribeAlerts();
        void UnsubscribeAlerts();
    }
}
