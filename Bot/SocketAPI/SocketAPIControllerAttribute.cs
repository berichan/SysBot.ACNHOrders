using System;

[AttributeUsage(AttributeTargets.Class)]
/// <summary>
/// Marks the class or struct as a `SocketAPIController`, or otherwise simply a suitable container of `SocketAPIEndpoint`s.  
/// </summary>
public class SocketAPIController: Attribute {}