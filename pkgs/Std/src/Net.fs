namespace EffSharp.Std

open EffSharp.Core
open EffSharp.Gen
open System.Net
open System.Net.Sockets
open System.IO

[<Struct>]
type Addr =
  | Tcp of tcp: IPEndPoint
  | Unix of unix: UnixDomainSocketEndPoint

module Addr =
  let tcp (ip: IPAddress) (port: int) = Tcp(IPEndPoint(ip, port))
  let loopback port = tcp IPAddress.Loopback port
  let any port = tcp IPAddress.Any port
  let unix (Path p) = Unix(UnixDomainSocketEndPoint(p))

  let toEndpoint =
    function
    | Tcp ep -> ep :> EndPoint
    | Unix ep -> ep :> EndPoint

  /// AddressFamily + ProtocolType + EndPoint for socket
  /// creation. Tcp -> (InterNetwork|V6, Tcp, ep),
  /// Unix -> (Unix, Unspecified, ep).
  let internal socketArgs =
    function
    | Tcp ep -> ep.AddressFamily, ProtocolType.Tcp, (ep :> EndPoint)
    | Unix ep -> AddressFamily.Unix, ProtocolType.Unspecified, (ep :> EndPoint)

/// Bound + listening socket (TCP or Unix).
[<Struct>]
type Listener internal (socket: Socket) =
  member internal _.Socket = socket

/// Bound UDP datagram socket.
[<Struct>]
type UdpSocket internal (socket: Socket) =
  member internal _.Socket = socket

type Stream internal (socket: Socket) =
  member internal _.Socket = socket
  member _.AsIOStream() = new NetworkStream(socket) :> System.IO.Stream

[<Struct>]
type NetErr =
  | ConnectionRefused of connrefused: SocketException
  | ConnectionReset of connreset: SocketException
  | AddrInUse of addrinuse: SocketException
  | AddrNotAvailable of addrnotavail: SocketException
  | TimedOut of timedout: SocketException
  | HostUnreachable of hostunreach: SocketException
  | NetIOError of netioerr: exn

module NetErr =
  let ofExn (e: exn) : NetErr =
    match e with
    | :? SocketException as se ->
      match se.SocketErrorCode with
      | SocketError.ConnectionRefused -> ConnectionRefused se
      | SocketError.ConnectionReset -> ConnectionReset se
      | SocketError.AddressAlreadyInUse -> AddrInUse se
      | SocketError.AddressNotAvailable -> AddrNotAvailable se
      | SocketError.TimedOut -> TimedOut se
      | SocketError.HostUnreachable -> HostUnreachable se
      | _ -> NetIOError se
    | _ -> NetIOError e

[<Effect(Mode.Wrap)>]
type Net =
  abstract listen: backlog: int -> Addr -> Eff<Listener, NetErr, unit>
  abstract accept: Listener -> Eff<Stream * Addr, NetErr, unit>
  abstract connect: Addr -> Eff<Stream, NetErr, unit>
  abstract close: Listener -> Eff<unit, NetErr, unit>

  abstract read: bytes: int -> Stream -> Eff<byte array, NetErr, unit>
  abstract write: Stream -> byte array -> Eff<unit, NetErr, unit>
  abstract shutdown: Stream -> Eff<unit, NetErr, unit>
  abstract close: Stream -> Eff<unit, NetErr, unit>

  abstract bind: IPEndPoint -> Eff<UdpSocket, NetErr, unit>

  abstract send:
    UdpSocket -> IPEndPoint -> byte array -> Eff<unit, NetErr, unit>

  abstract recv: UdpSocket -> int -> Eff<byte array * IPEndPoint, NetErr, unit>
  abstract close: UdpSocket -> Eff<unit, NetErr, unit>

  abstract resolve: string -> Eff<IPAddress array, NetErr, unit>


