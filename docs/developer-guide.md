# Developer Guide

## Building the repository

Panopticon uses the standard .NET Core tooling. Install the tools and build normally (dotnet build).

### Windows

Solution file: `src/Panopticon.sln`, openable directly in Visual Studio.

### Linux & MacOS

Build via the .NET Core CLI:

```shell
cd src
dotnet build
```

Also works on Windows as an alternative to Visual Studio.

#### Linux dependencies

- .NET 8.0+
- `libc`
- `libsdl2`
- `libopenal`

##### .NET 8.0+

Minimum supported version: .NET 8.0+. Install via the [official Linux installation guide](https://learn.microsoft.com/en-us/dotnet/core/install/linux), or a distro package if preferred: [Ubuntu](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu-install?tabs=dotnet8&pivots=os-linux-ubuntu-2404), [Fedora](https://learn.microsoft.com/en-us/dotnet/core/install/linux-fedora?tabs=dotnet8).

##### `libc`

- APT based: `sudo apt install libc-dev`
- RPM based: `sudo dnf install glibc-devel`.

Some distros or package managers may install the file libdl.so.2 and not libdl.so. This can be remedied via a symlink ([source](https://github.com/mellinoe/veldrid/issues/143#issuecomment-446096640)):

- use `ldconfig -p | grep libdl` to dtermine the location of `libdl.so.2` (or similar)
- `sudo ln -s </path/to/libdl.so.2> </same/path/libdl.so>`

```shell
➜  ~ ldconfig -p | grep libdl
  libdl.so.2 (libc6,x86-64) => /lib/x86_64-linux-gnu/libdl.so.2
➜  ~ sudo ln -s /lib/x86_64-linux-gnu/libdl.so.2 libdl.so
➜  ~ ls -l /lib/x86_64-linux-gnu | grep libdl
-rw-r--r--  1 root root         8 Oct  7 01:13 libdl.a
lrwxrwxrwx  1 root root        32 Nov  6 00:12 libdl.so -> /lib/x86_64-linux-gnu/libdl.so.2
-rw-r--r--  1 root root     14480 Oct  7 01:13 libdl.so.2
➜  ~ 
```

##### `libsdl2`

- APT based: `sudo apt install libsdl2-dev`
- RPM based: `sudo dnf install sdl2-compat-devel`

##### `openal`

- APT based: `sudo apt install libopenal-dev`
- RPM based: `sudo apt install openal-soft-devel`

## Running Panopticon

Panopticon does not provide any game assets itself, so it expects the user to have the game files installed on the system.

### Windows

On Windows any local installations of C&C or BFME/BFME2/BFME2ROTWK are found through the registry or if they are defined by a environment variables. (See Linux & MacOS below)

### Linux & MacOS

Since there are no official installers of the original games on Unix systems, the game installations must be defined through environment variables here. The list of environment variables is as follows:

| Game      | Environment variable |
| --------- | -------------------- |
| Generals  | CNC_GENERALS_PATH    |
| Zero Hour | CNC_GENERALS_ZH_PATH |
| BFME      | BFME_PATH            |
| BFME II   | BFME2_PATH           |
| BFME II   | BFME2_ROTWK_PATH     |

The same environment variables also work on Windows, as an alternative to the registry.

### Starting Panopticon

Entry point: `Panopticon.Launcher` project. Default game: C&C Generals. Game selection: `-g <identifier>`
(e.g. `-g bfme` for Battle for Middle-earth), contingent on a configured installation location.

### Developer Mode

Toggle: `F11` inside the launcher. Function: engine-internals display, for debugging.

## Units

Runtime distance unit: meters. Positions, lengths, bounds, and translations are meters; authored
rates are meters per second; per-logic-frame values are meters per logic frame (acceleration:
meters per logic frame squared). Angles, health, damage, costs, ratios, cells, and pixels are not
lengths and carry no conversion.

Source conversion applies once at each source boundary, with the inverse on legacy write:

| Boundary | Meters per source unit |
|---|---|
| Generals INI parse, map read/write, W3D/glTF/NIF import, replay and save read/write | 0.3048 (one foot; `UnitConversionPolicy.GeneralsFeetToMeters`) |
| BFME, BFME II, RotWK | 0.3048, provisional compatibility scale, not a measured calibration |
| EE2 unit import | similarity scale of the profile source distance: mixed profile 12 x 0.3048 = 3.6576 m; reference profiles 18.2505 x 0.3048 = 5.5627524 m |

Entry point: `UnitConversionPolicy` in
`src/Panopticon.Simulation/Content/Importing/Units/Materialization/UnitConversionPolicy.cs`,
declared per game by `SourceUnitPolicy` with no default fallthrough. Design and seam inventory:
[the H2b metric deep dive](roadmap/deep-dives/h2b-metric-core.md).
