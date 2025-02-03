# Sanctuary.SoeProtocol

This repository contains reversed engineered documentation and a simple implementation of version
three of the 'SOE Protocol'. The SOE Protocol was developed by Sony Online Entertainment as a UDP
transport layer for the networking traffic of various games, newer examples of which include Free
Realms, H1Z1, Landmark and PlanetSide 2.

> [!WARNING]
> The content of this repository is reverse engineered. As such, it may be
> incomplete and/or incorrect. While it is likely applicable to many games that use the SOE
> protocol, it has been written using 2022 versions of PlanetSide 2 as a reference.

## Documentation

I have documented the SOE protocol to the extent that I understand it. This can be found under the
`docs` folder, starting [here](./docs/README.md).

## Implementation

C# and Zig implementations of the SOE protocol, including samples and tests, can be found under the
respective `src-cs` and `src-zig` directories.

The C# implementation is 'complete' and somewhat optimised but has an elusive bug that can break
longstanding connections. The Zig implementation is currently in development and aims to fix this,
along with improving the overall structure of the code and enhancing the performance and feature set.

## Other References

There are various other reverse engineered protocol/game server emulation projects containing
implementations of the SOE protocol.

- &lt;Library&gt; (&lt;Lang&gt;, [Derived from])
- [bacta/soe-archive](https://github.com/bacta/soe-archive) (Java)
- [h1emu/h1z1-server](https://github.com/H1emu/h1z1-server) (JavaScript, H1Z1)
- [kirmmin/LandmarkEmu](https://github.com/kirmmin/LandmarkEmu) (C#, Landmark)
- [Joshsora/LibSOE](https://github.com/Joshsora/LibSOE) (C#, Free Realms)
- [misterupkeep/soe-dissector](https://github.com/misterupkeep/soe-dissector) (WireShark dissector/Lua, Free Realms)
- [psemu/soe-network](https://github.com/psemu/soe-network/) (JavaScript, PlanetSide 2)
- [thoop/swg](https://github.com/thoop/swg) (JavaScript, Star Wars: Galaxies)
