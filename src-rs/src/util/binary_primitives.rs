pub mod read {
    pub fn read_u16_be(buffer: &[u8]) -> u16 {
        let mut value: u16 = (buffer[0] as u16) << 8;
        value |= buffer[1] as u16;
        
        value
    }

    pub fn read_u32_be(buffer: &[u8]) -> u32 {
        let mut value: u32 = (buffer[0] as u32) << 24;
        value |= (buffer[1] as u32) << 16;
        value |= (buffer[2] as u32) << 8;
        value |= buffer[3] as u32;
        
        value
    }
}

pub mod write {
    pub fn write_u16_be(buffer: &mut [u8], value: u16) {
        buffer[0] = (value >> 8) as u8
    }
}
