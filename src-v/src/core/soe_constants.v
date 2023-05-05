module core

import time

const (
	soe_protocol_version            = u32(3)
	default_crc_length              = u8(2)
	default_udp_length              = u32(512)
	default_session_heartbeat_after = time.Time{
		second: 25
	}
	default_session_inactivity_timeout = time.Time{
		second: 30
	}
)
