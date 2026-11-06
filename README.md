# Planet Crafter Mod Menu

A BepInEx 5 mod for **The Planet Crafter** (works on the free Prologue/Demo and the full game). It adds a
**Mods** button to the main menu.

- **Lists every installed mod**: loose DLLs in `BepInEx/plugins` and one folder per mod.
- **ON/OFF toggle** for each mod. It renames the mod's `.dll` to `.dll.disabled` and back.
- **Install from a GitHub repo link.** Paste `https://github.com/owner/repo` (or `owner/repo`) and press Install.
  The menu downloads the files attached to the repo's **latest release** (`.dll` plus model, texture and sound files
  like `.obj .mtl .png .wav .json`) into `BepInEx/plugins/<repo>/`.
  If the same `.dll` was installed by hand somewhere else in `plugins`, that copy is turned off so the mod doesn't load twice.
- **Update** and **Remove** buttons for mods installed from a repo.
- **Restart now** relaunches the game through Steam so your changes take effect.

> Mods are code that runs on your PC. Only install from repos you trust.

## Install
1. Install [BepInEx 5 (win x64)](https://github.com/BepInEx/BepInEx/releases) into the game folder and run the game once.
2. Download `PlanetCrafterModMenu.dll` from this repo's [Releases](../../releases) and put it in
   `BepInEx/plugins/planet-crafter-mod-menu/`.

## Try it
Open **Mods**, paste `guyka2212/planet-crafter-drone-fpv` and press Install to get a flyable FPV drone with acro mode.

## Making your mod installable from the menu
Publish a GitHub release and attach your plugin `.dll` (and any data files it loads from its own folder) as release assets.
Example: [planet-crafter-drone-fpv](https://github.com/guyka2212/planet-crafter-drone-fpv).

## Build from source
Run `./build.sh` (Git Bash). It needs only the `csc.exe` that ships with Windows and your own game install. It compiles
against the game's DLLs, and none of them are included here.

## Files the menu writes
`BepInEx/config/guy.planetcrafter.modmenu.registry.txt` stores each mod's name, version and source repo.

## Credits
Ported from [gunk-mod-menu](https://github.com/guyka2212/gunk-mod-menu). Built with Claude (AI) using the universal-modder
toolkit. Loader: BepInEx. No game files are included.
