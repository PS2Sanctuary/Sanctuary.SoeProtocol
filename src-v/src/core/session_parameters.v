module core

import time

// SoeSessionParameters contains values used to control a session.
pub struct SoeSessionParameters {
pub:
	// The application protocol being proxied by this session.
	application_protocol string [required]
	// The maximum length of a UDP packet that this part can receive.
	udp_length u32 = default_udp_length
	// The maximum number of raw packets that may queued for either processing or sending.
	max_queued_raw_packets i32 = 512
	// The maximum number of data fragments that may be queued for either stitching or dispatch.
	max_queued_reliable_data_packets i16 = 256
	// The timespan after which to send a heartbeat, if no contextual packets have been received within the interval.
	// Set to Time{} to disable heart-beating.
	heartbeat_after time.Time = default_session_heartbeat_after
	// The default timespan after which to consider a session inactive, if no contextual packets have been received
	// within the interval. Set to Time{} to prevent a session from being terminated due to inactivity.
	inactivity_timeout time.Time = default_session_inactivity_timeout
pub mut:
	// The maximum length of a UDP packet that the remote party can receive.
	udp_length_remote u32
	// The seed used to calculate packet CRC hashes.
	crc_seed u32
	// The number of bytes used to store a packet CRC hash. Must be between 0 and 4, inclusive.
	crc_length u8 = default_crc_length
	// A value indicating whether compression is enabled for the session.
	is_compression_enabled bool
	// The data acknowledgement window
	data_ack_window i16 = 32
	// A value indicating whether all data packets should be acknowledged.
	acknowledge_all_data bool
}
