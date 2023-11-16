use std::mem::size_of;

pub const MULTI_DATA_INDICATOR: [u8; 2] = [0x00, 0x19];

/// Calculates the true data sequence from an incoming packet sequence.
#[inline]
pub fn get_true_incoming_sequence(
    packet_sequence: u16,
    current_sequence: u64,
    max_queued_reliable_data_packets: i16
) -> u64 {
    // Note; this method makes the assumption that the amount of queued reliable data
    // can never be more than slightly less than the max value of a ushort
    
    // Zero-out the lower two bytes of our last known sequence and
    // and insert the packet sequence in that space
    let mut sequence: u64 = packet_sequence as u64 | (current_sequence & 0xFFFFFFFFFFFF0000);
    
    // If the sequence we obtain is smaller than our possible window, we must have wrapped
    // forward to the next 'packet sequence' block, and hence need to increment the true
    // sequence by an entire block
    if sequence < current_sequence - max_queued_reliable_data_packets as u64 {
        sequence += 0xFFFF + 1
    }
    // If the sequence we obtain is larger than our possible window, we must have wrapped back
    // to the last 'packet sequence' 'block' (ushort), and hence need to decrement the true
    // sequence by an entire block
    else if sequence > current_sequence + max_queued_reliable_data_packets as u64 {
        sequence -= 0xFFFF + 1
    }
    
    sequence
}

/// Checks if the given buffer starts with the `MULTI_DATA_INDICATOR`.
#[inline]
pub fn has_multi_data_indicator(buffer: &[u8]) -> bool {
    buffer.len() >= MULTI_DATA_INDICATOR.len()
        && buffer[0..2] == MULTI_DATA_INDICATOR
}

/// Writes the `MULTI_DATA_INDICATOR` to the given buffer, and increments the offset appropriately.
#[inline]
pub fn write_multi_data_indicator(buffer: &mut [u8], offset: &mut usize) {
    let end_offset = *offset + MULTI_DATA_INDICATOR.len();
    buffer[*offset..end_offset].copy_from_slice(&MULTI_DATA_INDICATOR);
    *offset += 2;
}

/// Reads a variable length value from a buffer.
pub fn read_variable_length(buffer: &[u8], offset: &mut usize) -> u32 {
    let mut value: u32 = 0;

    if buffer[*offset] < u8::MAX
    {
        value = buffer[*offset] as u32;
        *offset += 1;
    }
    else if buffer[*offset + 1] == u8::MAX && buffer[*offset + 2] == u8::MAX
    {
        value = u32::from_be_bytes(buffer[(*offset + 3)..].split_at(size_of::<u32>()).try_into().unwrap());
        value |= (buffer[*offset + 3] as u32) << 24;
        value |= (buffer[*offset + 4] as u32) << 16;
        value |= (buffer[*offset + 5] as u32) << 8;
        value |= buffer[*offset + 6] as u32;
        *offset += 7;
    }
    else
    {
        value |= (buffer[*offset + 1] as u32) << 8;
        value |= buffer[*offset + 2] as u32;
        *offset += 3;
    }

    value
}

/// Gets the amount of space in a buffer that a variable length value will consume.
pub fn get_variable_length_size(length: u32) -> usize {
    if length < 0xFF {
        size_of::<u8>()
    }
    else if length < 0xFFFF {
        size_of::<u16>() + 1
    }
    else {
        size_of::<u32>() + 3
    }
}

pub fn write_variable_length(buffer: &mut [u8], length: u32, offset: &mut usize) {
    
}
