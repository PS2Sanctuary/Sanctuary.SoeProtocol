module services

const key_state_len = 256

[heap]
pub struct Rc4Cipher {
	mut:
		state []u8 = []u8{ len: key_state_len }
		index_1 i32
		index_2 i32
}

[direct_array_access]
pub fn (mut key_state Rc4Cipher) transform(input_buffer []u8, mut output_buffer []u8) {
	if output_buffer.len < input_buffer.len {
		panic("The output buffer must be at least as long as the input buffer.")
	}

	my_key_state := key_state.state

	for i in 0..input_buffer.len {
		key_state.increment()

		xor_index := (my_key_state[key_state.index_1] + my_key_state[key_state.index_2]) % key_state_len
		output_buffer[i] = byte(input_buffer[i] ^ my_key_state[xor_index])
	}
}

pub fn (mut key_state Rc4Cipher) advance(amount int) {
	for _ in 0..amount {
		key_state.increment()
	}
}

pub fn rc4_schedule_cipher(key_data_buffer []u8, mut cipher Rc4Cipher) {
	rc4_schedule_key(key_data_buffer, mut cipher.state)
}

[direct_array_access]
pub fn rc4_schedule_key(key_data_buffer []u8, mut key_state []u8) {
	if key_data_buffer.len < 1 || key_data_buffer.len > key_state_len {
		panic("Key length must be greater than zero and less than ${key_state_len}")
	}

	if key_state.len < key_state_len {
		panic("The key state buffer must have a capacity of at least ${key_state_len} bytes long")
	}

	for i in 0..key_state_len {
		key_state[i] = byte(i)
	}

	mut swap_index_1 := byte(0)
	mut swap_index_2 := byte(0)

	for i in 0..key_state_len {
		swap_index_2 = byte((swap_index_2 + key_state[i] + key_data_buffer[swap_index_1]) % key_state_len)
		key_state[i], key_state[swap_index_2] = key_state[swap_index_2], key_state[i]

		swap_index_1 = (swap_index_1 + 1) % key_data_buffer.len
	}
}

[direct_array_access; inline]
fn (mut key_state Rc4Cipher) increment() {
	unsafe {
		mut my_key_state := key_state.state

		key_state.index_1 = (key_state.index_1 + 1) % key_state_len
		key_state.index_2 = (key_state.index_2 + my_key_state[key_state.index_1]) % key_state_len
		my_key_state[key_state.index_1], my_key_state[key_state.index_2] = my_key_state[key_state.index_2], my_key_state[key_state.index_1]
	}
}