type NetProvider internal () =
  interface Net with

    member _.listen (backlog: int) (addr: Addr) =
      let af, proto, ep = Addr.socketArgs addr

      Eff.tryCatch (fun () ->
        let socket = new Socket(af, SocketType.Stream, proto)

        try
          if af <> AddressFamily.Unix then
            socket.SetSocketOption(
              SocketOptionLevel.Socket,
              SocketOptionName.ReuseAddress,
              true
            )

          socket.Bind(ep)
          socket.Listen(backlog)
          Listener(socket)
        with ex ->
          socket.Dispose()
          raise ex
      )
      |> Eff.mapErr NetErr.ofExn

    member _.accept(listener: Listener) =
      fun () -> task {
        let! socket = listener.Socket.AcceptAsync()

        let remote =
          if listener.Socket.AddressFamily = AddressFamily.Unix then
            match socket.RemoteEndPoint with
            | :? UnixDomainSocketEndPoint as ep -> Unix(ep)
            | _ -> Unix(UnixDomainSocketEndPoint(""))
          else
            match socket.RemoteEndPoint with
            | :? IPEndPoint as ep -> Tcp(ep)
            | _ -> Tcp(IPEndPoint(IPAddress.Any, 0))

        return Stream(socket), remote
      }
      |> Eff.tryTask
      |> Eff.mapErr NetErr.ofExn

    member _.connect(addr: Addr) =
      let af, proto, ep = Addr.socketArgs addr

      fun () -> task {
        let socket = new Socket(af, SocketType.Stream, proto)

        try
          do! socket.ConnectAsync(ep)
          return Stream(socket)
        with ex ->
          socket.Dispose()
          return raise ex
      }
      |> Eff.tryTask
      |> Eff.mapErr NetErr.ofExn

    member _.close(listener: Listener) : Eff<unit, NetErr, unit> =
      Eff.tryCatch (fun () -> listener.Socket.Dispose())
      |> Eff.mapErr NetErr.ofExn

    member _.read (maxBytes: int) (stream: Stream) =
      fun () -> task {
        let buffer = Array.zeroCreate maxBytes
        let mem = System.Memory<byte>(buffer, 0, maxBytes)

        let! n = stream.Socket.ReceiveAsync(mem, SocketFlags.None)

        return Array.sub buffer 0 n
      }
      |> Eff.tryTask
      |> Eff.mapErr NetErr.ofExn

    member _.write (stream: Stream) (data: byte array) =
      fun () -> task {
        let mutable offset = 0

        while offset < data.Length do
          let mem =
            System.ReadOnlyMemory<byte>(data, offset, data.Length - offset)

          let! sent = stream.Socket.SendAsync(mem, SocketFlags.None)

          offset <- offset + sent
      }
      |> Eff.tryTask
      |> Eff.mapErr NetErr.ofExn

    member _.shutdown(stream: Stream) =
      Eff.tryCatch (fun () -> stream.Socket.Shutdown(SocketShutdown.Both))
      |> Eff.mapErr NetErr.ofExn

    member _.close(stream: Stream) : Eff<unit, NetErr, unit> =
      Eff.tryCatch (fun () -> stream.Socket.Dispose())
      |> Eff.mapErr NetErr.ofExn

    member _.bind(ep: IPEndPoint) =
      Eff.tryCatch (fun () ->
        let socket =
          new Socket(ep.AddressFamily, SocketType.Dgram, ProtocolType.Udp)

        try
          socket.Bind(ep)
          UdpSocket(socket)
        with ex ->
          socket.Dispose()
          raise ex
      )
      |> Eff.mapErr NetErr.ofExn

    member _.send (socket: UdpSocket) (ep: IPEndPoint) (data: byte array) =
      fun () -> task {
        let mem = System.ReadOnlyMemory<byte>(data)

        let! _ = socket.Socket.SendToAsync(mem, SocketFlags.None, ep)

        return ()
      }
      |> Eff.tryTask
      |> Eff.mapErr NetErr.ofExn

    member _.recv (socket: UdpSocket) (maxBytes: int) =
      fun () -> task {
        let buffer = Array.zeroCreate maxBytes

        let remote: EndPoint =
          if socket.Socket.AddressFamily = AddressFamily.InterNetworkV6 then
            IPEndPoint(IPAddress.IPv6Any, 0)
          else
            IPEndPoint(IPAddress.Any, 0)

        let mem = System.Memory<byte>(buffer, 0, maxBytes)

        let! result =
          socket.Socket.ReceiveFromAsync(mem, SocketFlags.None, remote)

        let received = Array.sub buffer 0 result.ReceivedBytes

        let sender =
          match result.RemoteEndPoint with
          | :? IPEndPoint as ep -> ep
          | _ -> IPEndPoint(IPAddress.Any, 0)

        return received, sender
      }
      |> Eff.tryTask
      |> Eff.mapErr NetErr.ofExn

    member _.close(socket: UdpSocket) : Eff<unit, NetErr, unit> =
      Eff.tryCatch (fun () -> socket.Socket.Dispose())
      |> Eff.mapErr NetErr.ofExn

    member _.resolve(hostname: string) =
      fun () -> task { return! Dns.GetHostAddressesAsync(hostname) }
      |> Eff.tryTask
      |> Eff.mapErr NetErr.ofExn

[<AutoOpen>]
module NetExt =
  type Net with
    static member Provider() = NetProvider()
