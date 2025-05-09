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
    //lib_zlib.installHeader(b.path("lib/zlib/zlib.h"), "zlib.h");

    // Add our core components as a static library
    const lib = b.addStaticLibrary(.{
        .name = "sanctuary_soe_protocol",
        .root_source_file = b.path("src/root.zig"),
        .target = target,
        .optimize = optimize,
    });

    // Link libraries
    lib.linkLibrary(lib_zlib);

    // This declares intent for the library to be installed into the standard
    // location when the user invokes the "install" step (the default step when
    // running `zig build`).
    b.installArtifact(lib);
    b.installArtifact(lib_zlib);

    // ===== zig build test =====

    // Creates a step for unit testing. This only builds the test executable
    // but does not run it.
    const test_units = b.addTest(.{
        .root_source_file = b.path("src/root.zig"),
        .target = target,
        .optimize = optimize,
    });

    // Link libraries
    test_units.linkLibrary(lib_zlib);
    test_units.addIncludePath(b.path("lib/zlib"));
    // Mark the test binary to be installed when the "install" step is invoked
    b.installArtifact(test_units);

    // Create an executable artifact, and a build step to execute it ('zig build test')
    const run_lib_unit_tests = b.addRunArtifact(test_units);
    const test_step = b.step("test", "Run unit tests");
    test_step.dependOn(&run_lib_unit_tests.step);

    // ===== zig build run =====

    // Create a new executable named `root` from `src/root.zig`
    const exe = b.addExecutable(.{
        .name = "sample_ping_pong",
        .root_source_file = b.path("src/sample_ping_pong.zig"),
        .target = target,
        .optimize = optimize,
    });

    // Link libraries
    exe.linkLibrary(lib_zlib);
    exe.addIncludePath(b.path("lib/zlib"));
    // Mark the root binary to be installed when the "install" step is invoked
    b.installArtifact(exe);

    // Add an executable artifact, and a build step to execute it ('zig build run')
    const run_exe = b.addRunArtifact(exe);
    const run_step = b.step("run", "Run the sample executable");
    run_step.dependOn(&run_exe.step);
}
