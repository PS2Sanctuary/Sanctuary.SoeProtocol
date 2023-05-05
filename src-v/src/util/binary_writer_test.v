module util

fn test_write_byte() {
	mut buffer := [u8(0)]
	mut writer := BinaryWriter{
		buffer: buffer
	}

	writer.write_byte(12)
	assert buffer[0] == 12
	assert writer.get_offset() == 1
}

fn test_write_bool() {
	mut buffer := []u8{len: 2, init: 255}
	mut writer := BinaryWriter{
		buffer: buffer
	}

	writer.write_bool(false)
	assert buffer[0] == 0

	writer.write_bool(true)
	assert buffer[1] == 1

	assert writer.get_offset() == 2
}

fn test_writer_u16_be() {
	mut buffer := []u8{len: 2}
	mut writer := BinaryWriter{
		buffer: buffer
	}

	writer.write_u16_be(300)
	assert buffer[0] == 0x01
	assert buffer[1] == 0x2C

	assert writer.get_offset() == sizeof(u16)
}

fn test_writer_u24_be() {
	mut buffer := []u8{len: 3}
	mut writer := BinaryWriter{
		buffer: buffer
	}

	writer.write_u24_be(70000)
	assert buffer[0] == 0x01
	assert buffer[1] == 0x11
	assert buffer[2] == 0x70

	assert writer.get_offset() == 3
}

fn test_writer_u32_be() {
	mut buffer := []u8{len: 4}
	mut writer := BinaryWriter{
		buffer: buffer
	}

	writer.write_u32_be(70000000)
	assert buffer[0] == 0x04
	assert buffer[1] == 0x2C
	assert buffer[2] == 0x1D
	assert buffer[3] == 0x80

	assert writer.get_offset() == sizeof(u32)
}

fn test_write_bytes_when_value_too_long() {
	data := [u8(1), 2, 3, 4]
	mut buffer := []u8{len: 3}
	mut writer := BinaryWriter{
		buffer: buffer
	}

	mut buffer_too_long := false
	writer.write_bytes(data) or { buffer_too_long = true }
	assert buffer_too_long
}

fn test_write_bytes() {
	data := [u8(1), 2, 3, 4]
	mut buffer := []u8{len: 5}
	mut writer := BinaryWriter{
		buffer: buffer
	}

	writer.advance(1)!
	writer.write_bytes(data)!
	assert buffer == [u8(0), 1, 2, 3, 4]
	assert writer.get_offset() == 5
}

fn test_write_string_when_value_too_long() {
	value := 'str'
	mut buffer := []u8{len: 3}
	mut writer := BinaryWriter{
		buffer: buffer
	}

	mut buffer_too_long := false
	writer.write_string_null_terminated(value) or { buffer_too_long = true }
	assert buffer_too_long
}

fn test_write_string() {
	value := 'str'
	mut buffer := []u8{len: 5}
	mut writer := BinaryWriter{
		buffer: buffer
	}

	writer.advance(1)!
	writer.write_string_null_terminated(value)!

	mut expected := []u8{len: 1, cap: 5, init: 0}
	expected << value.bytes()
	expected << 0

	assert buffer == expected
}
