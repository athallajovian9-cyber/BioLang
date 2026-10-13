# BioLang native build documentation

This project can be built as a fully standalone native executable using .NET Native AOT.

## Prerequisites

- .NET 10 SDK
- A target platform RID (for example: linux-x64, linux-arm64, win-x64, osx-arm64)

## Build a native binary

```bash
dotnet publish src/BioLang.csproj \
  -c Release \
  -r linux-x64 \
  -p:PublishAot=true \
  -p:InvariantGlobalization=true \
  -o dist/native/linux-x64
```

The generated binary is located at:

```text
dist/native/linux-x64/biolang
```

## Run a BioLang program

```bash
./dist/native/linux-x64/biolang tests/fibonacci.bio
```

## Build all recommended targets

```bash
bash scripts/publish-native.sh
```

This script builds for the recommended targets:

- linux-x64
- linux-arm64
- win-x64
- osx-arm64

## Notes

- Native AOT produces a single binary with no .NET runtime dependency.
- The project already contains the self-hosting bootstrap files under `selfhost/`.
- When the app is packaged for release, those files can be embedded or copied beside the binary for a fully deployable standalone distribution.
