module util

import encoding.binary

pub struct BinaryWriter {
mut:
	buffer []u8 [required]
	offset i32
}

// new_binary_writer initializes a BinaryWriter instance over a mutable slice.
[inline]
pub fn new_binary_writer(mut buffer []u8) BinaryWriter {
	return BinaryWriter{
		buffer: buffer
		offset: 0
	}
}

// get_offset gets the number of bytes written by a BinaryWriter.
[inline]
pub fn (writer BinaryWriter) get_offset() i32 {
	return writer.offset
}

// get_consumed gets a slice of the underlying array up to the offset of a BinaryWriter.
[inline]
pub fn (writer BinaryWriter) get_consumed() []u8 {
	return writer.buffer[..writer.offset]
}

// advance advances the offset of a BinaryWriter.
[inline]
pub fn (mut writer BinaryWriter) advance(amount i32) ! {
	writer.offset += amount

	if writer.offset >= writer.buffer.len {
		writer.offset -= amount
		return error('Cannot advance past the end of the writer')
	}
}

[direct_array_access; inline]
pub fn (mut writer BinaryWriter) write_byte(value u8) {
	_ = writer.buffer[writer.offset] // bounds check

	writer.buffer[writer.offset] = value
	writer.offset++
}

[inline]
pub fn (mut writer BinaryWriter) write_bool(value bool) {
	if value == true {
		writer.write_byte(1)
	} else {
		writer.write_byte(0)
	}
}

[inline]
pub fn (mut writer BinaryWriter) write_u16_be(value u16) {
	binary.big_endian_put_u16_at(mut writer.buffer, value, writer.offset)
	writer.offset += sizeof(u16)
}

[direct_array_access; inline]
pub fn (mut writer BinaryWriter) write_u24_be(value u32) {
	_ = writer.buffer[writer.offset] // bounds check
	_ = writer.buffer[writer.offset + 2] // bounds check

	writer.buffer[writer.offset] = u8(value >>> u32(16))
	writer.buffer[writer.offset + 1] = u8(value >>> u32(8))
	writer.buffer[writer.offset + 2] = u8(value)
	writer.offset += 3
}

[inline]
pub fn (mut writer BinaryWriter) write_u32_be(value u32) {
	binary.big_endian_put_u32_at(mut writer.buffer, value, writer.offset)
	writer.offset += sizeof(u32)
}

[inline]
pub fn (mut writer BinaryWriter) write_bytes(buffer []u8) ! {
	if writer.offset + buffer.len > writer.buffer.len {
		return error('buffer is too long')
	}

	unsafe {
		vmemmove(&u8(writer.buffer.data) + usize(writer.offset), buffer.data, buffer.len)
	}
	writer.offset += buffer.len
}

[inline]
pub fn (mut writer BinaryWriter) write_string_null_terminated(value string) ! {
	if writer.offset + value.len + 1 > writer.buffer.len {
		return error('string value is too long')
	}

	unsafe {
		vmemmove(&u8(writer.buffer.data) + usize(writer.offset), value.str, value.len)
	}
	writer.offset += value.len

	writer.write_byte(0)
}
