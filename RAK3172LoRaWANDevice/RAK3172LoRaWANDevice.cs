﻿//---------------------------------------------------------------------------------
// Copyright (c) July 2022, devMobile Software
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
//---------------------------------------------------------------------------------
#define DIAGNOSTICS
namespace devMobile.IoT.LoRaWAN
{
	using System;
#if DIAGNOSTICS
	using System.Diagnostics;
#endif
	using System.IO.Ports;
    using System.Text;
    using System.Threading;

	/// <summary>
	/// The LoRaWAN device classes. From The Things Network definitions
	/// </summary>
	public enum LoRaWANDeviceClass
	{
		Undefined = 0,
		/// <summary>
		/// Class A devices support bi-directional communication between a device and a gateway. Uplink messages (from 
		/// the device to the server) can be sent at any time. The device then opens two receive windows at specified 
		/// times (RX1 Delay and RX2 Delay) after an uplink transmission. If the server does not respond in either of 
		/// these receive windows, the next opportunity will be after the next uplink transmission from the device. 
		A,
		/// <summary>
		/// Class B devices extend Class A by adding scheduled receive windows for downlink messages from the server. 
		/// Using time-synchronized beacons transmitted by the gateway, the devices periodically open receive windows. 
		/// The time between beacons is known as the beacon period, and the time during which the device is available 
		/// to receive downlinks is a “ping slot.”
		/// </summary>
		B,
		/// <summary>
		/// Class C devices extend Class A by keeping the receive windows open unless they are transmitting, as shown 
		/// in the figure below. This allows for low-latency communication but is many times more energy consuming than 
		/// Class A devices.
		/// </summary>
		C
	}

	/// <summary>
	/// Possible results of library methods (combination of RAK3172 AT command and state machine errors)
	/// </summary>
	public enum Result
	{
		Undefined = 0,
		/// <summary>
		/// Command executed without error.
		/// </summary>
		Success,
		/// <summary>
		/// Command failed to complete in configured duration.
		/// </summary>
		Timeout,
		/// <summary>
		/// Generic error or input is not supported.
		/// </summary>
		AtError,
		/// <summary>
		/// The input parameter of the command is wrong.
		/// </summary>
		ParameterError,
		/// <summary>
		/// The network is busy so the command is not completed.
		/// </summary>
		BusyError,
		/// <summary>
		/// The parameter is too long.
		/// </summary>
		ParameterOverflow,
		/// <summary>
		/// Device is not yet joined to a network.
		/// </summary>
		NotJoined,
		/// <summary>
		/// Error detected during the reception of the command.
		/// </summary>
		ReceiveError,
		/// <summary>
		/// Duty cycle limited and cannot send data.
		/// </summary>
		DutyCycleRestricted
	}

