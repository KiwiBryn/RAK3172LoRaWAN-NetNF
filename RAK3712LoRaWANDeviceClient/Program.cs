//---------------------------------------------------------------------------------
// Copyright (c) May 2022, devMobile Software
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// Must have one of following options defined in the project\build definitions
//    PAYLOAD_HEX or PAYLOAD_BYTES
//    OTAA or ABP
//
// Optional definitions
//		DEVICE_DEVEUI_SET
//		DEVICE_FACTORY_SETTINGS
//
//---------------------------------------------------------------------------------
//#define ST_STM32F769I_DISCOVERY      // nanoff --target ST_STM32F769I_DISCOVERY --update 
#define ESP32_WROOM   // nanoff --target ESP32_REV0 --serialport COM17 --update
//#define DEVICE_DEVEUI_SET
//#define FACTORY_RESET
///#define PAYLOAD_BCD
#define PAYLOAD_BYTES
//#define OTAA
//#define ABP
//#define CONFIRMED
//#define UNCONFIRMED
//#define REGION_SET
//#define ADR_SET
//#define SLEEP
namespace devMobile.IoT.LoRaWAN
{
    using System;
    using System.Diagnostics;
    using System.IO.Ports;
    using System.Threading;

#if ESP32_WROOM
	using nanoFramework.Hardware.Esp32; //need NuGet nanoFramework.Hardware.Esp32
#endif

    public class Program
    {
#if ST_STM32F769I_DISCOVERY
		private const string SerialPortId = "COM6";
#endif
#if ESP32_WROOM
		private const string SerialPortId = "COM2";
#endif
		private const string Band = "8-1";
		private static readonly TimeSpan JoinTimeOut = new TimeSpan(0, 0, 10);
		private static readonly TimeSpan SendTimeout = new TimeSpan(0, 0, 10);
#if SLEEP
		private static readonly TimeSpan SleepTimeOut = new TimeSpan(0, 0, 30);
#endif
		private const byte MessagePort = 1;
		private static readonly TimeSpan MessageSendTimerDue = new TimeSpan(0, 0, 15);
		private static readonly TimeSpan MessageSendTimerPeriod = new TimeSpan(0, 1, 0);
		private static Timer MessageSendTimer;
#if PAYLOAD_BCD
		private const string PayloadBcd = "48656c6c6f204c6f526157414e"; // Hello LoRaWAN in BCD
#endif
#if PAYLOAD_BYTES
		private static readonly byte[] PayloadBytes = { 0x48, 0x65, 0x6c, 0x6c, 0x6f, 0x20, 0x4c, 0x6f, 0x52, 0x61, 0x57, 0x41, 0x4e }; // Hello LoRaWAN in bytes
#endif

