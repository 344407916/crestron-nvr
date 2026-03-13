using Crestron.DeviceDrivers.SDK.EntityModel.Attributes;

namespace CrestronNvrDriver.Definitions
{
    /// <summary>
    /// 子设备描述数据结构，对应 platform:managedDevices 字典中的 value。
    /// Crestron Home 通过此信息在设备列表中展示子设备。
    /// </summary>
    [EntityDataType(Id = "platform:ManagedDevice")]
    public class PlatformManagedDevice
    {
        public PlatformManagedDevice(
            DeviceUxCategory uxCategory,
            string name,
            string manufacturer,
            string model,
            string serialNumber)
        {
            UxCategory = uxCategory;
            Name = name;
            Manufacturer = manufacturer;
            Model = model;
            SerialNumber = serialNumber;
        }

        [EntityProperty]
        public DeviceUxCategory UxCategory { get; private set; }

        [EntityProperty]
        public string Name { get; private set; }

        [EntityProperty]
        public string Manufacturer { get; private set; }

        [EntityProperty]
        public string Model { get; private set; }

        [EntityProperty]
        public string SerialNumber { get; private set; }
    }
}
