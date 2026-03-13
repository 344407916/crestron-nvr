using CrestronNvrDriver.NvrApi;
using System;

namespace CrestronNvrDriver
{
    /// <summary>
    /// NVR 告警监听器
    ///
    /// 长连接监听 NVR 推送的告警事件。
    /// 收到告警后回调 NvrPlatform，由其触发 Programmable Event。
    /// Crestron Home OS (MC4-R) 直接接收事件，用户在 Actions & Events 中配置联动。
    /// </summary>
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
            // 订阅 NVR 告警推送 (通常为长连接 HTTP/WebSocket/私有协议)
            _nvrClient.OnAlertReceived += NvrClient_OnAlertReceived;
            _nvrClient.SubscribeAlerts();
            // --- 伪代码结束 ---
        }

        public void StopListening()
        {
            if (!_isListening) return;
            _isListening = false;

            // --- NVR 交互伪代码 ---
            _nvrClient.OnAlertReceived -= NvrClient_OnAlertReceived;
            _nvrClient.UnsubscribeAlerts();
            // --- 伪代码结束 ---
        }

        private void NvrClient_OnAlertReceived(object sender, NvrAlertEvent alert)
        {
            OnAlert?.Invoke(alert);
        }
    }
}
