module util

import core
import encoding.binary
import services

pub enum SoePacketValidationResult {
	// The packet is valid.
	valid = 0
	// The packet is too short, for its type.
	too_short = 1
	// The packet failed CRC validation.
	crc_mismatch = 2
	// The packet had an unknown OP code
	invalid_op_code = 3
}

[inline]
pub fn read_soe_op_code(buffer []u8) core.SoeOpCode {
	if _unlikely_(buffer.len < sizeof(core.SoeOpCode)) {
		return core.SoeOpCode.invalid
	}

	return unsafe { core.SoeOpCode(binary.big_endian_u16(buffer)) }
}

// is_contextless_packet gets a value indicating whether the given OP code
// represents a packet that is used outside of the context of a session.
[inline]
pub fn is_contextless_packet(op core.SoeOpCode) bool {
	return op in [.session_request, .session_response, .unknown_sender, .remap_connection]
}

// is_contextual_packet gets a value indicating whether the given OP code represents
// a packet that may only be used within the context of a session.
[inline]
pub fn is_contextual_packet(op core.SoeOpCode) bool {
	return op in [
		.multi_packet,
		.disconnect,
		.heartbeat,
		.net_status_request,
		.net_status_response,
		.reliable_data,
		.reliable_data_fragment,
		.out_of_order,
		.acknowledge,
	]
}

// append_crc writes a CRC check value to the given BinaryWriter. The entirety
// of the writer's buffer is used to calculate the check.
pub fn append_crc(mut writer BinaryWriter, crc_seed u32, crc_length u8) {
	if _unlikely_(crc_length == 0) {
		return
	}

	crc_value := services.crc32_hash(writer.get_consumed(), crc_seed)
	match crc_length {
		1 { writer.write_byte(u8(crc_value)) }
		2 { writer.write_u16_be(u16(crc_value)) }
		3 { writer.write_u24_be(crc_value) }
		4 { writer.write_u32_be(crc_value) }
		else { panic('Invalid CRC length! (${crc_length})') }
	}
}

// validate_soe_packet checks that the packet_data 'most likely' contains an SOE protocol packet.
pub fn validate_soe_packet(packet_data []u8, params core.SoeSessionParameters) (SoePacketValidationResult, core.SoeOpCode) {
	if _unlikely_(packet_data.len < sizeof(core.SoeOpCode)) {
		return SoePacketValidationResult.too_short, core.SoeOpCode.invalid
	}

	op := read_soe_op_code(packet_data)
	if _unlikely_(!is_contextless_packet(op)) && _unlikely_(!is_contextual_packet(op)) {
		return SoePacketValidationResult.invalid_op_code, op
	}

	minimum_length := get_packet_minimum_length(op, params.is_compression_enabled, params.crc_length)
	if _unlikely_(minimum_length > packet_data.len) {
		return SoePacketValidationResult.too_short, op
	}

	if is_contextless_packet(op) || params.crc_length == 0 {
		return SoePacketValidationResult.valid, op
	}

	actual_crc := services.crc32_hash(packet_data[..packet_data.len - params.crc_length],
		params.crc_seed)
	mut crc_match := false

	match params.crc_length {
		1 {
			crc := packet_data[packet_data.len - 1]
			crc_match = u8(actual_crc) == crc
		}
		2 {
			crc := binary.big_endian_u16_at(packet_data, packet_data.len - 2)
			crc_match = u16(actual_crc) == crc
		}
		3 {
			mut reader := BinaryReader{
				buffer: packet_data#[-3..]
			}
			crc := reader.read_u24_be()
			crc_match = (actual_crc & 0x00FFFFFF) == crc
		}
		4 {
			crc := binary.big_endian_u32_at(packet_data, packet_data.len - 4)
			crc_match = actual_crc == crc
		}
		else {
			panic('Invalid CRC length (${params.crc_length})')
		}
	}

	if crc_match {
		return SoePacketValidationResult.valid, op
	} else {
		return SoePacketValidationResult.crc_mismatch, op
	}
}

pub fn get_packet_minimum_length(op core.SoeOpCode, is_compression_enabled bool, crc_length u8) i32 {
	return 0
}

[inline]
fn get_contextual_packet_padding(is_compression_enabled bool, crc_length u8) i32 {
	return int(sizeof(core.SoeOpCode)) + crc_length + if is_compression_enabled {
		1
	} else {
		0
	}
}
