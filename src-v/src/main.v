module main

import time
import core
import util

fn main() {
	buffer := [u8(0), u8(core.SoeOpCode.acknowledge), 0, 1, 2, 3]
	params := core.SoeSessionParameters{
		application_protocol: 'test'
	}

	mut total_time := time.Duration(0)
	mut st := time.new_stopwatch()

	for _ in 0 .. 100 {
		st.restart()
		util.validate_soe_packet(buffer, params)
		total_time += st.elapsed()
	}

	print('Took ${time.Duration(total_time / 100).nanoseconds()}ns avg')
}
