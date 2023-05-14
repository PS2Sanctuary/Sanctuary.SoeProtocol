#[derive(Clone)]
pub struct Rc4 {
    index_1: u8,
    index_2: u8,
    state: [u8; 256]
}

impl Rc4 {
    pub fn new(key: &[u8]) -> Rc4 {
        const STATE_LENGTH: usize = 256;

        assert!(key.len() >= 1 && key.len() <= STATE_LENGTH);
        let mut rc4 = Rc4 {
            index_1: 0,
            index_2: 0,
            state: [0; STATE_LENGTH]
        };

        for i in 0..STATE_LENGTH {
            rc4.state[i] = i as u8;
        }

        let mut swap_index_1: usize = 0;
        let mut swap_index_2: usize = 0;

        for i in 0..STATE_LENGTH {
            swap_index_2 = (swap_index_2 + rc4.state[i] as usize + key[swap_index_1] as usize) % STATE_LENGTH;
            rc4.state.swap(i, swap_index_2);
            swap_index_1 = (swap_index_1 + 1) % key.len();
        }

        return rc4;
    }

    pub fn next(&mut self) -> u8 {
        self.index_1 = self.index_1.wrapping_add(1);
        self.index_2 = self.index_2.wrapping_add(self.s_1());
        self.state.swap(self.index_1.into(), self.index_2.into());

        let index: usize = self.s_1().wrapping_add(self.s_2()).into();
        return self.state[index];
    }

    pub fn transform(&mut self, buffer: &mut [u8]) {
        for i in buffer {
            *i = *i ^ self.next();
        }
    }

    fn s_1(&mut self) -> u8 {
        self.state[self.index_1 as usize]
    }

    fn s_2(&mut self) -> u8 {
        self.state[self.index_2 as usize]
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_vector_1() {
        let mut state = Rc4::new(b"Key");
        let mut buffer_string = String::from("Plaintext");
        let buffer: &mut [u8] = unsafe { get_mutable_string_bytes(&mut buffer_string) };
        let cipher: [u8; 9] =  [ 0xBB, 0xF3, 0x16, 0xE8, 0xD9, 0x40, 0xAF, 0x0A, 0xD3 ];

        state.transform(&mut buffer[..]);

        assert!(buffer.iter().zip(cipher.iter()).all(|(a,b)| a == b))
    }

    #[test]
    fn test_vector_2() {
        let mut state = Rc4::new(b"Wiki");
        let mut buffer_string = String::from("pedia");
        let buffer: &mut [u8] = unsafe { get_mutable_string_bytes(&mut buffer_string) };
        let cipher: [u8; 5] =  [ 0x10, 0x21, 0xBF, 0x04, 0x20 ];

        state.transform(&mut buffer[..]);

        assert!(buffer.iter().zip(cipher.iter()).all(|(a,b)| a == b))
    }

    #[test]
    fn test_vector_3() {
        let mut state = Rc4::new(b"Secret");
        let mut buffer_string = String::from("Attack at dawn");
        let buffer: &mut [u8] = unsafe { get_mutable_string_bytes(&mut buffer_string) };
        let cipher: [u8; 14] =  [ 0x45, 0xA0, 0x1F, 0x64, 0x5F, 0xC3, 0x5B, 0x38, 0x35, 0x52, 0x54, 0x4B, 0x9B, 0xF5 ];

        state.transform(&mut buffer[..]);

        assert!(buffer.iter().zip(cipher.iter()).all(|(a,b)| a == b))
    }

    unsafe fn get_mutable_string_bytes(value: &mut String) -> &mut [u8] {
        value.as_bytes_mut()
    }
}
