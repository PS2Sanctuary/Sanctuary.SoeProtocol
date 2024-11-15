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
        // Lock while we try to retrieve from the pool. We don't want to return the same item
        // to two callers at once
        self._mutex.lock();
        defer self._mutex.unlock();

        // Attempt to retrieve an item from the pool
        const item = self._pool.popOrNull();
        if (item) |actual| {
            return actual;
        }

        // Couldn't get one. Make a new one and add it to the pool
        var new_item = try self._allocator.create(PooledData);
        new_item._manager = self;
        new_item.data = try self._allocator.alloc(u8, self.data_length);
        new_item.takeRef();
        try self._pool.append(new_item);
        return new_item;
    }

    /// Returns a `PooledData` instance to the pool. This method is called from `PooledData.releaseRef()`.
    fn put(self: *PooledDataManager, item: *PooledData) void {
        // Can we append to the pool without going over the max size?
        if (self._pool.items.len < self._max_pool_size) {
            self._pool.append(item) catch {
                releaseItem(self, item);
            };
        } else {
            // Otherwise, free the item
            releaseItem(self, item);
        }
    }

    fn releaseItem(self: *const PooledDataManager, item: *PooledData) void {
        self._allocator.free(item.data);
        item._ref_count = 0;
        self._allocator.destroy(item);
    }
};

/// Contains `data` that is stored and re-used in a pool. Do not de/init this type directly. Instead,
/// retrieve it through the `PooledDataManager` and ensure to call the `takeRef` and `releaseRef` methods.
pub const PooledData = struct {
    _manager: *PooledDataManager,
    _ref_count: i16,

    /// The data. Do not re-assign this field.
    data: []u8,
    /// The actual length of the data stored in `data`
    data_len: usize,

    /// Indicates that the calling scope is holding a reference to this `PooledData` instance.
    pub fn takeRef(self: *PooledData) void {
        self._ref_count += 1;
    }

    /// Indicates that the calling scope is releasing a reference to this `PooledData` instance.
    pub fn releaseRef(self: *PooledData) void {
        self._ref_count -= 1;

        // It should just be the manager holding a ref on us. Let's return to the pool
        if (self._ref_count == 1) {
            self._manager.put(self);
        }
    }

    pub fn storeData(self: @This(), data: []u8) void {
        if (data.len > self.data.len) {
            @panic("Data is too long to store");
        }

        @memcpy(self.data, data);
        self.data_len = data.len;
    }

    /// Gets a slice over the actual data stored in this instance.
    pub fn getSlice(self: @This()) []u8 {
        return self.data[0..self.data_len];
    }
};
