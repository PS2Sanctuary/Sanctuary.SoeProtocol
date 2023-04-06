module main

import time

fn main() {
	st := time.new_stopwatch()

	time.sleep(time.microsecond * 1000)

	println("Slept for ${st.elapsed().microseconds()}")
}
