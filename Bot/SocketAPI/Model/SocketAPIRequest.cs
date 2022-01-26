using System;

namespace SocketAPI
{
	[Serializable]
	public sealed class SocketAPIRequest
	{
		/// <summary>
		/// The unique identifier for the request.
		/// </summary>
		public string? id { get; set; }

		/// <summary>
		/// Represents the name of the endpoint to remotely execute and from which to fetch the result.
		/// </summary>
		public string? endpoint { get; set; }

		/// <summary>
		/// The JSON-formatted arguments string to pass to the endpoint.
		/// </summary>
		public string? args { get; set; }

		public SocketAPIRequest() {}

		public override string ToString()
		{
			return $"SocketAPI.SocketAPIRequest (id: {this.id}) - endpoint: {this.endpoint}, args: {this.args}";
		}
	}
}