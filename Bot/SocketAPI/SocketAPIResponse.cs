namespace SysBot.ACNHOrders 
{
	/// <summary>
	/// Represents a serializable response to return to the client.
	/// </summary>
	public class SocketAPIResponse
	{
		public SocketAPIResponse() {}

		public SocketAPIResponse(object? value, string? error)
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
	}
}