using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;

namespace SocketAPI {
	/// <summary>
	/// Acts as an API server, accepting requests and replying over TCP/IP.
	/// </summary>
	public sealed class SocketAPIServer
	{
		/// <summary>
		/// Useful for TcpListener's graceful shutdown.
		/// </summary>
		private CancellationTokenSource tcpListenerCancellationSource = new();

		/// <summary>
		/// Provides an alias to the cancellation token.
		/// </summary>
		private CancellationToken tcpListenerCancellationToken 
		{ 
			get { return tcpListenerCancellationSource.Token; }
			set { }
		}

		/// <summary>
		/// The TCP listener used to listen for incoming connections.
		/// </summary>
		private TcpListener? listener;

		/// <summary>
		/// Keeps a list of callable endpoints.
		/// </summary>
		private Dictionary<string, Delegate> apiEndpoints = new();

		/// <summary>
		/// Keeps the list of connected clients to broadcast events to.
		/// </summary>
		private ConcurrentBag<TcpClient> clients = new();

		private SocketAPIServer() {}


		private static SocketAPIServer? _shared;

		/// <summary>
		///	The singleton instance of the `SocketAPIServer`.
		/// </summary>
		public static SocketAPIServer shared
		{
			get 
			{  
				if (_shared == null)
					_shared = new();
				return _shared;
			}
			private set { }
		}

		/// <summary>
		/// Starts listening for incoming connections on the configured port.
		/// </summary>
		public async Task Start(SocketAPIServerConfig config)
		{
			if (!config.Enabled)
				return;

			if (!config.LogsEnabled)
				Logger.disableLogs();

			int eps = RegisterEndpoints();
			Logger.LogInfo($"n. of registered endpoints: {eps}");

			listener = new(IPAddress.Any, config.Port);

			try 
			{
				listener.Start();
			}
			catch(SocketException ex)
			{
				Logger.LogError($"Socket API server failed to start: {ex.Message}");
				return;
			}

			Logger.LogInfo($"Socket API server listening on port {config.Port}.");

			tcpListenerCancellationToken.ThrowIfCancellationRequested();
			tcpListenerCancellationToken.Register(listener.Stop);

			while(!tcpListenerCancellationToken.IsCancellationRequested)
			{
				try
				{
					TcpClient client = await listener.AcceptTcpClientAsync();
					clients.Add(client);

					IPEndPoint? clientEP = client.Client.RemoteEndPoint as IPEndPoint;
					Logger.LogInfo($"A client connected! IP: {clientEP?.Address}, on port: {clientEP?.Port}");

					HandleTcpClient(client);
				}
				catch(OperationCanceledException) when (tcpListenerCancellationToken.IsCancellationRequested)
				{
					Logger.LogInfo("The socket API server was closed.", true);
					while(!clients.IsEmpty)
						clients.TryTake(out _);
				}
				catch(Exception ex)
				{
					Logger.LogError($"An error occured on the soket API server: {ex.Message}", true);
				}
			}
		}

		/// <summary>
		/// Given a connected TcpClient, this callback handles communication & graceful shutdown.
		/// </summary>
		private async void HandleTcpClient(TcpClient client)
		{
			NetworkStream stream = client.GetStream();

			while (true)
			{
				byte[] buffer = new byte[client.ReceiveBufferSize];
				int bytesRead = await stream.ReadAsync(buffer, 0, client.ReceiveBufferSize, tcpListenerCancellationToken);

				if (bytesRead == 0)
				{
					Logger.LogInfo("A remote client closed the connection.");
					break;
				}

				string rawMessage = Encoding.UTF8.GetString(buffer);
				rawMessage = Regex.Replace(rawMessage, @"\r\n?|\n|\0", "");
				
				SocketAPIRequest? request = SocketAPIProtocol.DecodeMessage(rawMessage);

				if (request == null)
				{
					this.SendResponse(client, SocketAPIMessage.FromError("There was an error while JSON-parsing the provided request."));
					continue;
				}

				SocketAPIMessage? message = this.InvokeEndpoint(request!.endpoint!, request?.args);

				if (message == null)
					message = SocketAPIMessage.FromError("The supplied endpoint was not found.");

				message.id = request!.id;

				this.SendResponse(client, message);
			}
		}

