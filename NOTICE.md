# Data and dependencies

The original application source in this repository is licensed under MIT.

Aion 2 and its game names belong to their respective owners. This project is
independent and is not affiliated with or endorsed by the game publisher.

Protocol layouts were investigated using passive local recordings and static
examination of an installed AionFlex build. The parser here is an independent
implementation. No third-party application binaries, decompiled source, raw
player recordings, character-name caches, credentials, or accounts are distributed.

`src/LocalDps/Data/skill-rules.json` contains numeric skill classification facts
used to exclude known healing and non-damage effects. Its source and source-file
digests are recorded in the JSON. Those classifications remain provisional;
they are not an independent damage-accuracy benchmark. No skill descriptions,
icons, names, or full third-party skill database are included.

The bundled English enemy-name catalog is adapted from taengu/AION2-DPS-Meter
(MIT), commit 2fa3c3e186426ff77db81a6608c87030d529ad7c:
https://github.com/taengu/AION2-DPS-Meter/blob/2fa3c3e186426ff77db81a6608c87030d529ad7c/src/main/resources/i18n/npcs/en.json

Source SHA-256: 0c5a4d67058ef39105f71e0979b39af57c305f64e929a35c42dc955fdfe61b3d.
Conversion retains all 6,084 template IDs and their name fields, sorts by numeric
ID, and omits the isBoss field. No private AionFlex name table is distributed.
The upstream license is reproduced below. Optional local target-names.json
entries can override or extend this catalog; see the README.

## Official class emblems

The eight class emblems embedded in the application are official AION 2 artwork:
© NC Corporation. All Rights Reserved. They are used to identify classes within
this unofficial fan tool and are not covered by this repository's MIT license.
They are not offered as a standalone asset pack or sublicensed for other uses.

Source: https://aion2.plaync.com/en-us/about/index#classes
Official fan-content guidelines:
https://aion2.plaync.com/ja-jp/board/rules/view?articleId=6a7d9723bcc86319892231b1

`src/LocalDps/Assets/Classes/provenance.json` records each original NC CDN URL and
SHA-256 digest. WebP originals were decoded to PNG for WPF compatibility without
redrawing, resizing, recoloring or cropping. The application reads the embedded
images and makes no network requests for them. This project is not affiliated
with, endorsed by, or sponsored by NC Corporation.

Npcap is a separately installed dependency governed by its own license:
https://npcap.com/#license. Its installer, DLLs, and driver are not redistributed.
Microsoft .NET is also installed separately.

## Bundled NPC catalog upstream license

MIT License

Copyright (c) 2026 TK-open-public

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
