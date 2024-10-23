# soe-protocol

A Zig version of the PS2Sanctuary SOE Protocol libraries.

## Building & Testing

Ensure all submodules are initialised:

```sh
git submodule update --init
```

#### Build

```sh
zig build
```

This will produce binaries in `./zig-out`, specifically:

`./zig-out/libsoe-protocol.a`

#### Test

```sh
zig build test
# Verbose
zig build test --summary all
```

If you aren't using the verbose output, then nothing being printed to he console indicates that all
tests passed.

## Repository Maintenance

### Updating Submodules

Change into the directory of the submodule, e.g.

```sh
cd ./lib/zlib
```

Then checkout the particular branch / tag / commit that you desire:

```sh
git pull
git checkout 2.2.2
```

Now head back to the root folder and commit the change:

```sh
cd ../..
# Add the submodule directory
git add lib/zlib
git commit ...
git push
```

And finally, all other repositories will need to update the submodule:

```sh
git pull
git submodule update --init
```