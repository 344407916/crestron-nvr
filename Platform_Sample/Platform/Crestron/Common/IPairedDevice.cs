using Crestron.RAD.DeviceTypes.Gateway;

namespace SamplePlatformCommon
{
	public interface IPairedDevice
	{
		GatewayPairedDeviceInformation PairedDeviceInformation { get; }

		void SetConnectionStatus(bool connected);
	}
}
