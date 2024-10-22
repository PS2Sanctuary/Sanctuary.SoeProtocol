const std = @import("std");

// Although this function looks imperative, note that its job is to
// declaratively construct a build graph that will be executed by an external
// runner.
pub fn build(b: *std.Build) void {
    // Standard target options allows the person running `zig build` to choose
    // what target to build for. Here we do not override the defaults, which
    // means any target is allowed, and the default is native. Other options
    // for restricting supported target set are available.
    const target = b.standardTargetOptions(.{});

    // Standard optimization options allow the person running `zig build` to select
    // between Debug, ReleaseSafe, ReleaseFast, and ReleaseSmall. Here we do not
    // set a preferred release mode, allowing the user to decide how to optimize.
    const optimize = b.standardOptimizeOption(.{});

    const zlibng = b.addStaticLibrary(.{
        .name = "zlib-ng",
        .target = target,
        .optimize = optimize,
        .link_libc = true,
    });
    zlibng.addConfigHeader(createZlibNgConfigHeader(b, "gzread.c"));
    zlibng.addConfigHeader(createZlibNgConfigHeader(b, "zlib_name_mangling.h"));
    zlibng.addConfigHeader(createZlibNgConfigHeader(b, "zlib_name_mangling-ng.h"));
    zlibng.addConfigHeader(createZlibNgConfigHeader(b, "zconf.h"));
    zlibng.addConfigHeader(createZlibNgConfigHeader(b, "zconf-ng.h"));
    zlibng.addConfigHeader(createZlibNgConfigHeader(b, "zlib.h"));
    zlibng.addConfigHeader(createZlibNgConfigHeader(b, "zlib-ng.h"));
    zlibng.addCSourceFiles(
        .{
            .files = &.{
                "lib/zlib-ng/adler32.c",
                "lib/zlib-ng/compress.c",
                "lib/zlib-ng/cpu_features.c",
                "lib/zlib-ng/crc32_braid_comb.c",
                "lib/zlib-ng/crc32.c",
                "lib/zlib-ng/deflate_fast.c",
                "lib/zlib-ng/deflate_huff.c",
                "lib/zlib-ng/deflate_medium.c",
                "lib/zlib-ng/deflate_quick.c",
                "lib/zlib-ng/deflate_rle.c",
                "lib/zlib-ng/deflate_slow.c",
                "lib/zlib-ng/deflate_stored.c",
                "lib/zlib-ng/deflate.c",
                "lib/zlib-ng/functable.c",
                "lib/zlib-ng/gzlib.c",
                "lib/zlib-ng/gzread.c",
                "lib/zlib-ng/gzwrite.c",
                "lib/zlib-ng/infback.c",
                "lib/zlib-ng/inflate.c",
                "lib/zlib-ng/inftrees.c",
                "lib/zlib-ng/insert_string_roll.c",
                "lib/zlib-ng/insert_string.c",
                "lib/zlib-ng/trees.c",
                "lib/zlib-ng/uncompr.c",
                "lib/zlib-ng/zutil.c",
            },
            .flags = &.{"-std=c11"},
        },
    );
    zlibng.addIncludePath(.{ .src_path = .{ .owner = b, .sub_path = "lib/zlib-ng" } });
    //zlibng.installConfigHeader(zlibng_config_header_zlibng);
    //zlibng.installHeader(.{ .src_path = .{ .owner = b, .sub_path = "zlib.h" } }, "lib/zlib-ng/zlib.h.in");

    const lib = b.addStaticLibrary(.{
        .name = "soe-protocol",
        // In this case the main source file is merely a path, however, in more
        // complicated build scripts, this could be a generated file.
        .root_source_file = b.path("src/root.zig"),
        .target = target,
        .optimize = optimize,
    });
    lib.linkLibrary(zlibng);

    // Add 3rd-party dependencies as modules
    lib.root_module.addImport("network", b.dependency("network", .{}).module("network"));

    // This declares intent for the library to be installed into the standard
    // location when the user invokes the "install" step (the default step when
    // running `zig build`).
    b.installArtifact(lib);

    // Creates a step for unit testing. This only builds the test executable
    // but does not run it.
    const lib_unit_tests = b.addTest(.{
        .root_source_file = b.path("src/test_root.zig"),
        .target = target,
        .optimize = optimize,
    });

    const run_lib_unit_tests = b.addRunArtifact(lib_unit_tests);

    // Similar to creating the run step earlier, this exposes a `test` step to
    // the `zig build --help` menu, providing a way for the user to request
    // running the unit tests.
    const test_step = b.step("test", "Run unit tests");
    test_step.dependOn(&run_lib_unit_tests.step);
}

fn createZlibNgConfigHeader(b: *std.Build, comptime header_file: []const u8) *std.Build.Step.ConfigHeader {
    return b.addConfigHeader(
        .{
            .include_path = header_file,
            .style = .{
                .cmake = .{
                    .src_path = .{
                        .owner = b,
                        .sub_path = "lib/zlib-ng/" ++ header_file ++ ".in",
                    },
                },
            },
        },
        .{
            .ZLIB_SYMBOL_PREFIX = "",
        },
    );
}
