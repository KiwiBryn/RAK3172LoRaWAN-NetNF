//---------------------------------------------------------------------------------
// Copyright (c) Julky 2022, devMobile Software
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
// https://docs.rakwireless.com/RUI3/Serial-Operating-Modes/AT-Command-Manual/#overview
//---------------------------------------------------------------------------------
//#define ST_STM32F769I_DISCOVERY      // nanoff --target ST_STM32F769I_DISCOVERY --update 
#define ESP32_WROOM   //nanoff --target ESP32_REV0 --serialport COM17 --update

namespace devMobile.IoT.LoRaWAN.nanoFramework.RAK3172
{
	using System;
	using System.Diagnostics;
	using System.IO.Ports;
	using System.Threading;
#if ESP32_WROOM
	using global::nanoFramework.Hardware.Esp32; //need NuGet nanoFramework.Hardware.Esp32
#endif

	public class Program
	{
#if ESP32_WROOM
		private const string SerialPortId = "COM2";
#endif
#if ST_STM32F769I_DISCOVERY
		private const string SerialPortId = "COM6";
#endif
		private const string DevEui = "...";
		private const string DevAddress = "...";
		private const string NwksKey = "...";
		private const string AppsKey = "..."; 
		private const byte MessagePort = 1;
		private const string Payload = "A0EEE456D02AFF4AB8BAFD58101D2A2A"; // Hello LoRaWAN

		public static void Main()
		{
			Debug.WriteLine("devMobile.IoT.LoRaWAN.nanoFramework.RAK3172.NetworkJoinABP starting");

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

				using (SerialPort serialPort = new SerialPort(SerialPortId))
				{
					// set parameters
					serialPort.BaudRate = 115200;
					serialPort.Parity = Parity.None;
					serialPort.DataBits = 8;
					serialPort.StopBits = StopBits.One;
					serialPort.Handshake = Handshake.None;
					serialPort.NewLine = "\r\n";
					serialPort.ReadTimeout = 1000;

					serialPort.DataReceived += SerialDevice_DataReceived;

					serialPort.Open();

					serialPort.WatchChar = '\n';

					serialPort.ReadExisting(); // Running at 115K2 this was necessary

					// Set the Device EUI
					Console.WriteLine("Set Device EUI");
					serialPort.WriteLine($"AT+DEVEUI={DevEui}");

					// Set the Working mode to LoRaWAN
					Console.WriteLine("Set Work mode");
					serialPort.WriteLine("AT+NWM=1");

					// Set the Region to AS923
					Console.WriteLine("Set Region");
					serialPort.WriteLine("AT+BAND=8");

					// Set the JoinMode
					Console.WriteLine("Set Join mode");
					serialPort.WriteLine("AT+NJM=0");

					// Set the DevAddress
					Console.WriteLine("Set Device Address");
					serialPort.WriteLine($"AT+DEVADDR={DevAddress}");

					// Set the Network Session Key
					Console.WriteLine("Set NwksKey");
					serialPort.WriteLine($"AT+NWKSKEY={NwksKey}");

					// Set the Application Session Key
					Console.WriteLine("Set AppsKey");
					serialPort.WriteLine($"AT+APPSKEY={AppsKey}");

					// Set the Confirm flag
					Console.WriteLine("Set Confirm off");
					serialPort.WriteLine("AT+CFM=0");

					// Join the network
					Console.WriteLine("Start Join");
					serialPort.WriteLine("AT+JOIN=1:0:10:2");

					// Wait for the +EVT:JOINED

					while (true)
					{
						Console.WriteLine("Sending");
						serialPort.WriteLine($"AT+SEND={MessagePort}:{Payload}");

						Thread.Sleep(300000);
					}
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex.Message);
			}
		}

		private static void SerialDevice_DataReceived(object sender, SerialDataReceivedEventArgs e)
		{
			SerialPort serialPort = (SerialPort)sender;

			switch (e.EventType)
			{
				case SerialData.Chars:
					break;

				case SerialData.WatchChar:
					string response = serialPort.ReadExisting();
					Debug.Write(response);
					break;
				default:
					Debug.Assert(false, $"e.EventType {e.EventType} unknown");
					break;
			}
		}
	}
}