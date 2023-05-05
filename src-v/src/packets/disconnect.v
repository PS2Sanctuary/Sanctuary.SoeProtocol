module packets

import encoding.binary

const size_disconnect = sizeof(u32) + sizeof(DisconnectReason)

// DisconnectReason enumerates the possible session termination codes.
pub enum DisconnectReason as u16 {
	// No reason can be given for the disconnect.
	unknown = 0
	// An ICMP error occured, forcing the disconnect.
	icmp_error = 1
	// The other party has let the session become inactive.
	timeout = 2
	// An internal use code, used to indicate that the other party has sent a disconnect.
	other_side_terminated = 3
	// Indicates that the session manager has been disposed of. Generally occurs upon shutdown.
	manager_deleted = 4
	// An internal use code, indicating a session request attempt has failed.
	connect_fail = 5
	// The proxied application is terminating the session.
	application = 6
	// An internal use code, indicating that the session must disconnect as the other party is unreachable.
	unreachable_connection = 7
	// Indicates that the session has been closed because a data sequence was not acknowledged quickly enough.
	unacknowledged_timeout = 8
	// Indicates that a session request has failed (often due to the connecting party attempting a
	// reconnection too quickly), and a new attempt should be made after a short delay.
	new_connection_attempt = 9
	// Indicates that the application did not accept a session request.
	connection_refused = 10
	// Indicates that the proper session negotiation flow has not been observed.
	connect_error = 11
	// Indicates that a session request has probably been looped back to the sender,
	// and it should not continue with the connection attempt.
	connecting_to_self = 12
	// Indicates that reliable data is being sent too fast to be processed.
	reliable_overflow = 13
	// Indicates that the session manager has been orphaned by the application.
	application_released = 14
	// Indicates that a corrupt packet was received.
	corrupt_packet = 15
	// Indicates that the requested SOE protocol version or application protocol is invalid.
	protocol_mismatch = 16
}

// Disconnect represents a packet used to terminate a session.
pub struct Disconnect {
pub:
	// The ID of the session that is being terminated.
	session_id u32 [required]
	// The reason for the termination.
	reason DisconnectReason [required]
}

pub fn deserialize_disconnect(buffer []u8) Disconnect {
	session_id := binary.big_endian_u32(buffer)
	reason := DisconnectReason(binary.big_endian_u32_at(buffer, sizeof(u32)))

	return Disconnect{
		session_id: session_id
		reason: reason
	}
}

pub fn (disconnect Disconnect) serialize(mut buffer []u8) {
	binary.big_endian_put_u32(mut buffer, disconnect.session_id)
	binary.big_endian_put_u16_at(mut buffer, u16(disconnect.reason), sizeof(u32))
}
