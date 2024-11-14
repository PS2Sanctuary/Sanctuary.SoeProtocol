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
pub fn resolveHostToAddress(allocator: std.mem.Allocator, hostname: []const u8, port: u16) !std.net.Address {
    const address_list = try std.net.getAddressList(
        allocator,
        hostname,
        port,
    );
    defer address_list.deinit();

    return address_list.addrs[0];
}

pub const ReceiveFrom = struct {
    received_len: usize,
    sender: std.net.Address,
};

pub const UdpSocket = struct {
    _socket: posix.socket_t,

    pub fn init(socket_buffer_len: i32) !UdpSocket {
        const socket = try posix.socket(
            posix.AF.INET,
            posix.SOCK.DGRAM | posix.SOCK.NONBLOCK,
            posix.IPPROTO.UDP,
        );

        // Set the length of the send and receive buffer
        // https://huge-man-linux.net/man2extras/man2freebsd/getsockopt.html
        const len_bytes: []const u8 = std.mem.asBytes(&socket_buffer_len);
        try posix.setsockopt(socket, posix.SOL.SOCKET, posix.SO.SNDBUF, len_bytes);
        try posix.setsockopt(socket, posix.SOL.SOCKET, posix.SO.RCVBUF, len_bytes);

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
            &endpoint.any,
            endpoint.getOsSockLen(),
        );
    }

    pub fn sendTo(self: *const UdpSocket, endpoint: std.net.Address, buffer: []const u8) posix.SendToError!usize {
        return posix.sendto(
            self._socket,
            buffer,
            0,
            &endpoint.any,
            endpoint.getOsSockLen(),
        );
    }

    pub fn receiveFrom(self: *UdpSocket, buffer: []u8) posix.RecvFromError!ReceiveFrom {
        var other_addr: posix.sockaddr = undefined;
        var other_addrlen: posix.socklen_t = @sizeOf(posix.sockaddr);

        const rec_len = try posix.recvfrom(
            self._socket,
            buffer,
            0,
            &other_addr,
            &other_addrlen,
        );
        const sender = std.net.Address.initPosix(@alignCast(&other_addr));

        return ReceiveFrom{
            .received_len = rec_len,
            .sender = sender,
        };
    }
};

test parseAddressWithPort {
    const address = try parseAddressWithPort("127.0.0.1:5000");
    // 0x1388 = port 5000
    try std.testing.expectEqualSlices(
        u8,
        &[14]u8{ 0x13, 0x88, 127, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0 },
        &address.any.data,
    );
}

test resolveHostToAddress {
    const address = try resolveHostToAddress(
        std.testing.allocator,
        "localhost",
        5000,
    );

    // Some systems resolve to IPv6 by default
    if (address.any.family == posix.AF.INET6) {
        // 0x1388 = port 5000
        try std.testing.expectEqualSlices(
            u8,
            &[14]u8{ 0x13, 0x88, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
            &address.any.data,
        );
    } else {
        // 0x1388 = port 5000
        try std.testing.expectEqualSlices(
            u8,
            &[14]u8{ 0x13, 0x88, 127, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0 },
            &address.any.data,
        );
    }
}

test "testUdpRoundTrip" {
    var receive_socket = try UdpSocket.init(512);
    const send_socket = try UdpSocket.init(512);
    const receive_endpoint = try parseAddress("127.0.0.1", 46897);

    const expected = [_]u8{ 1, 2, 3, 4, 5 };
    const actual: []u8 = try std.testing.allocator.alloc(u8, 512);
    defer std.testing.allocator.free(actual);

    try std.testing.expectError(
        posix.RecvFromError.WouldBlock,
        receive_socket.receiveFrom(actual),
    );

    try receive_socket.bind(receive_endpoint);
    _ = try send_socket.sendTo(receive_endpoint, &expected);
    const recv_data = try receive_socket.receiveFrom(actual);

    try std.testing.expectEqualSlices(u8, &expected, actual[0..recv_data.received_len]);
}