	/// <summary>
	/// RAK3172 client implementation (LoRaWAN only).
	/// </summary>
	public sealed class Rak3172LoRaWanDevice : IDisposable
	{
		/// <summary>
		/// The DevEUI is a 64-bit globally-unique Extended Unique Identifier (EUI-64) assigned by the manufacturer, or
		/// the owner, of the end-device. This is represented by a 16 character long string
		/// </summary>
		public const byte DevEuiLength = 16;
		/// <summary>
		/// The JoinEUI(formerly known as AppEUI) is a 64-bit globally-unique Extended Unique Identifier (EUI-64).Each 
		/// Join Server, which is used for authenticating the end-devices, is identified by a 64-bit globally unique 
		/// identifier, JoinEUI, that is assigned by either the owner or the operator of that server. This is 
		/// represented by a 16 character long string.
		/// </summary>
		public const byte JoinEuiLength = 16;
		/// <summary>
		/// The AppKey is the encryption key between the source of the message (based on the DevEUI) and the destination 
		/// of the message (based on the AppEUI). This key must be unique for each device. This is represented by a 32 
		/// character long string
		/// </summary>
		public const byte AppKeyLength = 32;
		/// <summary>
		/// The DevAddr is composed of two parts: the address prefix and the network address. The address prefix is 
		/// allocated by the LoRa Alliance® and is unique to each network that has been granted a NetID. This is 
		/// represented by an 8 character long string.
		/// </summary>
		public const byte DevAddrLength = 8;
		/// <summary>
		/// After activation, the Network Session Key(NwkSKey) is used to secure messages which do not carry a payload.
		/// </summary>
		public const byte NwsKeyLength = 32;
		/// <summary>
		/// The AppSKey is an application session key specific for the end-device. It is used by both the application 
		/// server and the end-device to encrypt and decrypt the payload field of application-specific data messages.
		/// This is represented by an 32 character long string
		/// </summary>
		public const byte AppsKeyLength = 32;
		/// <summary>
		/// The minimum supported port number. Port 0 is used for FRMPayload which contains MAC commands only.
		/// </summary>
		public const byte MessagePortMinimumValue = 1;
		/// <summary>
		/// The maximum supported port number. Port 224 is used for the LoRaWAN Mac layer test protocol. Ports 
		/// 223…255 are reserved for future application extensions.
		/// </summary>
		public const byte MessagePortMaximumValue = 223;
		/// <summary>
		/// The minimum interval (in seconds) between join attempt retries.
		/// </summary>
		public const ushort JoinRetryIntervalMinimum = 7;

		private readonly TimeSpan CommandTimeoutDefault = new TimeSpan(0, 0, 5);

		private SerialPort serialDevice = null;

		private string ATCommandExpectedResponse;
		private readonly AutoResetEvent ATCommandResponseExpectedEvent;
		private StringBuilder Response;
		private Result Result;

		/// <summary>
		/// Event handler called when network join process completed.
		/// </summary>
		/// <param name="joinSuccessful">Was the network join attempt successful</param>
		public delegate void JoinCompletionHandler(bool joinSuccessful);
		public JoinCompletionHandler OnJoinCompletion;
		/// <summary>
		/// Event handler called when uplink message delivery to network confirmed
		/// </summary>
		public delegate void MessageConfirmationHandler(bool succesful);
		public MessageConfirmationHandler OnMessageConfirmation;
		/// <summary>
		/// Event handler called when downlink message received.
		/// </summary>
		/// <param name="port">LoRaWAN Port number.</param>
		/// <param name="rssi">Received Signal Strength Indicator(RSSI).</param>
		/// <param name="snr">Signal to Noise Ratio(SNR).</param>
		/// <param name="payload">Hexadecimal representation of payload.</param>
		public delegate void ReceiveMessageHandler(byte port, int rssi, int snr, string payload);
		public ReceiveMessageHandler OnReceiveMessage;

		public Rak3172LoRaWanDevice()
		{
			this.Response = new StringBuilder(512);
			this.ATCommandResponseExpectedEvent = new AutoResetEvent(false);
		}

