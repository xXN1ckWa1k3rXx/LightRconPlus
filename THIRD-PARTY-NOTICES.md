# Third-party notices

## Procon Frostbite / Procon 1

LightRcon+ is inspired by Procon Frostbite. Development consulted the [Mik4sa/Procon-1 repository](https://github.com/Mik4sa/Procon-1), originally developed by Myrcon, Geoffrey 'Phogue' Green and other Procon contributors. This project does **not** claim clean-room development.

Embedded map/mode catalogs, map-specific faction/role tables and selected weapon/message mappings contain converted or adapted Procon-origin material. LightRcon+ is distributed under GPLv3 with this provenance retained. Full Procon C# application files, UI assets and binaries are not compiled or distributed in the application. This does not imply that derived resources are free of Procon material.

Upstream sources:

- [BF3.def](https://github.com/Mik4sa/Procon-1/blob/master/src/Resources/Configs/BF3.def), [BF4.def](https://github.com/Mik4sa/Procon-1/blob/master/src/Resources/Configs/BF4.def), [BFHL.def](https://github.com/Mik4sa/Procon-1/blob/master/src/Resources/Configs/BFHL.def): map identifiers/names, mode combinations, factions/roles and weapon identifiers.
- [English localization (au.loc)](https://github.com/Mik4sa/Procon-1/blob/master/src/Resources/Localization/au.loc): selected weapon/cause labels and Commo Rose references. The full localization file is not embedded. Interface translations are maintained in LightRcon+'s own resource catalog.
- Packet, FrostbiteConnection, FrostbiteClient, BF3Client, BF4Client, BFHLClient, TeamScore and uscMaplist implementations: behavioral references for packet flags, responses, rounded scores, game differences and rotation commands.
- [Upstream license notices](https://github.com/Mik4sa/Procon-1/blob/master/doc/LICENSES.txt).

LightRcon+ modifications (2026): conversion to JSON, separation by game, selection of labels, supplemental BF4 aliases, and integration into a .NET 10/WPF application. `tools/import-game-data.ps1` converts BF3/BFHL resources. Development-only reference downloads are excluded from Git and publishing.

The original Procon notice for the derived material follows (the historic project name is retained):

```text
BFBC2 PRoCon
Copyright 2010 Geoffrey 'Phogue' Green

    This file is part of BFBC2 PRoCon.

    BFBC2 PRoCon is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    BFBC2 PRoCon is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with BFBC2 PRoCon. If not, see <http://www.gnu.org/licenses/>.
```

Full GPLv3 terms are in [LICENSE](LICENSE). Upstream material retains its GPLv3-or-later grant; LightRcon+'s own grant is GPL-3.0-only.

## Battlefield / Electronic Arts / DICE

Battlefield 3, Battlefield 4, Battlefield Hardline, associated marks and game content belong to Electronic Arts, DICE and their respective rights holders. Battlefield Hardline was developed by Visceral Games. These names identify compatible games; this project's license does not grant trademark rights.

Development also consulted the BF4 R12 PC Server Administration / Remote Administration Protocol documentation and map/mode tables published by DICE / Electronic Arts. These older references do not guarantee current server behavior and are not redistributed here.

LightRcon+ is not affiliated with, endorsed by or sponsored by EA, DICE, Visceral Games or Procon contributors.

## Microsoft components

The application uses .NET, WPF, Windows APIs and installed Windows fonts/glyphs. Framework-dependent releases use the separately installed runtime. Self-contained releases include Microsoft runtime files under their applicable licenses/notices; those terms are not replaced by LightRcon+'s GPL. No Procon icon packs, GeoIP databases or MySQL connector are bundled.
