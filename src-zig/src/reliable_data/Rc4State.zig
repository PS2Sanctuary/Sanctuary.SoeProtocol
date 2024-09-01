const std = @import("std");

/// Stores an RC4 key state and provides a means to transform data using the RC4 algorithm.
const Rc4State = @This();

/// The number of bytes used to store the key state.
const RC4_STATE_LENGTH = 256;

/// Private field: The first swap index.
_index_1: usize = 0,
/// Private field: The second swap index.
_index_2: usize = 0,
/// Private field: Stores the data used to mutate the input stream.
_data: [RC4_STATE_LENGTH]u8,

/// Initializes a new instance of the Rc4State using the provided key.
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

/// Copies the existing state.
pub fn copy(self: *const Rc4State) Rc4State {
    var data: [RC4_STATE_LENGTH]u8 = undefined;
    @memcpy(&data, &self._data);

    return Rc4State{
        ._data = data,
    };
}

/// Transforms (encrypts or decrypts) an input buffer into the output.
pub fn transform(self: *Rc4State, input: []const u8, output: []u8) void {
    for (0..input.len) |i| {
        self.increment();
        const xor_index: usize = (@as(usize, self._data[self._index_1]) + self._data[self._index_2]) % RC4_STATE_LENGTH;
        output[i] = input[i] ^ self._data[xor_index];
    }
}

/// 'Schedules' the key into the state data buffer.
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

/// Increments the state; progressing the stream.
fn increment(self: *Rc4State) void {
    self._index_1 = (self._index_1 + 1) % RC4_STATE_LENGTH;
    self._index_2 = (self._index_2 + self._data[self._index_1]) % RC4_STATE_LENGTH;

    const swap = self._data[self._index_1];
    self._data[self._index_1] = self._data[self._index_2];
    self._data[self._index_2] = swap;
}

test "Test RC4 Encryption Vectors" {
    const TestVector = struct {
        key: []const u8,
        plain_text: []const u8,
        cipher_text: []const u8,
    };

    const vectors = [3]TestVector{
        TestVector{
            .key = "Key",
            .plain_text = "Plaintext",
            .cipher_text = &[_]u8{ 0xBB, 0xF3, 0x16, 0xE8, 0xD9, 0x40, 0xAF, 0x0A, 0xD3 },
        },
        TestVector{
            .key = "Wiki",
            .plain_text = "pedia",
            .cipher_text = &[_]u8{ 0x10, 0x21, 0xBF, 0x04, 0x20 },
        },
        TestVector{
            .key = "Secret",
            .plain_text = "Attack at dawn",
            .cipher_text = &[_]u8{ 0x45, 0xA0, 0x1F, 0x64, 0x5F, 0xC3, 0x5B, 0x38, 0x35, 0x52, 0x54, 0x4B, 0x9B, 0xF5 },
        },
    };

    for (vectors) |vector| {
        var state = Rc4State.init(vector.key);

        const output = try std.testing.allocator.alloc(u8, vector.cipher_text.len);
        defer std.testing.allocator.free(output);

        state.transform(vector.plain_text, output);
        try std.testing.expectEqualSlices(u8, vector.cipher_text, output);
    }
}
