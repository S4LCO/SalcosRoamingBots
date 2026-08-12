# Changelog

## 0.4.1

- Changed adaptive distance scaling from a raid-wide value to an independent value for each bot.
- Added progressive per-bot retry backoff after repeated no-route searches, capped at 90 seconds.
- SRB now yields its layer during navigation backoff so normal lower-priority behavior can run.
- Added raid-end reporting for the three bots with the most no-route searches and their maximum failure streaks.
- Rechecks combat, danger, and medical state when a higher-priority BigBrain layer takes control.
- Improved post-combat destination preservation when combat preempts SRB before its regular eligibility check.
- Strengthened penalties for candidate destinations close to NavMesh edges.
- Reduced the coverage bonus slightly so unexplored fringe sectors do not overpower destination quality.

## 0.4.0

- Added lightweight 50-meter sector coverage tracking and coverage-aware destination scoring.
- Added raid-local adaptive distance scaling for maps where long route searches repeatedly fail.
- Added temporary avoidance of sectors where roaming movement became stuck or failed.
- Added path-quality scoring for excessive detours and NavMesh-edge destinations.
- Added immediate combat, danger, and medical handoff checks before SRB movement maintenance.
- Added optional post-combat destination resumption with a newly validated route.
- Added detailed raid-end coverage, handoff, resumption, and candidate-filtering statistics.
- Added free-camera bot markers with roles, distances, and current SRB states.
- Added selected-bot route visualization, bot cycling, and chase-camera follow mode.
- Preserved the existing global path-calculation budget and complete-route requirement.

## 0.3.2

- Reworked the debug free camera to move EFT's original raid camera through a late-updated flight rig.
- Preserved raid exposure, weather, post-processing, and camera effects.

## 0.3.0

- Added the optional in-raid debug free camera.

## 0.2.0

- Added lightweight raid-end roaming and navigation summaries.

## 0.1.0

- Initial SPT 4.1.x implementation using BigBrain and Waypoints.
