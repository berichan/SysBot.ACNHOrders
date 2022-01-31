using System.Text.Json;

namespace SocketAPI
{
	public sealed class SocketAPIProtocol
	{
		/// <summary>
		/// Given an inbound JSON-formatted string message, this method returns a `SocketAPIRequest` instance.
		/// Returns `null` if the input message is invalid JSON or if `endpoint` is missing.
		/// </summary>
		public static SocketAPIRequest? DecodeMessage(string message)
		{
			try 
			{
				SocketAPIRequest? request = JsonSerializer.Deserialize<SocketAPIRequest>(message);
				
				if (request == null) 
					return null;

				if (request!.endpoint == null)
					return null;

				return request;
			} 
			catch(System.Exception ex)
			{
				Logger.LogError($"Could not deserialize inbound request ({message}). Error: {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// Given the message type and input string, this method returns an encoded message ready to be sent to a client.
		/// The JSON-encoded message is terminated by "\0\0".
		/// Do not send messages of length > 2^16 bytes (or your OS's default TCP buffer size)! The messages would get TCP-fragmented.
		/// </summary>
		public static string? EncodeMessage(SocketAPIMessage message)
		{
			try
			{
				return JsonSerializer.Serialize(message) + "\0\0";
			}
			catch (System.Exception ex)
			{
				Logger.LogError($"Could not serialize outbound message ({message}). Error: {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// Given the input message, this method retrieves the message type as per protocol specification (1.)
		/// </summary>
		private static SocketAPIMessageType GetMessageTypeFromMessage(string type)
		{
			return (SocketAPIMessageType)System.Enum.Parse(typeof(SocketAPIMessageType), type, true);
		}
	}
}