		public static void Main()
		{
            Result result;

			Debug.WriteLine("devMobile.IoT.RAK3172LoRaWANDeviceClient starting");

			try
			{
				// set GPIO functions for COM2 (this is UART1 on ESP32)
#if ESP32_WROOM
				Configuration.SetPinFunction(Gpio.IO17, DeviceFunction.COM2_TX);
				Configuration.SetPinFunction(Gpio.IO16, DeviceFunction.COM2_RX);
#endif

				Debug.Write("Ports:");
				foreach (string port in SerialPort.GetPortNames())
				{
					Debug.Write($" {port}");
				}
				Debug.WriteLine("");

				using (Rak3172LoRaWanDevice device = new Rak3172LoRaWanDevice())
				{
					result = device.Initialise(SerialPortId, 115200, Parity.None, 8, StopBits.One);
					if (result != Result.Success)
					{
						Debug.WriteLine($"Initialise failed {result}");
						return;
					}

					MessageSendTimer = new Timer(SendMessageTimerCallback, device, Timeout.Infinite, Timeout.Infinite);
					
					device.OnJoinCompletion += OnJoinCompletionHandler;
					device.OnReceiveMessage += OnReceiveMessageHandler;
#if CONFIRMED
					device.OnMessageConfirmation += OnMessageConfirmationHandler;
#endif

#if FACTORY_RESET
					Debug.WriteLine($"{DateTime.UtcNow:hh:mm:ss} FactoryReset");
					result = device.FactoryReset();
					if (result != Result.Success)
					{
						Debug.WriteLine($"FactoryReset failed {result}");
						return;
					}
#endif

#if DEVICE_DEVEUI_SET
					Debug.WriteLine($"{DateTime.UtcNow:hh:mm:ss} Device EUI");
					result = device.DeviceEui(Config.devEui);
					if (result != Result.Success)
					{
						Debug.WriteLine($"DeviceEUI set failed {result}");
						return;
					}
#endif

#if REGION_SET
					Debug.WriteLine($"{DateTime.UtcNow:hh:mm:ss} Region{Band}");
					result = device.Band(Band);
					if (result != Result.Success)
					{
						Debug.WriteLine($"Band on failed {result}");
						return;
					}
#endif

#if ADR_SET
					Debug.WriteLine($"{DateTime.UtcNow:hh:mm:ss} ADR On");
					result = device.AdrOn();
					if (result != Result.Success)
					{
						Debug.WriteLine($"ADR on failed {result}");
						return;
					}
#endif
#if CONFIRMED
					Debug.WriteLine($"{DateTime.UtcNow:hh:mm:ss} Confirmed");
					result = device.UplinkMessageConfirmationOn();
					if (result != Result.Success)
					{
						Debug.WriteLine($"Confirm on failed {result}");
						return;
					}
#endif
#if UNCONFIRMED
					Debug.WriteLine($"{DateTime.UtcNow:hh:mm:ss} Unconfirmed");
					result = device.UplinkMessageConfirmationOff();
					if (result != Result.Success)
					{
						Debug.WriteLine($"Confirm off failed {result}");
						return;
					}
#endif

#if OTAA
					Debug.WriteLine($"{DateTime.UtcNow:hh:mm:ss} OTAA");
					result = device.OtaaInitialise(Config.JoinEui, Config.AppKey);
					if (result != Result.Success)
					{
						Debug.WriteLine($"OTAA Initialise failed {result}");
						return;
					}
#endif

#if ABP
					Debug.WriteLine($"{DateTime.UtcNow:hh:mm:ss} ABP");
					result = device.AbpInitialise(Config.DevAddress, Config.NwksKey, Config.AppsKey);
					if (result != Result.Success)
					{
						Debug.WriteLine($"ABP Initialise failed {result}");
						return;
					}
#endif

					Debug.WriteLine($"{DateTime.UtcNow:hh:mm:ss} Join start Timeout:{JoinTimeOut:hh:mm:ss}");
					result = device.Join(JoinTimeOut);
					if (result != Result.Success)
					{
						Debug.WriteLine($"Join failed {result}");
						return;
					}
					Debug.WriteLine($"{DateTime.UtcNow:hh:mm:ss} Join started");

					Thread.Sleep(Timeout.Infinite);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex.Message);
			}
		}

		private static void OnJoinCompletionHandler(bool result)
		{
			Debug.WriteLine($"{DateTime.UtcNow:hh:mm:ss} Join finished:{result}");

			if (result)
			{
				MessageSendTimer.Change(MessageSendTimerDue, MessageSendTimerPeriod);
			}
		}

		private static void SendMessageTimerCallback(object state)
		{
			Rak3172LoRaWanDevice device = (Rak3172LoRaWanDevice)state;

#if PAYLOAD_HEX
			Debug.WriteLine($"{DateTime.UtcNow:hh:mm:ss} port:{MessagePort} payload HEX:{PayloadHex}");
			Result result = device.Send(MessagePort, PayloadHex, SendTimeout);
#endif
#if PAYLOAD_BYTES
			Debug.WriteLine($"{DateTime.UtcNow:hh:mm:ss} port:{MessagePort} payload bytes:{Rak3172LoRaWanDevice.BytesToHex(PayloadBytes)}");
			Result result = device.Send(MessagePort, PayloadBytes, SendTimeout);
#endif
			if (result != Result.Success)
			{
				Debug.WriteLine($"Send failed {result}");
			}

#if SLEEP
			Debug.WriteLine($"{DateTime.UtcNow:hh:mm:ss} Sleep");
			result = device.Sleep(SleepTimeOut);
			if (result != Result.Success)
			{
				Debug.WriteLine($"Sleep failed {result}");
				return;
			}
#endif
		}

#if CONFIRMED
		static void OnMessageConfirmationHandler(bool confirmed)
		{
			if (confirmed)
			{
				Debug.WriteLine($"{DateTime.UtcNow:hh:mm:ss} Send Confirmed OK");
			}
			else
			{
				Debug.WriteLine($"{DateTime.UtcNow:hh:mm:ss} Send Confirmed failed");
			}
		}
#endif

		static void OnReceiveMessageHandler(byte port, int rssi, int snr, string payloadBcd)
		{
			byte[] payloadBytes = Rak3172LoRaWanDevice.HexToByes(payloadBcd);

			Debug.WriteLine($"{DateTime.UtcNow:hh:mm:ss} Receive Message RSSI:{rssi} SNR:{snr} Port:{port} Payload:{payloadBcd} PayLoadBytes:{BitConverter.ToString(payloadBytes)}");
		}
	}
}
