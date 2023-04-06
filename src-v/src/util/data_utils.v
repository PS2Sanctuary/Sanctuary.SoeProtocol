module util

import encoding.binary

pub const multi_data_indicator = [ u8(0x00), 0x19 ]

// get_true_incoming_sequence calculates the true data sequence from an incoming packet sequence.
pub fn get_true_incoming_sequence(
	packet_sequence u16,
	current_sequence i64,
	max_queued_reliable_data_packets i16
) i64 {
	// Note; this method makes the assumption that the amount of queued reliable data
	// can never be more than slightly less than the max value of a ushort

	// Zero-out the lower two bytes of our last known sequence and
	// and insert the packet sequence in that space
	mut sequence := packet_sequence | (current_sequence & 0xFFFFFFFFFFFF0000)

	// If the sequence we obtain is smaller than our possible window, we must have wrapped
	// forward to the next 'packet sequence' block, and hence need to increment the true
	// sequence by an entire block
	if _unlikely_(sequence < current_sequence - max_queued_reliable_data_packets) {
		sequence += 0xFFFF + 1
	}
	// If the sequence we obtain is larger than our possible window, we must have wrapped back
	// to the last 'packet sequence' 'block' (ushort), and hence need to decrement the true
	// sequence by an entire block
	else if _unlikely_(sequence > current_sequence + max_queued_reliable_data_packets) {
		sequence -= 0xFFFF + 1
	}

	return sequence
}

// has_multi_data_indicator checks if the given buffer starts with the multi_data_indicator
[direct_array_access; inline]
pub fn has_multi_data_indicator(buffer []u8) bool {
	return buffer.len >= multi_data_indicator.len
		&& buffer[0..2] == multi_data_indicator
}

// write_multi_data_indicator writes the multi_data_indicator to the given buffer,
// and increments the offset appropriately
[inline]
pub fn write_multi_data_indicator(mut buffer []u8) i32 {
	amount := copy(mut buffer, multi_data_indicator)

	if amount < multi_data_indicator.len {
		panic("buffer was too short to write the multi-data indicator")
	}

	return amount
}

pub fn read_variable_length_integer(buffer []u8, mut value &u32) i32 {
	unsafe {
		if buffer[0] < 0xFF {
			*value = buffer[0]
			return sizeof(u8)
		}
		else if buffer[1] == 0xFF && buffer[2] == 0xFF {
			*value = binary.big_endian_u32_at(buffer, 3)
			return 3 + sizeof(u32)
		}
		else {
			*value = binary.big_endian_u16_at(buffer, 1)
			return 1 + sizeof(u16)
		}
	}
}

pub fn get_variable_length_size(value i32) i32 {
	if value < 0xFF {
		return sizeof(u8)
	}
	else if value < 0xFFFF {
		return sizeof(u16) + 1
	}
	else {
		return sizeof(u32) + 3
	}
}

pub fn write_variable_length_integer(mut buffer []u8, value u32) i32 {
	if value < 0xFF {
		buffer[0] = u8(value)
		return 1
	}
	else if value < 0xFFFF {
		buffer[0] = 0xFF
		binary.big_endian_put_u16_at(mut buffer, u16(value), 1)
		return sizeof(u16) + 1
	}
	else {
		buffer[0] = 0xFF
		buffer[1] = 0xFF
		buffer[2] = 0xFF
		binary.big_endian_put_u32_at(mut buffer, value, 3)
		return sizeof(u32) + 3
	}
}
