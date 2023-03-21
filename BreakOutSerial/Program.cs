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
// https://docs.rakwireless.com/Product-Categories/WisDuo/RAK3172-Module/AT-Command-Manual/
//---------------------------------------------------------------------------------
#define SERIAL_ASYNC_READ
//#define SERIAL_THREADED_READ
//#define ST_STM32F769I_DISCOVERY      // nanoff --target ST_STM32F769I_DISCOVERY --update 
//#define SPARKFUN_ESP32_THING_PLUS   // nanoff --platform esp32 --serialport COM4 --update
#define RAK_WISBLOCK_RAK2305 // nanoff --update --target ESP32_PSRAM_REV0 --serialport COM4
// May 2022 Still experiencing issues with ComPort assignments
//#define NETDUINO3_WIFI   // nanoff --target NETDUINO3_WIFI --update
//#define ST_NUCLEO64_F091RC // nanoff --target ST_NUCLEO64_F091RC --update 
//#define ST_NUCLEO144_F746ZG //nanoff --target ST_NUCLEO144_F746ZG --update

namespace devMobile.IoT.LoRaWAN.nanoFramework.RAK3172
{
	using System;
	using System.Diagnostics;
	using System.IO.Ports;
	using System.Threading;
#if SPARKFUN_ESP32_THING_PLUS || RAK_WISBLOCK_RAK2305
   using global::nanoFramework.Hardware.Esp32; //need NuGet nanoFramework.Hardware.Esp32
#endif

   public class Program
	{
		private static SerialPort _SerialPort;
#if SERIAL_THREADED_READ
		private static Boolean _Continue = true;
#endif
#if SPARKFUN_ESP32_THING_PLUS
		private const string SerialPortId = "COM2";
#endif
#if RAK_WISBLOCK_RAK2305
      private const string SerialPortId = "COM2";
#endif
#if NETDUINO3_WIFI
      private const string SerialPortId = "COM3";
#endif
#if ST_NUCLEO64_F091RC
      private const string SerialPortId = "";
#endif
#if ST_NUCLEO144_F746ZG
      private const string SerialPortId = "";
#endif
#if ST_STM32F429I_DISCOVERY
      private const string SerialPortId = "";
#endif
#if ST_STM32F769I_DISCOVERY
		private const string SerialPortId = "COM6";
#endif

      public static void Main()
		{
#if SERIAL_THREADED_READ
			Thread readThread = new Thread(SerialPortProcessor);
#endif

			Debug.WriteLine("devMobile.IoT.LoRaWAN.nanoFramework.RAK3172 BreakoutSerial starting");

			try
			{
            // set GPIO functions for COM2 (this is UART1 on ESP32)
#if SPARKFUN_ESP32_THING_PLUS
				Configuration.SetPinFunction(Gpio.IO17, DeviceFunction.COM2_TX);
				Configuration.SetPinFunction(Gpio.IO16, DeviceFunction.COM2_RX);
#endif
#if RAK_WISBLOCK_RAK2305
            Configuration.SetPinFunction(Gpio.IO21, DeviceFunction.COM2_TX);
				Configuration.SetPinFunction(Gpio.IO19, DeviceFunction.COM2_RX);
#endif

            Debug.Write("Ports:");
				foreach (string port in SerialPort.GetPortNames())
				{
					Debug.Write($" {port}");
				}
				Debug.WriteLine("");

				using (_SerialPort = new SerialPort(SerialPortId))
				{
					// set parameters
					_SerialPort.BaudRate = 115200;
					_SerialPort.Parity = Parity.None;
					_SerialPort.DataBits = 8;
					_SerialPort.StopBits = StopBits.One;
					_SerialPort.Handshake = Handshake.None;
					_SerialPort.NewLine = "\r\n";
					_SerialPort.ReadTimeout = 1000;

               //_SerialPort.WatchChar = '\n'; // May 2022 WatchChar event didn't fire github issue https://github.com/nanoframework/Home/issues/1035

#if SERIAL_ASYNC_READ
               _SerialPort.DataReceived += SerialDevice_DataReceived;
#endif

					_SerialPort.Open();

					_SerialPort.WatchChar = '\n';

					_SerialPort.ReadExisting(); // Running at 115K2 this was necessary

#if SERIAL_THREADED_READ
					readThread.Start();
#endif

					for (int i = 0; i < 5; i++)
					{
						string atCommand;
						atCommand = "AT+VER=?";
						//atCommand = "AT+SN=?"; // Empty response?
						//atCommand = "AT+HWMODEL=?";
						//atCommand = "AT+HWID=?";
						//atCommand = "AT+DEVEUI=?";
						//atCommand = "AT+APPEUI=?";
						//atCommand = "AT+APPKEY=?";
						//atCommand = "ATR";
						//atCommand = "AT+SLEEP=4000";
						//atCommand = "AT+ATM";
						//atCommand = "AT+NWM=1";
						//atCommand = "AT?";
						//atCommand = "+++";
						Debug.WriteLine("");
						Debug.WriteLine($"{DateTime.UtcNow:hh:mm:ss} {i} TX:{atCommand} bytes:{atCommand.Length}--------------------------------");
						_SerialPort.WriteLine(atCommand);

						Thread.Sleep(5000);
					}
				}
#if SERIAL_THREADED_READ
				_Continue = false;
#endif
				Debug.WriteLine("Done");
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex.Message);
			}
		}

#if SERIAL_ASYNC_READ
		private static void SerialDevice_DataReceived(object sender, SerialDataReceivedEventArgs e)
		{
			SerialPort serialPort = (SerialPort)sender;

			switch (e.EventType)
			{
				case SerialData.Chars:
					break;

				case SerialData.WatchChar:
					string response = serialPort.ReadExisting();
					//Debug.Write($"{DateTime.UtcNow:hh:mm:ss} RX:{response} bytes:{response.Length}");
					Debug.Write(response);
					break;
				default:
					Debug.Assert(false, $"e.EventType {e.EventType} unknown");
					break;
			}
		}
#endif

#if SERIAL_THREADED_READ
		public static void SerialPortProcessor()
		{

			while (_Continue)
			{
				try
				{
					string response = _SerialPort.ReadLine();
					//string response = _SerialPort.ReadExisting();
					Debug.Write(response);
				}
				catch (TimeoutException ex) 
				{
					Debug.WriteLine($"Timeout:{ex.Message}");
				}
			}
		}
#endif
	}
}