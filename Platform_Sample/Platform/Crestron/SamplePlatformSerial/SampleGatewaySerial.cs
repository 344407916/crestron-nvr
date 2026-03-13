using System;
using Crestron.RAD.Common.Interfaces;
using Crestron.RAD.Common.Transports;
using Crestron.RAD.DeviceTypes.Gateway;
using Crestron.RAD.ProTransports;
using Crestron.Samples.Platforms;

namespace Platform_Crestron_Sample_Serial
{
	/// <summary>
	/// Sample gateway device driver that communicates through a serial connection.
	/// </summary>
	public class SampleGatewaySerial : AGateway, ISimpl, ISerialComport
	{
		public SimplTransport Initialize(Action<string, object[]> send)
		{
			ConnectionTransport = new SampleGatewaySerialTransport
			{
				EnableLogging = InternalEnableLogging,
				CustomLogger = InternalCustomLogger,
				EnableRxDebug = InternalEnableRxDebug,
				EnableTxDebug = InternalEnableTxDebug
			};

			Protocol = new SampleGatewayProtocol(ConnectionTransport, Id)
			{
				EnableLogging = InternalEnableLogging,
				CustomLogger = InternalCustomLogger
			};
			Protocol.RxOut += SendRxOut;

			return ConnectionTransport as SimplTransport;
		}

		public void Initialize(IComPort comPort)
		{
			ConnectionTransport = new SampleGatewaySerialTransport
			{
				EnableLogging = InternalEnableLogging,
				CustomLogger = InternalCustomLogger,
				EnableRxDebug = InternalEnableRxDebug,
				EnableTxDebug = InternalEnableTxDebug
			};

			Protocol = new SampleGatewayProtocol(ConnectionTransport, Id)
			{
				EnableLogging = InternalEnableLogging,
				CustomLogger = InternalCustomLogger
			};
			Protocol.RxOut += SendRxOut;
		}

		public override void Connect()
		{
			base.Connect();
			((SampleGatewayProtocol)Protocol).Connect();
		}

		public override void Disconnect()
		{
			base.Disconnect();
			((SampleGatewayProtocol)Protocol).Disconnect();
		}
	}
}