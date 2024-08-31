const BinaryPrimitives = @import("../utils/BinaryPrimitives.zig");

pub const Acknowledge = struct {
    pub const SIZE = @sizeOf(u16);

    sequence: u16,

    pub fn deserialize(buffer: []const u8) Acknowledge {
        return Acknowledge{
            .sequence = BinaryPrimitives.readU16BE(buffer),
        };
    }

    pub fn serialize(self: @This(), buffer: []u8) void {
        BinaryPrimitives.writeU16BE(buffer, self.sequence);
    }
};