		/// <summary>
		/// Initializes a new instance of the devMobile.IoT.LoRaWAN.NetCore.RAK3172.Rak3172LoRaWanDevice class using the
		/// specified port name, baud rate, parity bit, data bits, and stop bit.
		/// </summary>
		/// <param name="serialPortId">The port to use (for example, COM1).</param>
		/// <param name="baudRate">The baud rate, 600 to 115K2.</param>
		/// <param name="serialParity">One of the System.IO.Ports.SerialPort.Parity values, defaults to None.</param>
		/// <param name="dataBits">The data bits value, defaults to 8.</param>
		/// <param name="stopBits">One of the System.IO.Ports.SerialPort.StopBits values, defaults to One.</param>
		/// <exception cref="System.IO.IOException">The serial port could not be found or opened.</exception>
		/// <exception cref="UnauthorizedAccessException">The application does not have the required permissions to open the serial port.</exception>
		/// <exception cref="ArgumentNullException">The serialPortId is null.</exception>
		/// <exception cref="ArgumentException">The specified serialPortId, baudRate, serialParity, dataBits, or stopBits is invalid.</exception>
		/// <exception cref="InvalidOperationException">The attempted operation was invalid e.g. the port was already open.</exception>
		/// <returns><see cref="Result"/> of the operation.</returns>
		public Result Initialise(string serialPortId, int baudRate, Parity serialParity = Parity.None, ushort dataBits = 8, StopBits stopBits = StopBits.One)
		{
			if (serialPortId == null)
			{
				throw new ArgumentNullException(nameof(serialPortId));
			}

			if (serialPortId == string.Empty)
			{
				throw new ArgumentException(nameof(serialPortId));
			}

			serialDevice = new SerialPort(serialPortId)
			{
				BaudRate = baudRate,
				Parity = serialParity,
				StopBits = stopBits,
				Handshake = Handshake.None,
				DataBits = dataBits,

				// BHL necessary?
				ReadTimeout = 10000,
				ReadBufferSize = 512,

				NewLine = "\r\n"
			};

			serialDevice.DataReceived += SerialDevice_DataReceived;

			serialDevice.Open();

			serialDevice.WatchChar = '\n';

			// clear out the input buffer.
			serialDevice.ReadExisting();

			// Set the Working mode to LoRaWAN, not/never going todo P2P with this library.
#if DIAGNOSTICS
         Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+NWM=1");
#endif
			Result result = SendCommand("Current Work Mode: LoRaWAN.", "AT+NWM=1", CommandTimeoutDefault);
			if (result != Result.Success)
			{
#if DIAGNOSTICS
            Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+NWM=1 failed {result}");
#endif
				return result;
			}

			return Result.Success;
		}

		/// <summary>
		/// Sets the DeviceEUI
		/// </summary>
		/// <param name="deviceEui">The device EUI.</param>
		/// <exception cref="ArgumentNullException">The device EUI value is null.</exception>
		/// <exception cref="System.IO.ArgumentException">The deviceEui length is incorrect.</exception>
		/// <returns><see cref="Result"/> of the operation.</returns>
		public Result DeviceEui(string deviceEui)
		{
			if (deviceEui == null)
			{
				throw new ArgumentNullException(nameof(deviceEui), $"DeviceEUI is invalid");
			}

			if (deviceEui.Length != DevEuiLength)
			{
				throw new ArgumentException($"DevEUI invalid length must be {DevEuiLength} characters", nameof(deviceEui));
			}

#if DIAGNOSTICS
			Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+DEVEUI={deviceEui}");
#endif
			Result result = SendCommand("OK", $"AT+DEVEUI={deviceEui}", CommandTimeoutDefault);
			if (result != Result.Success)
			{
#if DIAGNOSTICS
				Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+DEVEUI failed {result}");
#endif
				return result;
			}

			return Result.Success;
		}

		/// <summary>
		/// Sets the LoRaWAN device class.
		/// </summary>
		/// <param name="loRaClass" cref="LoRaWANDeviceClass">The LoRaWAN device class</param>
		/// <exception cref="System.IO.ArgumentException">The loRaClass is invalid.</exception>
		/// <returns><see cref="Result"/> of the operation.</returns>

		public Result Class(LoRaWANDeviceClass loRaClass)
		{
			string command;

			switch (loRaClass)
			{
				case LoRaWANDeviceClass.A:
					command = "AT+CLASS=A";
					break;
				case LoRaWANDeviceClass.B:
					command = "AT+CLASS=B";
					break;
				case LoRaWANDeviceClass.C:
					command = "AT+CLASS=C";
					break;
				default:
					throw new ArgumentException($"LoRa class value {loRaClass} invalid", nameof(loRaClass));
			}

			// Set the class
#if DIAGNOSTICS
         Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} {command}");
#endif
			Result result = SendCommand("OK", command, CommandTimeoutDefault);
			if (result != Result.Success)
			{
#if DIAGNOSTICS
            Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} {command} failed {result}");
#endif
				return result;
			}

