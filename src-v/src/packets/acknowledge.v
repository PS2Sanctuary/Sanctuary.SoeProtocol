module packets

import encoding.binary

const size_acknowledge = sizeof(u16)

// Acknowledge represents a packet used to acknowledge reliable data.
pub struct Acknowledge {
pub:
	sequence u16 [required]
}

pub fn deserialize_acknowledge(buffer []u8) Acknowledge {
	sequence := binary.big_endian_u16(buffer)
	return Acknowledge{
		sequence: sequence
	}
}

pub fn (ack Acknowledge) serialize(mut buffer []u8) {
	binary.big_endian_put_u16(mut buffer, ack.sequence)
}
