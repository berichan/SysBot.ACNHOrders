using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace SysBot.ACNHOrders {
	/// <summary>
	/// Acts as an API server, accepting requests and replying over TCP/IP.
	/// </summary>
	public sealed class SocketAPIServer
	{
		/// <summary>
		/// Useful for TcpListener's graceful shutdown.
		/// </summary>
		private static CancellationTokenSource tcpListenerCancellationSource = new();

		/// <summary>
		/// Provides an alias to the cancellation token.
		/// </summary>
		private static CancellationToken tcpListenerCancellationToken 
		{ 
			get { return tcpListenerCancellationSource.Token; }
			set { }
		}

		/// <summary>
		/// The TCP listener used to listen for incoming connections.
		/// </summary>
		private static TcpListener? listener;

		/// <summary>
		/// Keeps a list of callable endpoints.
		/// </summary>
		private static Dictionary<string, Delegate> apiEndpoints = new();

		/// <summary>
		/// Keeps the list of connected clients to broadcast events to.
		/// </summary>
		private static ConcurrentBag<TcpClient> clients = new();

		public SocketAPIServer() {}

		/// <summary>
		/// Starts listening for incoming connections on the configured port.
		/// </summary>
		public static async Task Start(SocketAPIServerConfig config)
		{
			if (!config.Enabled)
				return;

			if (!config.LogsEnabled)
				Logger.disableLogs();

			int eps = RegisterEndpoints();
			Logger.LogInfo($"n. of registered endpoints: {eps}");

			string? res = (string?)InvokeEndpoint("dummyEP", "myArg1");
			Console.WriteLine(res);

			string hostname = Dns.GetHostName();
			if (!config.AllowRemoteClients)
				hostname = "localhost";

			IPAddress ip = Dns.GetHostEntry(hostname).AddressList[0];
			listener = new(ip, config.Port);
			listener.Start();

			Logger.LogInfo($"Socket API server listening on port {config.Port}.");

			tcpListenerCancellationToken.ThrowIfCancellationRequested();
			tcpListenerCancellationToken.Register(listener.Stop);

			while(!tcpListenerCancellationToken.IsCancellationRequested)
			{
				try
				{
					TcpClient client = await listener.AcceptTcpClientAsync();
					IPEndPoint? clientEP = client.Client.RemoteEndPoint as IPEndPoint;
					Logger.LogInfo($"A client connected! IP: {clientEP?.Address}, on port: {clientEP?.Port}");

					NetworkStream stream = client.GetStream();

					while(client.Connected)
					{
						byte[] buffer = new byte[client.Available];
						await stream.ReadAsync(buffer, 0, client.Available, tcpListenerCancellationToken);
						string message = System.Text.Encoding.UTF8.GetString(buffer).Replace("\n", "").Replace("\r", "");
						
						if (message == "quit")
						{
							client.Close();
						}
					}
				}
				catch(OperationCanceledException) when (tcpListenerCancellationToken.IsCancellationRequested)
				{
					Logger.LogInfo("The socket API server was closed.", true);
				}
				catch(Exception ex)
				{
					Logger.LogError($"An error occured on the soket API server: {ex.Message}", true);
				}
			}
		}

		/// <summary>
		/// Stops the execution of the server.
		/// </summary>
		public static void Stop()
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
		private static bool RegisterEndpoint(string name, Func<string, object?> handler)
		{
			if (apiEndpoints.ContainsKey(name))
				return false;

			apiEndpoints.Add(name, handler);

			return true;
		}

		/// <summary>
		/// Loads all the classes marked as `SocketAPIController` and respective `SocketAPIEndpoint`-marked static methods.
		/// </summary>
		/// <remarks>
		/// The SocketAPIEndpoint marked methods 
		/// </remarks>
		/// <returns>The number of static methods successfully registered.</returns>
		private static int RegisterEndpoints()
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
				RegisterEndpoint(endpoint.Name, endpoint.CreateDelegate<Func<string, object?>>());

			return endpoints.Count();
		}

		/// <summary>
		/// Invokes the registered endpoint via endpoint name, providing it with JSON-encoded arguments.
		/// </summary>
		/// <param name="endpointName">The name of the registered endpoint. Case-sensitive!</param>
		/// <param name="jsonArgs">The arguments to provide to the endpoint, encoded in JSON format.</param>
		/// <returns>A JSON-formatted response. `null` if the endpoint was not found.</returns>
		private static string? InvokeEndpoint(string endpointName, string jsonArgs)
		{
			if (!apiEndpoints.ContainsKey(endpointName))
				return RespondWithError("The supplied endpoint was not found.").Serialize();

			try
			{
				object? rawResponse = (object?)apiEndpoints[endpointName].Method.Invoke(null, new[] { jsonArgs });
				return RespondWithValue(rawResponse).Serialize();
			}
			catch(Exception ex)
			{
				return RespondWithError(ex.InnerException?.Message ?? "A generic exception was thrown.").Serialize();
			}
		}

		/// <summary>
		/// Creates a `SocketAPIResponse` populated with the supplied value object.
		/// </summary>
		private static SocketAPIResponse RespondWithValue(object? value)
		{
			return new(value, null);
		}

		/// <summary>
		/// Creates a `SocketAPIResponse` populated with the supplied error message.
		/// </summary>
		private static SocketAPIResponse RespondWithError(string errorMessage)
		{
			return new(null, errorMessage);
		}
	}
}