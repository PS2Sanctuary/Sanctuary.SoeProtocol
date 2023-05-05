module util

fn test_read_byte() {
	buffer := [u8(24), 19]
	mut reader := BinaryReader{
		buffer: buffer
	}

	assert reader.read_byte() == 24
	assert reader.read_byte() == 19
	assert reader.get_offset() == 2
}

fn test_read_bool() {
	buffer := [u8(0), 1, 2]
	mut reader := BinaryReader{
		buffer: buffer
	}

	assert reader.read_bool()! == false
	assert reader.read_bool()! == true

	mut threw := false
	reader.read_bool() or { threw = true }
	assert threw

	assert reader.get_offset() == 3
}

fn test_read_u24_be() {
	buffer := [u8(0x01), 0x11, 0x70]
	mut reader := BinaryReader{
		buffer: buffer
	}

	assert reader.read_u24_be() == 70000
	assert reader.get_offset() == 3
}

fn test_read_u32_be() {
	buffer := [u8(0x04), 0x2C, 0x1D, 0x80]
	mut reader := BinaryReader{
		buffer: buffer
	}

	assert reader.read_u32_be() == 70000000
	assert reader.get_offset() == sizeof(u32)
}

fn test_read_string_null_terminated() {
	buffer := [u8(0), u8(`s`), u8(`t`), u8(`r`), 0]
	mut reader := BinaryReader{
		buffer: buffer
	}

	reader.advance(1)!
	assert reader.read_string_null_terminated()! == 'str'
	assert reader.get_offset() == 5
}

fn test_read_string_null_terminated_when_no_terminator() {
	buffer := [u8(`s`), u8(`t`), u8(`r`)]
	mut reader := BinaryReader{
		buffer: buffer
	}

	mut threw := false
	reader.read_string_null_terminated() or { threw = true }
	assert threw
}
