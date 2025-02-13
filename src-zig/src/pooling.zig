const Mutex = std.Thread.Mutex;
const std = @import("std");

pub const PooledDataManager = struct {
    // TODO: Re-evaluate whether this is likely to be called from multiple threads
    _mutex: Mutex = .{},

    _allocator: std.mem.Allocator,
    _pool: std.ArrayList(*PooledData),
    _max_pool_size: usize,

    /// The length of data stored by each item in the pool.
    data_length: usize,

    pub fn init(
        allocator: std.mem.Allocator,
        data_length: usize,
        max_pool_size: u32,
    ) PooledDataManager {
        return PooledDataManager{
            ._allocator = allocator,
            ._pool = std.ArrayList(*PooledData).initCapacity(allocator, max_pool_size) catch unreachable,
            ._max_pool_size = max_pool_size,
            .data_length = data_length,
        };
    }

    pub fn deinit(self: *const PooledDataManager) void {
        for (self._pool.items) |element| {
            releaseItem(self, element);
        }
        self._pool.deinit();
    }

    /// Get a `PooledData` instance.
    pub fn get(self: *PooledDataManager) !*PooledData {
        // Attempt to retrieve an item from the pool. Lock while doing so.
        // We don't want to return the same item to two callers at once
        self._mutex.lock();
        const item = self._pool.pop();
        self._mutex.unlock();

        if (item) |actual| {
            actual.data_start_idx = 0;
            actual.data_end_idx = 0;
            return actual;
        }

        // Couldn't get one. Make a new one and add it to the pool
        const new_item = try self._allocator.create(PooledData);
        new_item.* = PooledData{
            ._manager = self,
            .data = try self._allocator.alloc(u8, self.data_length),
        };

        return new_item;
    }

    /// Returns a `PooledData` instance to the pool. This method is called from `PooledData.releaseRef()`.
    fn put(self: *PooledDataManager, item: *PooledData) void {
        std.debug.assert(item._ref_count == 0); // Ensure nothing is still using this data

        // Can we append to the pool without going over the max size?
        if (self._pool.items.len < self._max_pool_size) {
            self._mutex.lock();
            self._pool.append(item) catch {
                releaseItem(self, item);
            };
            self._mutex.unlock();
        } else {
            // Otherwise, free the item
            releaseItem(self, item);
        }
    }

    fn releaseItem(self: *const PooledDataManager, item: *PooledData) void {
        self._allocator.free(item.data);
        self._allocator.destroy(item);
    }
};

/// Contains `data` that is stored and re-used in a pool. Do not de/init this type directly. Instead,
/// retrieve it through the `PooledDataManager` and ensure to call the `takeRef` and `releaseRef` methods.
pub const PooledData = struct {
    _manager: *PooledDataManager,
    _ref_count: i16 = 0,

    /// The data. Do not re-assign this field.
    data: []u8,
    /// The end index of the actual data stored in `data`.
    data_end_idx: usize = 0,
    /// The start index of the actual data stored in `data`.
    data_start_idx: usize = 0,

    /// Indicates that the calling scope is holding a reference to this `PooledData` instance.
    pub fn takeRef(self: *PooledData) void {
        self._ref_count += 1;
    }

    /// Indicates that the calling scope is releasing a reference to this `PooledData` instance.
    /// If no references to this instance remain it will return itself to the pool.
    pub fn releaseRef(self: *PooledData) void {
        std.debug.assert(self._ref_count > 0); // Ensure that consumers are calling addRef() and not doubly-releasing
        self._ref_count -= 1;

        if (self._ref_count == 0) {
            self._manager.put(self);
        }
    }

    /// Stores the given `data` in the pool (by copying it into `data`, starting at `data_start_idx`),
    /// and sets the `data_end_idx` field appropriately.
    pub fn storeData(self: *PooledData, data: []const u8) void {
        if (self.data_start_idx + data.len > self.data.len) {
            @panic("Data is too long to store");
        }

        @memcpy(self.data[self.data_start_idx .. self.data_start_idx + data.len], data);
        self.data_end_idx = self.data_start_idx + data.len;
    }

    /// Appends the given `data` to the pool (by copying it into `data`, starting at `data_end_idx`),
    /// and sets the `data_end_idx` field appropriately.
    pub fn appendData(self: *PooledData, data: []const u8) void {
        if (self.data_end_idx + data.len > self.data.len) {
            @panic("Data is too long to store");
        }

        @memcpy(self.data[self.data_end_idx .. self.data_end_idx + data.len], data);
        self.data_end_idx += data.len;
    }

    /// Gets a slice over the actual data stored in this instance.
    pub fn getSlice(self: @This()) []u8 {
        return self.data[self.data_start_idx..self.data_end_idx];
    }
};

