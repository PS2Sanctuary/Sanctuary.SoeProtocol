using Microsoft.Extensions.Logging;
using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using Sanctuary.SoeProtocol.Util;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ZLogger;
using ZLogger.Providers;

namespace Sanctuary.SoeProtocol.Managers;

public class SessionManager
{
    private readonly ILogger<SessionManager> _logger;
    private readonly Socket _socket;
    private readonly Dictionary<SocketAddress, SoeConnection> _connections = [];

    internal ILoggerFactory LogFactory { get; }

    public SessionParameters SessionParams { get; }

    public SessionManager(SessionParameters sessionParams)
    {
        LogFactory = LoggerFactory.Create(options =>
        {
            options.SetMinimumLevel(LogLevel.Trace)
                .AddZLoggerConsole()
                .AddZLoggerRollingFile(rollOptions =>
                {
                    rollOptions.RollingInterval = RollingInterval.Day;
                    rollOptions.FilePathSelector = (timestamp, sequenceNumber) => Path.Combine
                    (
                        "_SoeProtocolLogs",
                        $"{timestamp:yyyy-MM-dd}_{sequenceNumber:000}.log"
                    );
                });
        });
        _logger = LogFactory.CreateLogger<SessionManager>();

        SessionParams = sessionParams;
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.IP);

        int bufferSize = (int)sessionParams.UdpLength * 64;
        _socket.SendBufferSize = bufferSize;
        _socket.ReceiveBufferSize = bufferSize;
    }

    /// <summary>
    /// Initializes this <see cref="SessionManager"/>.
    /// </summary>
    /// <param name="localEndPoint">The local endpoint to bind to. Leave null to bind to a random port.</param>
    public void Initialize(EndPoint? localEndPoint)
    {
        localEndPoint ??= new IPEndPoint(IPAddress.Any, 0);
        _socket.Bind(localEndPoint);
    }

    public SoeConnection ConnectToRemote(EndPoint remote)
    {
        SocketAddress address = remote.Serialize();
        if (_connections.TryGetValue(address, out SoeConnection? connection))
        {
            _logger.ZLogWarning
            (
                $"(ConnectToRemote) Terminating and replacing ACTIVE connection to the remote {remote}"
            );
            connection.TerminateSession(DisconnectReason.Application, true);
        }

        connection = new SoeConnection(LogFactory.CreateLogger<SoeConnection>(), this, address);
        _connections[address] = connection;

        return connection;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        byte[] unknownSender = new byte[sizeof(SoeOpCode)];
        BinaryPrimitives.WriteUInt16BigEndian(unknownSender, (ushort)SoeOpCode.UnknownSender);

        SocketAddress receiveAddress = new(AddressFamily.InterNetworkV6);
        byte[] buffer = GC.AllocateArray<byte>((int)SessionParams.UdpLength, true);

        while (!ct.IsCancellationRequested)
        {
            int receivedLen = await _socket.ReceiveFromAsync(buffer, SocketFlags.None, receiveAddress, ct);
            if (receivedLen < sizeof(SoeOpCode))
            {
                _logger.ZLogDebug($"SOE Packet from {receiveAddress} was too short ({receivedLen} bytes)");
                continue;
            }

            // Validate that this is a valid SOE packet
            SoePacketValidationResult validationResult
                = SoePacketUtils.ValidatePacket(buffer, SessionParams, out SoeOpCode opCode);
            if (validationResult is not SoePacketValidationResult.Valid)
            {
                _logger.ZLogWarning($"SOE packet failed validation: {validationResult}");
                return;
            }

            Memory<byte> packet = buffer.AsMemory(sizeof(SoeOpCode), receivedLen - sizeof(SoeOpCode));
            _logger.ZLogTrace($"Received SOE packet of type {opCode} and length {receivedLen} from {receiveAddress}");

            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (opCode)
            {
                case SoeOpCode.SessionRequest:
                {
                    HandleSessionRequest(receiveAddress, packet.Span);
                    break;
                }
                case SoeOpCode.RemapConnection:
                {
                    // TODO: Handle this here
                    break;
                }
                default:
                {
                    if (_connections.TryGetValue(receiveAddress, out SoeConnection? conn))
                        conn.HandleSoePacket(opCode, packet.Span);
                    else
                        _socket.Send(unknownSender);

                    break;
                }
            }
        }
    }

    private void HandleSessionRequest(SocketAddress sender, ReadOnlySpan<byte> packet)
    {
        if (_connections.TryGetValue(sender, out SoeConnection? conn))
        {
            _logger.ZLogDebug
            (
                $"(HandleSessionRequest) Terminating and replacing ACTIVE connection to {sender} " +
                $"as new SessionRequest has been received"
            );
            conn.TerminateSession(DisconnectReason.OtherSideTerminated, false);
        }

        conn = new SoeConnection(LogFactory.CreateLogger<SoeConnection>(), this, sender);
        conn.HandleSoePacket(SoeOpCode.SessionRequest, packet);

        _connections[sender] = conn;
    }
}
