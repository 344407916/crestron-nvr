using Crestron.DeviceDrivers.EntityModel;
using Crestron.DeviceDrivers.SDK;
using Crestron.DeviceDrivers.SDK.EntityModel;

[assembly: DriverAssemblyEntryPoint(typeof(CrestronNvrDriver.EntryPoint))]

namespace CrestronNvrDriver
{
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
}
