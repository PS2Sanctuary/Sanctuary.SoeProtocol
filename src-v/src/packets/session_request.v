module packets

import core
import util

// OP + soe_protocol_version + session_id + udp_length + application_protocol null terminator + 1 char
const size_min_session_request = sizeof(core.SoeOpCode) + sizeof(u32) + sizeof(u32) + sizeof(u32) +
	2

// SessionRequest represents a packet used to negotiate the start of a session.
pub struct SessionRequest {
pub:
	soe_protocol_version u32
	session_id           u32
	udp_length           u32
	application_protocol string
}

pub fn deserialize_session_request(buffer []u8, has_op bool) !SessionRequest {
	mut reader := util.new_binary_reader(buffer)

	if has_op {
		reader.advance(sizeof(core.SoeOpCode))!
	}

	soe_protocol_version := reader.read_u32_be()
	session_id := reader.read_u32_be()
	udp_length := reader.read_u32_be()
	application_protocol := reader.read_string_null_terminated()!

	return SessionRequest{
		soe_protocol_version: soe_protocol_version
		session_id: session_id
		udp_length: udp_length
		application_protocol: application_protocol
	}
}

pub fn (request SessionRequest) get_size() int {
	// subtract 1 here because size_min_ factors in at least one character of the string
	return packets.size_min_session_request - request.application_protocol.len - 1
}

// serialize writes this `SessionRequest` to a buffer, including the OP code.
pub fn (request SessionRequest) serialize(mut buffer []u8) ! {
	mut writer := util.new_binary_writer(mut buffer)

	writer.write_u16_be(u16(core.SoeOpCode.session_request))
	writer.write_u32_be(request.soe_protocol_version)
	writer.write_u32_be(request.session_id)
	writer.write_u32_be(request.udp_length)
	writer.write_string_null_terminated(request.application_protocol)!
}
