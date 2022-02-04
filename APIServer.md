# Socket API Server

The server is but a rather simple `TCPListener`. It accepts clients indefinitely and keeps a strong reference in memory so that it may be able to broadcast a message to all connected clients (i.e. island crashed, arrival & departures).

Message exchange is based on JSON - client requests are expected to conform to `SocketAPIRequest`, server responses and events conform to `SocketAPIMessage`.

## Configuration

The server comes with its own configuration file: `server.json`. It contains the following configurable properties:

```javascript
{
  "Enabled": true, // Whether the server and all its features should be enabled or disabled, `false` by default
  "LogsEnabled": true, // Whether logs should be written to console, `true` by default
  "Port": 5201 // The port on which the server will be listening for TCP clients, set to 5201 by default
}
```

## `SocketAPIRequest`

Requests are of the form:

```javascript
{
  "id":123, // This will be echoed back by the response.
  "endpoint":"endpointName", // The name of the remote endpoint to execute.
  "args":"{\"myArg\":123}" // JSON-formatted arguments object, this will be passed as a string to the endpoint whose responsibility will be to also deserialize it to the expected input type.
}
```

## `SocketAPIMessage`

And responses of the form:

```javascript
{
  "id":123, // Same as in client's request.
  "value":"{}", // object or null.
  "error":"message", // If an error was thrown by the endpoint, this would contain the error message.
  "status":"okay or error", // Contains either "okay" or "error".
  "_type":"event or response" // Contains either "response" or "event".
}
```

It is also the server's responsibility to load and keep track of endpoints - this is done via Reflection, **once** and asynchronously (assuming the `SocketAPIServer.Start()` method is not awaited), at startup. The server looks for classes marked with the `SocketAPIController` within the `SysBot.ACNHOrders` assembly - this was done to further prune the number of methods to explore -, and then for methods marked with the `SocketAPIEndpoint` that:

1. Have a single parameter of type `string`,
2. Are static,
3. Have a return type of `object`.

An example is provided in [`Bot/SocketAPI/ExampleEndpoint.cs`](https://github.com/Fehniix/SysBot.ACNHOrders/blob/main/Bot/SocketAPI/EndpointExample.cs).

A client request string is pre-validated by `SocketAPIProtocol.DecodeMessage`. The string has to be JSON RFC 8259 conformant, it must be able to be deserialized to a `SocketAPIRequest` object and the `endpoint` attribute must not be `null`. The arguments included within the request are expected to be already JSON-formatted, i.e.:

```json
"args":"{\"myArg\":123}"
```

## Client implementations

- TypeScript, NodeJS: [Fehniix/sysbot-net-api](https://github.com/Fehniix/sysbot-net-api)
