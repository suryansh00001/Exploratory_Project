# VR Setup (Quick Guide)

This is a short setup guide to run this project with a VR headset.

## 1. Prerequisites
- Unity 2019.2.10f1 (recommended by this project)
- Steam and SteamVR installed and working
- A supported PC VR headset
- For wired mode: USB-C Link cable (or native PCVR cable for your headset)
- For wireless mode: strong 5 GHz or Wi-Fi 6 network (PC on Ethernet recommended)
- SUMO installed on Windows

## 2. Open the Correct Unity Scene
1. Open the Unity project folder: SSASC - SUMO Unity Scene
2. Open scene: Assets/Scenes/Sumo Perfect Backup.unity

## 3. Verify VR Rig in Unity
1. In Hierarchy, confirm [VRTK_SDKManager] exists and is active.
2. Ensure only one gameplay camera is active as MainCamera.
3. Disable extra top/overview cameras if they override the headset view.
4. Confirm the subject/player has SubjectController enabled.

## 4. Start SUMO Bridge First
From repository root in PowerShell:

```powershell
pip install -r ".\SUMO Network\requirements.txt"
$env:SUMO_HOME = "C:\Program Files (x86)\Eclipse\Sumo"
python ".\SUMO Network\runner.py"
```

Wait until the SUMO GUI opens, then press Play (or Ctrl + A).

## 5. Choose Connection Method

### Method A: Wired (recommended first test)
1. Connect headset to PC with Link/PCVR cable.
2. Launch your headset PC-link mode (for example, Link on Meta headsets).
3. Start SteamVR and confirm headset + controllers are tracking.
4. In Unity, press Play in Sumo Perfect Backup scene.
5. Confirm first-person view updates with your headset movement.

### Method B: Wireless (Air Link / Streaming)
1. Put headset and PC on the same fast local network.
2. Keep PC connected by Ethernet when possible.
3. Start wireless PCVR mode from the headset (Air Link or your streaming app).
4. Launch SteamVR and verify stable tracking.
5. In Unity, press Play in Sumo Perfect Backup scene.

If wireless has delay or jitter, reduce streaming resolution/bitrate, close heavy downloads, and retest.

## 6. If You See Top-Down Camera Instead
- Check there are no other enabled cameras tagged MainCamera.
- Keep the VRTK rig camera active.
- Recenter in SteamVR and re-enter Play mode.
- For wireless mode, also confirm the streaming session did not fall back to 2D desktop view.

## 7. Quick Fallback (No Headset)
You can still test movement with keyboard controls in Play mode:
- W/S: forward/backward
- A/D: strafe
- Q/E or Left/Right arrows: rotate
