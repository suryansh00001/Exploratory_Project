# Enabling Pedestrian Safety Research in SUMO Through Externally Modelled Agents in Unity3D
This project was developed through courses of Advanced Methods of Modeling and Simulation and Social Simulation and Complex Analysis Systems from the Doctoral Program in Informatics Engineering at FEUP.

The goal was to facilitate the integration and implementation of pedestrian models in the traffic simulator SUMO. To achieve this, the SUMO simulation was recreated in Unity3D. This way new model can be implemented and verified in Unity, while keeping the great SUMO traffic simulation as it's backbone.

Currently it is possible to sync SUMO to Unity3D and extarnally simulate pedestrians in Unity with the Social Forces Model, and have that simualtion be mirrored in SUMO, while preserving all the pedestrian-vehicle interactions.

## Instalation
### Step #1: Install Sumo
Follow all the instruction given in this guide: [[https://sumo.dlr.de/docs/Installing/Windows_Build.html](https://sumo.dlr.de/docs/Installing/Windows_Build.html)]

Don't forget to install all the additional libraries and python modules.

### Step #2: Install Unity3D
The latest Unity3D version should work, but to be sure download version 2019.2.10f1.

### Step #3: Clone this repository

## Running the Simulator
For a brief VR-first setup checklist, see [README_VR_SETUP.md](README_VR_SETUP.md).

### Step 1: Run SUMO
Open command line in the root of the project and run 



```shell
C:/Users/LENOVO/anaconda3/envs/myen/python.exe ".\SUMO Network\runner.py"
```

or 

```shell
d:\SUMO2Unity\sumo-unity-distributed-pedestrian-simulator\venv\Scripts\Activate.ps1
python "SUMO Network\runner.py"
```
After the SUMO GUI window opens, press the play button or Ctrl + A.

To stop 
```shell
$targets = Get-CimInstance Win32_Process | Where-Object { $_.Name -match '^python(\.exe)?$' -and $_.CommandLine -like '*SUMO Network\runner.py*' }; if ($targets) { $targets | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }; "Stopped runner.py process IDs: $($targets.ProcessId -join ', ')" } else { 'No running runner.py process found.' }
```

### Windows commands (recommended)
Run these commands from the repository root in PowerShell:

```powershell
# 1) Install Python dependencies used by runner.py
pip install -r ".\SUMO Network\requirements.txt"

# 2) Set SUMO_HOME for this terminal (adjust if your SUMO path is different)
$env:SUMO_HOME = "C:\Program Files (x86)\Eclipse\Sumo"

# 3) Start SUMO + TraCI bridge
python ".\SUMO Network\runner.py"
```

Alternative (if you are already inside `SUMO Network`):

```powershell
pip install -r .\requirements.txt
python .\runner.py
```

Headless run (no SUMO GUI):

```powershell
python ".\SUMO Network\runner.py" --nogui
```

### Step 2: Run Unity3D scene.
Open the Unity3D project in Unity editor and select the scene [SUMO Perfect Backup](SSASC%20-%20SUMO%20Unity%20Scene/Assets/Scenes/Sumo%20Perfect%20Backup.unity).
Press play and SUMO and Unity should sync up automatically.

## Camera Setup (Top View to Pedestrian View)
If Unity is only showing a top view, switch to the headset/player camera rig used by this project.

1. Open the `Sumo Perfect Backup` scene.
2. In the Hierarchy, make sure the `[VRTK_SDKManager]` prefab exists and is active.
3. Disable or lower priority of any separate overview/top camera (check `MainCamera` tagged objects not under the VRTK rig).
4. Keep a single active gameplay camera as `MainCamera` (the camera inside the VRTK rig).
5. Select the pedestrian/player object and ensure the `SubjectController` script is enabled.
6. Make sure the player camera height is human-like (around Y = 1.6 to 1.8) on the camera rig/head transform.
7. Enter Play mode and confirm movement scripts are active (the project includes a `Locomotion.prefab` with VRTK controls).

Notes:
- This project is configured around VRTK/SteamVR camera rigs, not a fixed top-down camera.
- If there are multiple enabled cameras with `MainCamera` tag, Unity may render an unexpected view.
- If you use VR hardware, ensure XR/SteamVR is initialized before testing movement.

### Keyboard-only interaction (no VR headset)
The subject now supports a non-VR fallback controller for desktop testing.

1. Enter Play mode in Unity.
2. Make sure the `subject` object has `SubjectController` enabled.
3. Use keyboard controls:
	- `W` / `S`: move forward/backward
	- `A` / `D`: strafe left/right
	- `Q` / `E` (or Left/Right arrows): rotate view left/right

The camera is automatically placed at pedestrian eye height when VR tracking is unavailable.