		/// <summary>
		/// Sends to the supplied client the given message of type `Response`.
		/// </summary>
		public void SendResponse(TcpClient client, SocketAPIMessage message)
		{
			message.type = SocketAPIMessageType.Response;
			this.SendMessage(client, message);
		}

		/// <summary>
		/// Sends to the supplied client the given message of type `Event`.
		/// </summary>
		public void SendEvent(TcpClient client, SocketAPIMessage message)
		{
			message.type = SocketAPIMessageType.Event;
			this.SendMessage(client, message);
		}

		/// <summary>
		/// Given a message, this method sends it to all currently connected clients in parallel encoded as an event.
		/// </summary>
		public async void BroadcastEvent(SocketAPIMessage message)
		{
			foreach(TcpClient client in clients)
			{
				if (client.Connected)
					await Task.Run(() => SendEvent(client, message));
			}
		}

		/// <summary>
		/// Encodes a message and sends it to a client.
		/// </summary>
		private async void SendMessage(TcpClient toClient, SocketAPIMessage message)
		{
			byte[] wBuff = Encoding.UTF8.GetBytes(SocketAPIProtocol.EncodeMessage(message)!);
			try
			{
				await toClient.GetStream().WriteAsync(wBuff, 0, wBuff.Length, tcpListenerCancellationToken);
			}
			catch(Exception ex)
			{
				Logger.LogError($"There was an error while sending a message to a client: {ex.Message}");
				toClient.Close();
			}
		}

		/// <summary>
		/// Stops the execution of the server.
		/// </summary>
		public void Stop()
		{
			listener?.Server.Close();
			tcpListenerCancellationSource.Cancel();
		}

		/// <summary>
		/// Registers an API endpoint by its name.
		/// </summary>
		/// <param name="name">The name of the endpoint used to invoke the provided handler.</param>
		/// <param name="handler">The handler responsible for generating a response.</param>
		/// <returns></returns>
		private bool RegisterEndpoint(string name, Func<string, object?> handler)
		{
			if (apiEndpoints.ContainsKey(name))
				return false;

			apiEndpoints.Add(name, handler);

			return true;
		}

		/// <summary>
		/// Loads all the classes marked as `SocketAPIController` and respective `SocketAPIEndpoint`-marked methods.
		/// </summary>
		/// <remarks>
		/// The SocketAPIEndpoint marked methods 
		/// </remarks>
		/// <returns>The number of methods successfully registered.</returns>
		private int RegisterEndpoints()
		{
			var endpoints = AppDomain.CurrentDomain.GetAssemblies()
								.Where(a => a.FullName?.Contains("SysBot.ACNHOrders") ?? false)
								.SelectMany(a => a.GetTypes())
								.Where(t => t.IsClass && t.GetCustomAttributes(typeof(SocketAPIController), true).Count() > 0)
								.SelectMany(c => c.GetMethods())
								.Where(m => m.GetCustomAttributes(typeof(SocketAPIEndpoint), true).Count() > 0)
								.Where(m => m.GetParameters().Count() == 1 &&
											m.IsStatic &&
											m.GetParameters()[0].ParameterType == typeof(string) &&
											m.ReturnType == typeof(object));

			foreach (var endpoint in endpoints)
				RegisterEndpoint(endpoint.Name, (Func<string, object?>)endpoint.CreateDelegate(typeof(Func<string, object?>)));

			return endpoints.Count();
		}

		/// <summary>
		/// Invokes the registered endpoint via endpoint name, providing it with JSON-encoded arguments.
		/// </summary>
		/// <param name="endpointName">The name of the registered endpoint. Case-sensitive!</param>
		/// <param name="jsonArgs">The arguments to provide to the endpoint, encoded in JSON format.</param>
		/// <returns>A JSON-formatted response. `null` if the endpoint was not found.</returns>
		private SocketAPIMessage? InvokeEndpoint(string endpointName, string? jsonArgs)
		{
			if (!apiEndpoints.ContainsKey(endpointName))
				return SocketAPIMessage.FromError("The supplied endpoint was not found.");

			try
			{
				object? rawResponse = (object?)apiEndpoints[endpointName].Method.Invoke(null, new[] { jsonArgs });
				return SocketAPIMessage.FromValue(rawResponse);
			}
			catch(Exception ex)
			{
				return SocketAPIMessage.FromError(ex.InnerException?.Message ?? "A generic exception was thrown.");
			}
		}
	}
}