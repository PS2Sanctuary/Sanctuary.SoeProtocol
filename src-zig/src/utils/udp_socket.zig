const builtin = @import("builtin");
const posix = std.posix;
const std = @import("std");

/// Parses an IP address.
pub fn parseAddress(ip_address: []const u8, port: u16) !std.net.Address {
    return try std.net.Address.resolveIp(ip_address, port);
}

/// Parses an IP address with a port suffix.
pub fn parseAddressWithPort(value: []const u8) !std.net.Address {
    const colon_index = std.mem.indexOfScalar(
        u8,
        value,
        ':',
    ) orelse return error.InvalidFormat;

    const port = try std.fmt.parseInt(u16, value[colon_index + 1 ..], 10);
    return parseAddress(value[0..colon_index], port);
}

/// Resolves a hostname to the first associated IP address.
pub fn resolveHostToAddress(allocator: std.mem.ALlocator, hostname: []const u8, port: u16) !std.net.Address {
    const address_list = try std.net.getAddressList(
        allocator,
        hostname,
        port,
    );
    defer address_list.deinit();

    return address_list.addrs[0];
}

pub const UdpSocket = struct {
    _socket: posix.socket_t,

    pub fn init(socket_buffer_len: i32) !UdpSocket {
        const socket = try posix.socket(
            posix.AF.INET,
            posix.SOCK.DGRAM,
            posix.IPPROTO.UDP,
        );

        // Set the length of the send and receive buffer
        // https://huge-man-linux.net/man2extras/man2freebsd/getsockopt.html
        const len_bytes: []const u8 = std.mem.asBytes(&socket_buffer_len);
        posix.setsockopt(socket, posix.SOL.SOCKET, posix.SO.SNDBUF, len_bytes);
        posix.setsockopt(socket, posix.SOL.SOCKET, posix.SO.RCVBUF, len_bytes);

        // Set non-blocking mode
        // https://stackoverflow.com/questions/1150635/unix-nonblocking-i-o-o-nonblock-vs-fionbio
        // https://learn.microsoft.com/en-us/windows/win32/api/winsock/nf-winsock-ioctlsocket
        // TODO: This may not be needed, seems like posix.socket() initialise in non-blocking mode by default
        // if (builtin.os.tag == .windows) {
        //     std.os.windows.ws2_32.ioctlsocket(UdpSocket, std.os.windows.ws2_32.FIONBIO, 1);
        // } else {
        //     const flags: i32 = posix.fcntl(socket, posix.F.GETFL, 0);
        //     posix.fcntl(socket, posix.F.SETFL, flags | posix.O.NONBLOCK);
        // }

        return UdpSocket{
            ._socket = socket,
        };
    }

    pub fn deinit(self: *UdpSocket) void {
        posix.close(self._socket);
    }

    /// If in server mode, bind to a specific `port`.
    /// If in client mode, bind to `port` 0.
    pub fn bind(self: *UdpSocket, endpoint: std.net.Address) posix.BindError!void {
        try posix.bind(
            self._socket,
            endpoint.any,
            endpoint.getOsSockLen(),
        );
    }

    pub fn sendTo(self: *UdpSocket, endpoint: std.net.Address, buffer: []const u8) posix.SendError!usize {
        return posix.sendto(
            self._socket,
            buffer,
            0,
            endpoint.any,
            endpoint.getOsSockLen(),
        );
    }

    // pub fn receiveFrom(self: *UdpSocket) void {
    //     // TODO
    // }
};
