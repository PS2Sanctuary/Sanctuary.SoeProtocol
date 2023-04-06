module util

fn test_get_true_incoming_sequence() {
	mut sequence := get_true_incoming_sequence(1, 0, 8)
	assert sequence == 1

	sequence = get_true_incoming_sequence(1, 0xFFFF, 8)
	assert sequence == 0x10001

	sequence = get_true_incoming_sequence(0xFFFC, 0xFFFFFFFF, 8)
	assert sequence == 0xFFFFFFFC
}

fn test_has_multi_data_indicator() {
	too_short := [ u8(0x00) ]
	invalid := [ u8(0x19), 0x00 ]
	valid := [ u8(0x00), 0x19, 0x03 ]

	assert !has_multi_data_indicator(too_short)
	assert !has_multi_data_indicator(invalid)
	assert has_multi_data_indicator(valid)
}

fn test_write_multi_data_indicator() {
	mut buffer := []u8 { len: 1 + multi_data_indicator.len }
	buffer[0] = 0xFF

	amount_written := write_multi_data_indicator(mut buffer[1..])

	assert amount_written == multi_data_indicator.len
	assert buffer[1..] == multi_data_indicator
	assert buffer[0] == 0xFF
}

fn test_read_variable_length_integer() {
	mut value := u32(0)

	byte_length := [ u8(0xFE) ]
	assert read_variable_length_integer(byte_length, mut &value) == byte_length.len
	assert value == 0xFE

	short_length := [ u8(0xFF), 0xFF, 0x01 ]
	assert read_variable_length_integer(short_length, mut &value) == short_length.len
	assert value == 0xFF01

	int_length := [ u8(0xFF), 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00 ]
	assert read_variable_length_integer(int_length, mut &value) == int_length.len
	assert value == 0xFF000000
}

fn test_get_variable_length_size() {
	assert get_variable_length_size(0xFE) == 1
	assert get_variable_length_size(0xFF) == 3
	assert get_variable_length_size(0xFFFF) == 7
}

fn test_write_variable_length_integer() {
	mut buffer := []u8 { len: 7 }

	assert write_variable_length_integer(mut buffer, 0xFE) == 1
	assert buffer[0] == 0xFE

	assert write_variable_length_integer(mut buffer, 0xFF01) == 3
	assert buffer[..3] == [ u8(0xFF), 0xFF, 0x01 ]

	assert write_variable_length_integer(mut buffer, 0xFF000000) == 7
	assert buffer == [ u8(0xFF), 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00 ]
}
