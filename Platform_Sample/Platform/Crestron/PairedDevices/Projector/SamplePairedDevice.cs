using Crestron.RAD.DeviceTypes.Display;
using Crestron.RAD.DeviceTypes.Gateway;
using SamplePlatformCommon;

namespace SamplePairedDeviceDriver.Devices
{
	public class SamplePairedDevice : ABasicVideoDisplay, IPairedDevice
	{
		#region Fields

		private readonly GatewayPairedDeviceInformation _pairedDeviceInfo;

		#endregion

		#region Initialization

		public SamplePairedDevice(string id, string name)
		{
			_pairedDeviceInfo = new GatewayPairedDeviceInformation(
				id,
				name,
				Description,
				Manufacturer,
				BaseModel,
				DriverData.CrestronSerialDeviceApi.GeneralInformation.DeviceType,
				string.Empty);
		}

		#endregion Initialization

		#region IPairedDevice Members

		public GatewayPairedDeviceInformation PairedDeviceInformation
		{
			get { return _pairedDeviceInfo; }
		}

		public void SetConnectionStatus(bool connected)
		{
			Connected = connected;
		}

		#endregion
	}
}