pub const tests = struct {
    test "init manager" {
        const manager = PooledDataManager.init(
            std.testing.allocator,
            7,
            3,
        );
        defer manager.deinit();

        try std.testing.expectEqual(7, manager.data_length);
        try std.testing.expectEqual(3, manager._max_pool_size);
        try std.testing.expectEqual(3, manager._pool.capacity);
    }

    test "pool limits respected" {
        var manager = PooledDataManager.init(std.testing.allocator, 7, 2);
        defer manager.deinit();

        const data_1 = try manager.get();
        const data_2 = try manager.get();
        const data_3 = try manager.get();

        try std.testing.expectEqual(7, data_1.data.len);
        try std.testing.expectEqual(7, data_2.data.len);
        try std.testing.expectEqual(7, data_3.data.len);

        try std.testing.expectEqual(0, manager._pool.items.len);
        manager.put(data_1);
        manager.put(data_2);
        manager.put(data_3);
        try std.testing.expectEqual(2, manager._pool.items.len);
    }

    test "pool is utilised" {
        var manager = PooledDataManager.init(std.testing.allocator, 2, 2);
        defer manager.deinit();

        var data_1 = try manager.get();
        data_1.storeData(&[_]u8{ 1, 2 });
        manager.put(data_1);
        try std.testing.expectEqual(1, manager._pool.items.len);
        data_1 = try manager.get();
        try std.testing.expectEqual(0, manager._pool.items.len);
        try std.testing.expectEqualSlices(u8, &[_]u8{ 1, 2 }, data_1.data);
        manager.put(data_1);
    }

    test "reference taking" {
        var manager = PooledDataManager.init(std.testing.allocator, 2, 2);
        defer manager.deinit();

        var data = try manager.get();
        try std.testing.expectEqual(0, data._ref_count);

        data.takeRef();
        try std.testing.expectEqual(1, data._ref_count);
        data.takeRef();
        try std.testing.expectEqual(2, data._ref_count);

        data.releaseRef();
        try std.testing.expectEqual(0, manager._pool.items.len);
        data.releaseRef();
        try std.testing.expectEqual(1, manager._pool.items.len);
    }

    test "data manipulation" {
        var manager = PooledDataManager.init(std.testing.allocator, 3, 2);
        defer manager.deinit();

        var data = try manager.get();
        defer manager.put(data);

        // Test that store data works as expected
        try std.testing.expectEqual(0, data.data_end_idx);
        data.storeData(&[_]u8{ 1, 2, 3 });
        try std.testing.expectEqual(3, data.data_end_idx);
        try std.testing.expectEqualSlices(u8, &[_]u8{ 1, 2, 3 }, data.getSlice());

        // Test getSlice respects the indexes
        data.data_end_idx = 2;
        data.data_start_idx = 1;
        try std.testing.expectEqualSlices(u8, &[_]u8{2}, data.getSlice());

        // Append data from the end index
        data.appendData(&[_]u8{4});
        try std.testing.expectEqualSlices(u8, &[_]u8{ 2, 4 }, data.getSlice());

        // Store data with a non-zero start index
        data.storeData(&[_]u8{ 6, 7 });
        try std.testing.expectEqualSlices(u8, &[_]u8{ 6, 7 }, data.getSlice());
    }
};
