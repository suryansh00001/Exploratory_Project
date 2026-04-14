# SUMO2Unity Workspace

A unified workspace for traffic co-simulation research using SUMO and Unity.

This repository includes three complementary simulators:
- Car-Bike-Cycle simulator for mixed vehicle traffic synchronization and performance analytics.
- Pedestrian simulator for externally modeled pedestrian dynamics mirrored in SUMO.
- Pedestrian-VR simulator for immersive pedestrian and traffic safety studies in VR.

## Workspace Components

| Component | Path | Primary Purpose |
|---|---|---|
| Car-Bike-Cycle simulator | ./Car-Bike-Cycle-simulator | Vehicle-centric co-simulation, road import, runtime synchronization, metrics export |
| Pedestrian simulator | ./Pedestrian-simulator | Pedestrian behavior modeling in Unity with SUMO state synchronization |
| Pedestrian-VR simulator | ./Pedestrian-VR-simulator | VR-based pedestrian co-simulation and headset-oriented runtime setup |

## System Architecture

```mermaid
flowchart LR
		A[SUMO Network and Scenario Files] --> B[TraCI or Socket Integration Layer]
		B --> C[Unity Runtime Controllers]
		C --> D[3D Visualization and Agent Logic]
		C --> E[Performance and Trajectory Logging]
		E --> F[Results and Reports]
```

## End-to-End Workflow

```mermaid
flowchart TD
		S1[Prepare Scenario Files] --> S2[Launch SUMO Runner]
		S2 --> S3[Open Unity Scene]
		S3 --> S4[Start Integration Layer]
		S4 --> S5[Run Co-Simulation]
		S5 --> S6[Collect FPS and Agent Reports]
		S6 --> S7[Analyze Safety and Behavior Outcomes]
```

## Quick Navigation

- Main Unity solution (vehicle simulator): ./Car-Bike-Cycle-simulator/SUMO2Unity.sln
- Pedestrian SUMO runner: ./Pedestrian-simulator/SUMO Network/runner.py
- Vehicle scenario package: ./Car-Bike-Cycle-simulator/scenario1
- Project READMEs:
	- ./Car-Bike-Cycle-simulator/README.md
	- ./Pedestrian-simulator/README.md
	- ./Pedestrian-VR-simulator/README.md
	- ./Pedestrian-VR-simulator/README_VR_SETUP.md

## Practical Notes

- Generated or cached directories (for example `Library`, `Temp`, `obj`, `venv`, `.venv`) should not be used as authoritative documentation sources.
- Use the project-level READMEs as the source of truth for setup, execution, and troubleshooting.
