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

    // Add zlib as a static library
    const lib_zlib = b.addStaticLibrary(.{
        .name = "zlib",
        .target = target,
        .optimize = optimize,
        .link_libc = true,
    });
    lib_zlib.addCSourceFiles(
        .{
            .files = &.{
                "lib/zlib/adler32.c",
                "lib/zlib/compress.c",
                "lib/zlib/crc32.c",
                "lib/zlib/deflate.c",
                "lib/zlib/gzclose.c",
                "lib/zlib/gzlib.c",
                "lib/zlib/gzread.c",
                "lib/zlib/gzwrite.c",
                "lib/zlib/infback.c",
                "lib/zlib/inflate.c",
                "lib/zlib/inffast.c",
                "lib/zlib/inftrees.c",
                "lib/zlib/trees.c",
                "lib/zlib/uncompr.c",
                "lib/zlib/zutil.c",
            },
            .flags = &.{"-std=c89"},
        },
    );

    // Add our core components as a static library
    const lib = b.addStaticLibrary(.{
        .name = "soe-protocol",
        // In this case the main source file is merely a path, however, in more
        // complicated build scripts, this could be a generated file.
        .root_source_file = b.path("src/root.zig"),
        .target = target,
        .optimize = optimize,
    });

    // Add 3rd-party libraries
    lib.linkLibrary(lib_zlib);
    lib.addIncludePath(b.path("lib/zlib"));

    // Add 3rd-party dependencies as modules
    lib.root_module.addImport("network", b.dependency("network", .{}).module("network"));

    // This declares intent for the library to be installed into the standard
    // location when the user invokes the "install" step (the default step when
    // running `zig build`).
    b.installArtifact(lib);
    b.installArtifact(lib_zlib);

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

    // Create a new executable named `soe-protocol-sample` from `src/root.zig`
    const exe = b.addExecutable(.{
        .name = "soe-protocol-sample",
        .root_source_file = b.path("src/root.zig"),
        .target = target,
        .optimize = optimize,
    });
    // Link zlib against this executable
    exe.linkLibrary(lib_zlib);

    // Add a run artifact and step ('zig build run')
    const run_exe = b.addRunArtifact(exe);
    const run_step = b.step("run", "Run the sample executable");
    run_step.dependOn(&run_exe.step);
}
