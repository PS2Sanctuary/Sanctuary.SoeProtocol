pub const Rc4State = struct {
    const RC4_STATE_LENGTH = 256;

    _index_1: usize = 0,
    _index_2: usize = 0,
    _data: [RC4_STATE_LENGTH]u8,

    pub fn init(key: []const u8) Rc4State {
        var state = Rc4State{
            // Initialize the data array at comptime
            ._data = init: {
                // Using undefined here still fills out the array memory space as this is a value type
                var initial_value: [RC4_STATE_LENGTH]u8 = undefined;
                for (&initial_value, 0..) |*pt, i| {
                    pt.* = @intCast(i);
                }
                break :init initial_value;
            },
        };

        state.schedule(key);
        return state;
    }

    pub fn transform(self: *Rc4State, input: []const u8, output: []u8) void {
        for (0..input.len) |i| {
            self.increment();
            const xor_index: usize = @as(usize, self._data[self._index_1] + self._data[self._index_2]) % RC4_STATE_LENGTH;
            output[i] = input[i] ^ self._data[xor_index];
        }
    }

    fn schedule(self: *Rc4State, key: []const u8) void {
        // We perform this when initializing the data array at comptime in the init() function
        // for (0..RC4_STATE_LENGTH) |i| {
        //     self.data[i] = i;
        // }

        var swap_index_1: usize = 0;
        var swap_index_2: usize = 0;
        var swap: u8 = 0;

        for (0..RC4_STATE_LENGTH) |i| {
            swap_index_2 = (swap_index_2 + self._data[i] + key[swap_index_1]) % RC4_STATE_LENGTH;

            swap = self._data[i];
            self._data[i] = self._data[swap_index_2];
            self._data[swap_index_2] = swap;

            swap_index_1 = (swap_index_1 + 1) % key.len;
        }
    }

    fn increment(self: *Rc4State) void {
        self._index_1 = (self._index_1 + 1) % RC4_STATE_LENGTH;
        self._index_2 = (self._index_2 + self._data[self._index_1]) % RC4_STATE_LENGTH;

        const swap = self._data[self._index_1];
        self._data[self._index_1] = self._data[self._index_2];
        self._data[self._index_2] = swap;
    }
};
