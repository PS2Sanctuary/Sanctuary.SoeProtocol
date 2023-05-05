module packets

import core
import util

const size_remap_connection = sizeof(core.SoeOpCode) + sizeof(u32) + sizeof(u32)

// RemapConnection represents a packet used to remap an existing session to a new port.
pub struct RemapConnection {
pub:
	// The ID of the session to remap.
	session_id u32
	// The CRC seed of the session to remap.
	crc_seed u32
}

pub fn deserialize_remap_connection(buffer []u8, has_op bool) RemapConnection {
	mut reader := util.new_binary_reader(buffer)

	if has_op {
		reader.advance(sizeof(core.SoeOpCode))!
	}

	session_id := reader.read_u32_be()
	crc_seed := reader.read_u32_be()

	return RemapConnection{
		session_id: session_id
		crc_seed: crc_seed
	}
}

// serialize writes this `RemapConnection` to a buffer, including the OP code.
pub fn (remap RemapConnection) serialize(mut buffer []u8) {
	mut writer := util.new_binary_writer(mut buffer)

	writer.write_u16_be(u16(core.SoeOpCode.remap_connection))
	writer.write_u32_be(remap.session_id)
	writer.write_u32_be(remap.crc_seed)
}
