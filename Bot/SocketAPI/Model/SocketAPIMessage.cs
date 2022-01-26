namespace SocketAPI 
{
	/// <summary>
	/// Represents a serializable response to return to the client.
	/// </summary>
	public class SocketAPIMessage
	{
		public SocketAPIMessage() {}

		public SocketAPIMessage(object? value, string? error)
		{
			this.value = value;
			this.error = error;
		}

		/// <summary>
		/// Describes whether the request completed successfully or not.
		/// </summary>
		public string status { 
			get 
			{ 
				if (this.error != null)
					return "error";
				else
					return "okay";
			}

			private set {}
		}

		/// <summary>
		/// The unique identifier of the associated request.
		/// </summary>
		public string? id { get; set; }

		/// <summary>
		/// Describes the type of response; i.e. event or response.
		/// Wrapper property used for encoding purposes.
		/// </summary>
		public string? _type 
		{ 
			get { return this.type?.ToString().ToLower(); }
			private set {}
		}

		/// <summary>
		/// Describes the type of response; i.e. event or response. 
		/// </summary>
		public SocketAPIMessageType? type;

		/// <summary>
		/// If an error occurred while processing the client's request, this property would contain the error message.
		/// </summary>
		public string? error { get; set; }

		/// <summary>
		/// The actual body of the response, if any.
		/// </summary>
		public object? value { get; set; }

		/// <returns>
		/// This object `System.Text.Json.JsonSerializer.Serialize`'d.
		/// </returns>
		public string? Serialize()
		{
			return System.Text.Json.JsonSerializer.Serialize(this);
		}

		/// <summary>
		/// Creates a `SocketAPIResponse` populated with the supplied value object.
		/// </summary>
		public static SocketAPIMessage FromValue(object? value)
		{
			return new(value, null);
		}

		/// <summary>
		/// Creates a `SocketAPIResponse` populated with the supplied error message.
		/// </summary>
		public static SocketAPIMessage FromError(string errorMessage)
		{
			return new(null, errorMessage);
		}

		public override string ToString()
		{
			return $"SocketAPI.SocketAPIMessage (id: {this.id}) - status: {this.status}, type: {this.type}, value: {this.value}, error: {this.error}";
		}
	}
}