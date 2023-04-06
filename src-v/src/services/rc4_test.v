module services

struct TestVector {
	key string
	plain_text string
	cipher_text []u8
}

const test_vectors = [
	TestVector {
		key: "Key",
		plain_text: "Plaintext",
		cipher_text: [  u8(0xBB), 0xF3, 0x16, 0xE8, 0xD9, 0x40, 0xAF, 0x0A, 0xD3 ]
	},
	TestVector {
		key: "Wiki",
		plain_text: "pedia",
		cipher_text: [  u8(0x10), 0x21, 0xBF, 0x04, 0x20 ]
	},
	TestVector {
		key: "Secret",
		plain_text: "Attack at dawn",
		cipher_text: [ u8(0x45), 0xA0, 0x1F, 0x64, 0x5F, 0xC3, 0x5B, 0x38, 0x35, 0x52, 0x54, 0x4B, 0x9B, 0xF5 ]
	},
]

fn test_transform()
{
	for test_vector in test_vectors {
		mut cipher := get_rc4_cipher(test_vector)
		plaintext_bytes := test_vector.plain_text.bytes()
		mut cipher_bytes := []u8 { len: plaintext_bytes.len }

		cipher.transform(plaintext_bytes, mut cipher_bytes)
		assert test_vector.cipher_text == cipher_bytes
	}
}

fn test_round_trip() {
	for test_vector in test_vectors {
		mut encrypt_cipher := get_rc4_cipher(test_vector)
		mut decrypt_cipher := get_rc4_cipher(test_vector)

		plaintext_bytes := test_vector.plain_text.bytes()
		mut encrypted_bytes := []u8 { len: plaintext_bytes.len }
		mut decrypted_bytes := []u8 { len: plaintext_bytes.len }

		encrypt_cipher.transform(plaintext_bytes, mut encrypted_bytes)
		decrypt_cipher.transform(encrypted_bytes, mut decrypted_bytes)

		assert plaintext_bytes == decrypted_bytes
	}
}

// test_stream_transform ensures that the state is correctly advanced after a transform is completed.
fn test_stream_transform() {
	for test_vector in test_vectors {
		half := test_vector.cipher_text.len / 2
		mut decrypted := []u8 { len: test_vector.cipher_text.len }
		mut cipher := get_rc4_cipher(test_vector)

		cipher.transform(test_vector.cipher_text[0..half], mut decrypted)
		cipher.transform(test_vector.cipher_text[half..], mut decrypted[half..])

		assert test_vector.plain_text == decrypted.bytestr()
	}
}

fn test_advance() {
	mut test_values_1 := [ u8(1), 2, 3 ]
	mut test_values_2 := [ u8(1), 2, 3 ]

	mut cipher_1 := get_rc4_cipher(test_vectors[0])
	mut cipher_2 := get_rc4_cipher(test_vectors[0])

	cipher_1.transform(test_values_1, mut test_values_1)

	cipher_2.advance(2)
	cipher_2.transform(test_values_2[2..], mut test_values_2[2..])

	assert test_values_1[2] == test_values_2[2]
}

fn get_rc4_cipher(test_vector TestVector) &Rc4Cipher {
	mut key_state := &Rc4Cipher{}
	rc4_schedule_key(test_vector.key.bytes(), mut key_state.state)

	return key_state
}
