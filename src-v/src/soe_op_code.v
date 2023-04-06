module main

// Enumerates the packet OP codes used in the SOE protocol.
enum SoeOpCode as u16 {
	// Represents an invalid packet. Note that this is not part of the protocol specification.
	invalid = 0

	// Used to request the start of a session.
	session_request = 0x01

	// Used to confirm the start of a session, and set connection details.
	session_response = 0x02

	// Use to encapsulate two or more SOE protocol packets.
	multi_packet = 0x03

	// Used to indicate that a party is closing the session.
	disconnect = 0x05

	// Used to keep a session alive, when no data has been receiving by either party
	// for some time.
	heartbeat = 0x06

	// It is not entirely clear how this packet type is utilised.
	net_status_request = 0x07

	// It is not entirely clear how this packet type is utilised.
	net_status_response = 0x08

	// Used to transfer small buffers of application data.
	reliable_data = 0x09

	// Used to transfer large buffers of application data in multiple fragments.
	reliable_data_fragment = 0x0D

	// Used to indicate that a data sequence was received out-of-order.
	out_of_order = 0x11

	// Used to acknowledge that a data sequence has been received.
	acknowledge = 0x15

	// Used to indicate that the receiving party does not have a session
	// associated with the sender's address.
	unknown_sender = 0x1D

	// Used to request that a session be remapped to another port.
	remap_connection = 0x1E
}
