using Crestron.DeviceDrivers.SDK.EntityModel.Attributes;

namespace CrestronNvrDriver.Definitions
{
    /// <summary>
    /// 设备 UX 类型枚举。
    /// 定义子设备在 Crestron Home 中显示的设备类型和图标。
    ///
    /// 如果 SDK 版本已内置此枚举，可直接使用 SDK 中的定义，删除此文件。
    /// </summary>
    [EntityDataType(Id = "crestron:DeviceUxCategory")]
    public enum DeviceUxCategory
    {
        Other,
        Amplifier,
        Appliance,
        Area,
        AudioMixer,
        AudioProcessor,
        AvReceiver,
        AvSwitcher,
        AudioSwitcher,
        BlurayPlayer,
        CableBox,
        Camera,
        Display,
        Fan,
        Fireplace,
        GameConsole,
        GarageDoor,
        Group,
        Hvac,
        Intercom,
        IrrigationSystem,
        Light,
        Lock,
        NetworkRouter,
        NetworkSwitch,
        Outlet,
        Platform,
        Pool,
        PoolController,
        PowerController,
        Printer,
        Projector,
        ProjectorLift,
        ProjectorScreen,
        Room,
        Scanner,
        SecuritySystem,
        Sensor,
        Shade,
        Spa,
        Speaker,
        Vacuum,
        Vehicle,
        VideoConferenceCodec,
        VideoServer
    }
}
