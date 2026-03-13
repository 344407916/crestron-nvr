using System.Text;
using Crestron.RAD.Common.Enums;
using Crestron.RAD.Common.Interfaces.ExtensionDevice;
using Crestron.RAD.DeviceTypes.ExtensionDevice;
using Crestron.RAD.DeviceTypes.Gateway;
using Crestron.SimplSharp.CrestronIO;
using SamplePlatformCommon;

namespace PairedExtensionDevice_Crestron_Sample_IP
{
	public class SamplePairedExtension : AExtensionDevice, IPairedDevice
	{
		#region Fields

		private int _tapCount;
		private readonly PropertyValue<int> _tapCountProperty;
		private readonly GatewayPairedDeviceInformation _pairedDeviceInfo;

		#endregion

		#region Contructor

		public SamplePairedExtension(string id, string name)
		{
			_tapCount = 0;
			_tapCountProperty = CreateProperty<int>(new PropertyDefinition("TapCount", string.Empty, DevicePropertyType.Int32));

			_pairedDeviceInfo = new GatewayPairedDeviceInformation(
				id,
				name,
				Description,
				Manufacturer,
				BaseModel,
				DriverData.CrestronSerialDeviceApi.GeneralInformation.DeviceType,
				string.Empty);
		}

		#endregion

		#region AExtensionDevice Members

		protected override string GetUiDefinition(string uiFolderPath)
		{
			var uiFilePath = Path.Combine(uiFolderPath, "PairedExtensionUi.xml");

			if (!File.Exists(uiFilePath))
			{
				Log(string.Format("ERROR: Ui Definition file not found. Path: {0}", uiFilePath));
				return null;
			}

			if (EnableLogging)
				Log(string.Format("UI Definition file found. Path: '{0}'", uiFilePath));

			return File.ReadToEnd(uiFilePath, Encoding.UTF8);
		}

		protected override IOperationResult DoCommand(string command, string[] parameters)
		{
			switch (command)
			{
				case "ButtonTap":
					_tapCount++;
					_tapCountProperty.Value = _tapCount;
					Commit();
					break;
			}

			return new OperationResult(OperationResultCode.Success);
		}

		protected override IOperationResult SetDriverPropertyValue<T>(string propertyKey, T value)
		{
			return new OperationResult(OperationResultCode.Error, "this method is not implemented");
		}

		protected override IOperationResult SetDriverPropertyValue<T>(string objectId, string propertyKey, T value)
		{
			return new OperationResult(OperationResultCode.Error, "this method is not implemented");
		}

		#endregion

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

		#region IConnection Members

		public override void Connect()
		{
			Connected = true;

			_tapCountProperty.Value = _tapCount;
			Commit();
		}

		#endregion
	}
}