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
            ._pool = std.ArrayList(*PooledData).init(allocator),
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
        const item = self._pool.popOrNull();
        if (item) |actual| {
            actual.data_start_idx = 0;
            actual.data_end_idx = 0;
            return actual;
        }
        self._mutex.unlock();

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

    /// Stores the given `data` into the pool item, and sets the `data_len` field appropriately.
    pub fn storeData(self: *PooledData, data: []u8) void {
        if (self.data_start_idx + data.len > self.data.len) {
            @panic("Data is too long to store");
        }

        @memcpy(self.data[self.data_start_idx .. self.data_start_idx + data.len], data);
        self.data_end_idx = self.data_start_idx + data.len;
    }

    /// Gets a slice over the actual data stored in this instance.
    pub fn getSlice(self: @This()) []u8 {
        return self.data[self.data_start_idx..self.data_end_idx];
    }
};
