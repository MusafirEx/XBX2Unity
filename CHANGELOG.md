# Changelog

## 0.3.0 - 2026-11-02
- Add selected `.cpk` archive support in the Unity Editor.
- Invoke external CriPakTool to list/extract archives directly from selected Project assets.
- Add CPK header validation, 2 GiB warning, unique extraction folders, process log, Stop, and CASM/CANV handoff.
- Include CPK tool in UnityPackage and UPM ZIP build.


## 0.2.0 - 2026-11-02
- Initialize XBX2Unity as a Unity Package Manager package.
- Add safe, editor-only CASM/CANV file discovery and binary audit.
- Add external casmExtract process wrapper with PNG and BC5 options, result logging and cancellation.
- Add documented limitations and game-data safeguards.
