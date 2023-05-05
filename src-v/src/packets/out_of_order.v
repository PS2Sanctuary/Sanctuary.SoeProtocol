module packets

import encoding.binary

const size_out_of_order = sizeof(u16)

// OutOfOrder represents a packet used to indicate that an out-of-order reliable data sequence has been received.
pub struct OutOfOrder {
pub:
	// The mis-ordered sequence number.
	sequence u16
}

pub fn deserialize_out_of_order(buffer []u8) OutOfOrder {
	sequence := binary.big_endian_u16(buffer)
	return OutOfOrder{
		sequence: sequence
	}
}

pub fn (ooo OutOfOrder) serialize(mut buffer []u8) {
	binary.big_endian_put_u16(mut buffer, ooo.sequence)
}
