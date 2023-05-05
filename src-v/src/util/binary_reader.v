module util

import encoding.binary

pub struct BinaryReader {
	buffer []u8 [required]
mut:
	offset i32
}

// new_binary_reader initializes a BinaryReader instance over a mutable slice.
[inline]
pub fn new_binary_reader(mut buffer []u8) BinaryReader {
	return BinaryReader{
		buffer: buffer
		offset: 0
	}
}

// get_offset gets the number of bytes written by a BinaryReader.
[inline]
pub fn (reader BinaryReader) get_offset() i32 {
	return reader.offset
}

// get_consumed gets a slice of the underlying array up to the offset of a BinaryReader.
[inline]
pub fn (reader BinaryReader) get_consumed() []u8 {
	return reader.buffer[..reader.offset]
}

// advance advances the offset of a BinaryReader.
[inline]
pub fn (mut reader BinaryReader) advance(amount i32) ! {
	reader.offset += amount

	if reader.offset >= reader.buffer.len {
		reader.offset -= amount
		return error('Cannot advance past the end of the reader')
	}
}

[direct_array_access; inline]
pub fn (mut reader BinaryReader) read_byte() u8 {
	value := reader.buffer[reader.offset]
	reader.offset++
	return value
}

[inline]
pub fn (mut reader BinaryReader) read_bool() !bool {
	value := reader.read_byte()

	return match value {
		u8(0) { false }
		u8(1) { true }
		else { error('attempted to read a boolean value, but the data was neither 0 nor 1 (${value})') }
	}
}

[direct_array_access; inline]
pub fn (mut reader BinaryReader) read_u24_be() u32 {
	b := reader.buffer
	o := reader.offset

	value := u32(b[o + 2]) | (u32(b[o + 1]) << u32(8)) | (u32(b[o]) << u32(16))
	reader.offset += 3
	return value
}

[inline]
pub fn (mut reader BinaryReader) read_u32_be() u32 {
	value := binary.big_endian_u32_at(reader.buffer, reader.offset)
	reader.offset += sizeof(u32)
	return value
}

[inline]
pub fn (mut reader BinaryReader) read_string_null_terminated() !string {
	mut string_end := reader.buffer[reader.offset..].index(0)
	if string_end == -1 {
		return error('null-terminator not found')
	}

	string_end += reader.offset
	value := reader.buffer[reader.offset..string_end].bytestr()
	reader.offset += value.len + 1
	return value
}
