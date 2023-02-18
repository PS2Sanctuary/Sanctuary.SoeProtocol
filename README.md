# Sanctuary.SoeProtocol

This repository contains reversed engineered documentation, and soon an implementation of, the
'SOE Protocol'. It was developed by Sony Online Entertainment as a UDP transport layer for the
networking traffic of various games, newer examples of which include Free Realms, H1Z1, Landmark
and PlanetSide 2.

> **Warning**: the contents of this repository are reverse engineered. As such, it may be
> incomplete and/or incorrect. While it is likely applicable to many games that use the SOE
> protocol, it has been written using 2022 versions of PlanetSide 2 as a reference.

## Documentation

I have documented the SOE protocol to the extent that I understand it. This can be found under the
`docs` folder, starting [here](./docs/README.md).

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