			return Result.Success;
		}

		/// <summary>
		/// Disables uplink message confirmations.
		/// </summary>
		/// <returns><see cref="Result"/> of the operation.</returns>
		public Result UplinkMessageConfirmationOff()
		{
            // Set the confirmation type
#if DIAGNOSTICS
         Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+CFM=0");
#endif
            Result result = SendCommand("OK", "AT+CFM=0", CommandTimeoutDefault);
            if (result != Result.Success)
			{
#if DIAGNOSTICS
            Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+CFM=0 failed {result}");
#endif
				return result;
			}

			return Result.Success;
		}

		/// <summary>
		/// Enables uplink message confirmations.
		/// </summary>
		/// <returns><see cref="Result"/> of the operation.</returns>
		public Result UplinkMessageConfirmationOn()
		{
			// Set the confirmation type
#if DIAGNOSTICS
         Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+CFM=1");
#endif
			Result result = SendCommand("OK", "AT+CFM=1", CommandTimeoutDefault);
			if (result != Result.Success)
			{
#if DIAGNOSTICS
            Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+CFM=1 failed {result}");
#endif
				return result;
			}

			return Result.Success;
		}

		/// <summary>
		/// Sets the band/region. Doesn't use region codes like many other modules, considered using EU868, US915 etc. but 
		/// wan't certain how to map 8,8-1,8-2,8-3,8-4 to AS923?.
		/// </summary>
		/// <param name="band">The band which is a LoRaWAN region code plus optional regional configuration settings.</param>
		/// <exception cref="ArgumentNullException">The band value is null.</exception>
		/// <returns><see cref="Result"/> of the operation.</returns>
		public Result Band(string band)
		{
			if (band == null)
			{
				throw new ArgumentNullException(nameof(band), $"Band is invalid");
			}

#if DIAGNOSTICS
         Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+BAND={band}");
#endif
			Result result = SendCommand("OK", $"AT+BAND={band}", CommandTimeoutDefault);
			if (result != Result.Success)
			{
#if DIAGNOSTICS
            Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+BAND failed {result}");
#endif
				return result;
			}

			return Result.Success;
		}

		/// <summary>
		/// Disables Adaptive Data Rate(ADR) support.
		/// </summary>
		/// <returns><see cref="Result"/> of the operation.</returns>
		public Result AdrOff()
		{
#if DIAGNOSTICS
         Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+ADR=0");
#endif
            Result result = SendCommand("OK", "AT+ADR=0", CommandTimeoutDefault);
            if (result != Result.Success)
			{
#if DIAGNOSTICS
            Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+ADR=0 failed {result}");
#endif
				return result;
			}

			return Result.Success;
		}

		/// <summary>
		/// Enables Adaptive Data Rate(ADR) support
		/// </summary>
		/// <returns><see cref="Result"/> of the operation.</returns>
		public Result AdrOn()
		{
#if DIAGNOSTICS
         Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+ADR=1");
#endif
            Result result = SendCommand("OK", "AT+ADR=1", CommandTimeoutDefault);
            if (result != Result.Success)
			{
#if DIAGNOSTICS
            Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+ADR=1 failed {result}");
#endif
				return result;
			}

			return Result.Success;
		}

		/// <summary>
		/// Configures the device to use Activation By Personalisation(ABP) to connect to the LoRaWAN network
		/// </summary>
		/// <param name="devAddr">The device address<see cref="DevAddrLength"></param>
		/// <param name="nwksKey">The network sessions key<see cref="NwsKeyLength"> </param>
		/// <param name="appsKey">The application session key <see cref="AppsKeyLength"/></param>
		/// <exception cref="System.IO.ArgumentNullException">The devAddr, nwksKey or appsKey is null.</exception>
		/// <exception cref="System.IO.ArgumentException">The devAddr, nwksKey or appsKey length is incorrect.</exception>
		/// <returns><see cref="Result"/> of the operation.</returns>
		public Result AbpInitialise(string devAddr, string nwksKey, string appsKey)
		{
			Result result;

			if (devAddr == null)
			{
				throw new ArgumentNullException(nameof(devAddr));
			}

			if (devAddr.Length != DevAddrLength)
			{
				throw new ArgumentException($"devAddr invalid length must be {DevAddrLength} characters", nameof(devAddr));
			}

			if (nwksKey == null)
			{
				throw new ArgumentNullException(nameof(nwksKey));
			}

			if (nwksKey.Length != NwsKeyLength)
			{
				throw new ArgumentException($"nwksKey invalid length must be {NwsKeyLength} characters", nameof(nwksKey));
			}

			if (appsKey == null)
			{
				throw new ArgumentNullException(nameof(appsKey));
			}

			if (appsKey.Length != AppsKeyLength)
			{
				throw new ArgumentException($"appsKey invalid length must be {AppsKeyLength} characters", nameof(appsKey));
			}

			// Set the network join mode to ABP
#if DIAGNOSTICS
         Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+NJM=0");
#endif
			result = SendCommand("OK", "AT+NJM=0", CommandTimeoutDefault);
			if (result != Result.Success)
			{
#if DIAGNOSTICS
            Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+NJM=0 failed {result}" );
#endif
				return result;
			}

			// set the devAddr
#if DIAGNOSTICS
         Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+DEVADDR={devAddr}");
#endif
			result = SendCommand("OK", $"AT+DEVADDR={devAddr}", CommandTimeoutDefault);
			if (result != Result.Success)
			{
#if DIAGNOSTICS
            Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+DEVADDR failed {result}");
#endif
				return result;
			}

			// Set the nwsKey
#if DIAGNOSTICS
         Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+NWKSKEY={nwksKey}");
#endif
			result = SendCommand("OK", $"AT+NWKSKEY={nwksKey}", CommandTimeoutDefault);
			if (result != Result.Success)
			{
#if DIAGNOSTICS
				Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+NWKSKEY failed {result}");
#endif
				return result;
			}

			// Set the appsKey
#if DIAGNOSTICS
         Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+APPSKEY={appsKey}");
#endif
			result = SendCommand("OK", $"AT+APPSKEY={appsKey}", CommandTimeoutDefault);
			if (result != Result.Success)
			{
#if DIAGNOSTICS
				Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+APPSKEY failed {result}");
#endif
				return result;
			}

			return Result.Success;
		}

		/// <summary>
		/// Configures the device to use Over The Air Activation(OTAA) to connect to the LoRaWAN network
		/// </summary>
		/// <param name="joinEui">The join server unique identifier <see cref="JoinEuiLength"/></param>
		/// <param name="appKey">The application key<see cref="AppKeyLength"/> </param>
		/// <exception cref="System.IO.ArgumentNullException">The joinEui or appKey is null.</exception>
		/// <exception cref="System.IO.ArgumentException">The joinEui or appKey length is incorrect.</exception>
		/// <returns><see cref="Result"/> of the operation.</returns>
		public Result OtaaInitialise(string joinEui, string appKey)
		{
			Result result;

			if (joinEui == null)
			{
				throw new ArgumentNullException(nameof(joinEui));
			}

			if (joinEui.Length != JoinEuiLength)
			{
				throw new ArgumentException($"appEui invalid length must be {JoinEuiLength} characters", nameof(joinEui));
			}

			if (appKey == null)
			{
				throw new ArgumentNullException(nameof(appKey));
			}

			if (appKey.Length != AppKeyLength)
			{
				throw new ArgumentException($"appKey invalid length must be {AppKeyLength} characters", nameof(appKey));
			}

			// Set the Network Join Mode to OTAA
#if DIAGNOSTICS
         Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+NJM=1");
#endif
			result = SendCommand("OK", "AT+NJM=1", CommandTimeoutDefault);
			if (result != Result.Success)
			{
#if DIAGNOSTICS
            Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+NJM=1 failed {result}");
#endif
				return result;
			}

			// Set the appEUI
#if DIAGNOSTICS
         Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+APPEUI={joinEui}");
#endif
			result = SendCommand("OK", $"AT+APPEUI={joinEui}", CommandTimeoutDefault);
			if (result != Result.Success)
			{
#if DIAGNOSTICS
            Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+APPEUI= failed {result}");
#endif
				return result;
			}

			// Set the appKey
#if DIAGNOSTICS
         Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+APPKEY={appKey}");
#endif
			result = SendCommand("OK", $"AT+APPKEY={appKey}", CommandTimeoutDefault);
			if (result != Result.Success)
			{
#if DIAGNOSTICS
            Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+APPKEY= failed {result}");
#endif
				return result;
			}

			return Result.Success;
		}

		/// <summary>
		/// Starts the process which Joins device to the network
		/// </summary>
		/// <param name="JoinAttempts">Number of attempts made to join the network</param>
		/// <param name="retryIntervalSeconds">Delay between attempts to join the network</param>
		/// <returns><see cref="Result"/> of the operation.</returns>
		public Result Join(TimeSpan timeout, byte JoinAttempts = 0, byte retryIntervalSeconds = 8)
		{
			if (retryIntervalSeconds < JoinRetryIntervalMinimum)
			{
				throw new ArgumentException($"retryInterval invalid must be >= {JoinRetryIntervalMinimum} seconds", nameof(retryIntervalSeconds));
			}

#if DIAGNOSTICS
         Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+JOIN");
#endif
			Result result = SendCommand("OK", $"AT+JOIN=1:0:{retryIntervalSeconds}:{JoinAttempts}", timeout);
			if (result != Result.Success)
			{
#if DIAGNOSTICS
            Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+JOIN failed {result}");
#endif
				return result;
			}

			return Result.Success;
		}

		/// <summary>
		/// Sends an uplink message in Hexadecimal format
		/// </summary>
		/// <param name="port">LoRaWAN Port number.</param>
		/// <param name="payload">Hexadecimal encoded bytes to send</param>
		/// <exception cref="ArgumentNullException">The payload string is null.</exception>
		/// <exception cref="ArgumentException">The payload string must be a multiple of 2 characters long.</exception>
		/// <exception cref="ArgumentException">The port is number is out of range must be <see cref="MessagePortMinimumValue"/> to <see cref="MessagePortMaximumValue"/>.</exception>
		/// <returns><see cref="Result"/> of the operation.</returns>
		public Result Send(byte port, string payload, TimeSpan timeout)
		{
			if ((port < MessagePortMinimumValue) || (port > MessagePortMaximumValue))
			{
				throw new ArgumentException($"Port invalid must be {MessagePortMinimumValue} to {MessagePortMaximumValue}", nameof(port));
			}

			if (payload == null)
			{
				throw new ArgumentNullException(nameof(payload));
			}

			if ((payload.Length % 2) != 0)
			{
				throw new ArgumentException("Payload length invalid must be a multiple of 2", nameof(payload));
			}

			// Send message the network
#if DIAGNOSTICS
         Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+SEND={port}:payload {payload}");
#endif
			Result result = SendCommand("OK", $"AT+SEND={port}:{payload}", timeout);
			if (result != Result.Success)
			{
#if DIAGNOSTICS
            Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+SEND failed {result}");
#endif
				return result;
			}

			return Result.Success;
		}

		/// <summary>
		/// Sends an uplink message of array of bytes with a sepcified port number.
		/// </summary>
		/// <param name="port">LoRaWAN Port number.</param>
		/// <param name="payload">Array of bytes to send</param>
		/// <exception cref="ArgumentNullException">The payload array is null.</exception>
		/// <returns><see cref="Result"/> of the operation.</returns>
		/// <summary>
		/// Sends an uplink message of array of bytes with a sepcified port number.
		/// </summary>
		/// <param name="port">LoRaWAN Port number.</param>
		/// <param name="payload">Array of bytes to send</param>
		/// <exception cref="ArgumentNullException">The payload array is null.</exception>
		/// <returns><see cref="Result"/> of the operation.</returns>
		public Result Send(byte port, byte[] payload, TimeSpan timeout)
		{
			if ((port < MessagePortMinimumValue) || (port > MessagePortMaximumValue))
			{
				throw new ArgumentException($"Port invalid must be greater than or equal to {MessagePortMinimumValue} and less than or equal to {MessagePortMaximumValue}", nameof(port));
			}

			if (payload == null)
			{
				throw new ArgumentNullException(nameof(payload));
			}

			string payloadHex = BytesToHex(payload);

			// Send message the network
#if DIAGNOSTICS
         Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+SEND=:{port} payload {payloadHex}");
#endif
			Result result = SendCommand("OK", $"AT+SEND={port}:{payloadHex}", timeout);
			if (result != Result.Success)
			{
#if DIAGNOSTICS
            Debug.WriteLine($" {DateTime.UtcNow:hh:mm:ss} AT+SEND failed {result}");
#endif
				return result;
			}

			return Result.Success;
		}

		private Result SendCommand(string expectedResponse, string command, TimeSpan timeout)
		{
			this.ATCommandExpectedResponse = expectedResponse;

			serialDevice.WriteLine(command);

			this.ATCommandResponseExpectedEvent.Reset();

			if (!this.ATCommandResponseExpectedEvent.WaitOne((int)timeout.TotalMilliseconds, false))
			{
				return Result.Timeout;
			}

			this.ATCommandExpectedResponse = string.Empty;

			return Result;
		}

		private void SerialDevice_DataReceived(object sender, SerialDataReceivedEventArgs e)
		{
			// we only care if got EoL character
			if (e.EventType != SerialData.WatchChar)
			{
				return;
			}

			SerialPort serialDevice = (SerialPort)sender;

			Response.Append(serialDevice.ReadExisting());

			int eolPosition;
			do
			{
				// extract a line
				eolPosition = Response.ToString().IndexOf(serialDevice.NewLine);

				if (eolPosition != -1)
				{
					string line = Response.ToString(0, eolPosition);

					Response = Response.Remove(0, eolPosition + serialDevice.NewLine.Length);
#if DIAGNOSTICS
					Debug.WriteLine($" Line :{line} ResponseExpected:{ATCommandExpectedResponse} Response:{Response}");
#endif
					if (ATCommandExpectedResponse != string.Empty)
					{
						int successIndex = line.IndexOf(ATCommandExpectedResponse);
						if (successIndex != -1)
						{
							Result = Result.Success;

							ATCommandResponseExpectedEvent.Set();

							continue;
						}
					}

					// See if device successfully joined network
					if (line.StartsWith("+EVT:JOINED"))
					{
						OnJoinCompletion?.Invoke(true);

						continue;
					}

					// See if device successfully joined network
					if (line.StartsWith("+EVT:JOIN FAILED"))
					{
						OnJoinCompletion?.Invoke(false);

						continue;
					}

					// Applicable only if confirmed messages enabled 
					if (line.StartsWith("+EVT:SEND CONFIRMED OK"))
					{
						OnMessageConfirmation?.Invoke(true);

						continue;
					}

					if (line.StartsWith("+EVT:SEND CONFIRMED FAILED"))
					{
						OnMessageConfirmation?.Invoke(false);

						continue;
					}

					// +EVT:RX_1:-34:15:UNICAST:15:000102030405
					if (line.StartsWith("+EVT:RX_1") || line.StartsWith("+EVT:RX_2") || line.StartsWith("+EVT:RX_3") || line.StartsWith("+EVT:RX_C"))
					{
						// TODO beef up validation, nto certain what todo if borked
						string[] metricsFields = line.Split(':');

						int rssi = int.Parse(metricsFields[2]);
						int snr = int.Parse(metricsFields[3]);
						byte port = byte.Parse(metricsFields[5]);

						string payload = metricsFields[6];

						if (this.OnReceiveMessage != null)
						{
							OnReceiveMessage(port, rssi, snr, payload);
                        }

						continue;
					}

					switch (line)
					{
						case "OK":
							Result = Result.Success;
							break;
						case "AT_ERROR":
							Result = Result.AtError;
							break;
						case "AT_PARAM_ERROR":
							Result = Result.ParameterError;
							break;
						case "AT_BUSY_ERROR":
							Result = Result.BusyError;
							break;
						case "AT_TEST_PARAM_OVERFLOW":
							Result = Result.ParameterOverflow;
							break;
						case "AT_NO_NETWORK_JOINED":
							Result = Result.NotJoined;
							break;
						case "AT_RX_ERROR":
							Result = Result.ReceiveError;
							break;
						case "AT_DUTYCYLE_RESTRICTED":
							Result = Result.DutyCycleRestricted;
							break;
						default:
							break;
					}

					ATCommandResponseExpectedEvent.Set();
                }
            }
			while (eolPosition != -1);
		}


		// Utility functions for clients for processing messages payloads to be send, ands messages payloads received.

		/// <summary>
		/// Converts an array of byes to a hexadecimal string.
		/// </summary>
		/// <param name="payloadBytes"></param>
		/// <exception cref="ArgumentNullException">The array of bytes is null.</exception>
		/// <returns>String containing hex encoded bytes</returns>
		public static string BytesToHex(byte[] payloadBytes)
		{
			if (payloadBytes == null)
			{
				throw new ArgumentNullException(nameof(payloadBytes));
			}

			StringBuilder payloadBcd = new StringBuilder(BitConverter.ToString(payloadBytes));

			payloadBcd = payloadBcd.Replace("-", "");

			return payloadBcd.ToString();
		}

		/// <summary>
		/// Converts a hexadecimal string to an array of bytes.
		/// </summary>
		/// <param name="payload">array of bytes encoded as hex</param>
		/// <exception cref="ArgumentNullException">The Hexadecimal string is null.</exception>
		/// <exception cref="ArgumentException">The Hexadecimal string is not at even number of characters.</exception>
		/// <exception cref="System.FormatException">The Hexadecimal string contains some invalid characters.</exception>
		/// <returns>Array of bytes parsed from Hexadecimal string.</returns>
		public static byte[] HexToByes(string payload)
		{
			if (payload == null)
			{
				throw new ArgumentNullException(nameof(payload));
			}
			if (payload.Length % 2 != 0)
			{
				throw new ArgumentException($"Payload invalid length must be an even number", nameof(payload));
			}

			Byte[] payloadBytes = new byte[payload.Length / 2];

			char[] chars = payload.ToCharArray();

			for (int index = 0; index < payloadBytes.Length; index++)
			{
				byte byteHigh = Convert.ToByte(chars[index * 2].ToString(), 16);
				byte byteLow = Convert.ToByte(chars[(index * 2) + 1].ToString(), 16);

				payloadBytes[index] += (byte)(byteHigh * 16);
				payloadBytes[index] += byteLow;
			}

			return payloadBytes;
		}

		/// <summary>
		/// Ensures unmanaged serial port and thread resources are released in a "responsible" manner.
		/// </summary>
		public void Dispose()
		{
			if (serialDevice != null)
			{
				serialDevice.Dispose();
				serialDevice = null;
			}
		}
	}
}
