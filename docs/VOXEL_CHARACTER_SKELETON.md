# Voxel Character Skeleton & Animation System

## Design Philosophy
All characters (commander, troops) share a **universal voxel skeleton** - a hierarchy of body-part nodes that can be independently positioned and rotated for animation. Each body part is a separate MeshInstance3D built from a voxel sub-grid.

The skeleton is NOT a Godot Skeleton3D with bones - it's a scene tree hierarchy where each body part is a child node with a local pivot point, allowing simple Transform-based animation.

## Universal Skeleton Hierarchy

```
CharacterRoot (Node3D)
  |
  +-- Hips (Node3D) ............... pivot: center of pelvis
  |     |
  |     +-- Spine (Node3D) ........ pivot: bottom of torso
  |     |     |
  |     |     +-- Torso (MeshInstance3D)
  |     |     |
  |     |     +-- Neck (Node3D) ... pivot: top of torso
  |     |     |     |
  |     |     |     +-- Head (MeshInstance3D)
  |     |     |
  |     |     +-- LeftShoulder (Node3D) . pivot: left shoulder joint
  |     |     |     |
  |     |     |     +-- LeftUpperArm (MeshInstance3D)
  |     |     |     |
  |     |     |     +-- LeftElbow (Node3D) . pivot: elbow joint
  |     |     |           |
  |     |     |           +-- LeftForearm (MeshInstance3D)
  |     |     |           |
  |     |     |           +-- LeftHand (MeshInstance3D)
  |     |     |
  |     |     +-- RightShoulder (Node3D) . pivot: right shoulder joint
  |     |           |
  |     |           +-- RightUpperArm (MeshInstance3D)
  |     |           |
  |     |           +-- RightElbow (Node3D)
  |     |                 |
  |     |                 +-- RightForearm (MeshInstance3D)
  |     |                 |
  |     |                 +-- RightHand (MeshInstance3D)
  |     |
  |     +-- LeftHip (Node3D) ...... pivot: left hip socket
  |     |     |
  |     |     +-- LeftThigh (MeshInstance3D)
  |     |     |
  |     |     +-- LeftKnee (Node3D) . pivot: knee joint
  |     |           |
  |     |           +-- LeftShin (MeshInstance3D)
  |     |           |
  |     |           +-- LeftFoot (MeshInstance3D)
  |     |
  |     +-- RightHip (Node3D)
  |           |
  |           +-- RightThigh (MeshInstance3D)
  |           |
  |           +-- RightKnee (Node3D)
  |                 |
  |                 +-- RightShin (MeshInstance3D)
  |                 |
  |                 +-- RightFoot (MeshInstance3D)
  |
  +-- WeaponMount (Node3D) ........ for troops holding items
```

## Joint Rotation Axes

| Joint | Primary Axis | Secondary | Range | Notes |
|-------|-------------|-----------|-------|-------|
| Hips | Y (turn) | X (tilt) | Y: 360, X: +-20 | Root motion pivot |
| Spine | X (lean fwd/back) | Z (lean side) | X: +-30, Z: +-15 | Breathing, recoil |
| Neck | Y (look L/R) | X (look up/down) | Y: +-70, X: +-40 | Head tracking |
| Shoulder | X (swing fwd/back) | Z (raise/lower) | X: +-120, Z: +-90 | Walk swing, salute |
| Elbow | X (bend) | - | X: 0 to -140 | Only bends inward |
| Hip | X (swing fwd/back) | Z (spread) | X: +-80, Z: +-30 | Walk stride |
| Knee | X (bend) | - | X: 0 to 130 | Only bends backward |

## Animation Clips

### Walk Cycle (1.0s loop, 2 steps)
```
Time:  0.0   0.25   0.5   0.75   1.0
       ----  -----  ----  -----  ----

Hips Y:     0     -0.02   0    -0.02    0    (vertical bob)

L Hip X:   30      0     -30     0      30   (swing forward/back)
R Hip X:  -30      0      30     0     -30   (opposite phase)

L Knee X:   0     40       0    10       0   (bend on back swing)
R Knee X:   0     10       0    40       0

L Shoulder X: -20   0     20     0     -20   (opposite to legs)
R Shoulder X:  20   0    -20     0      20

L Elbow X:  -20  -40     -20   -30    -20   (slight bend)
R Elbow X:  -20  -30     -20   -40    -20

Spine X:     0    -3       0    -3       0   (slight forward lean)
Spine Z:    -2     0       2     0      -2   (hip sway)
```

### Idle (2.0s loop)
```
Hips Y:    sin(t * 2.5) * 0.005     (subtle breathing bob)
Spine X:   sin(t * 1.5) * 2         (breathing lean)
Neck Y:    random target every 2-4s  (head look)
Shoulders: sin(t * 1.0) * 3         (subtle arm sway)
```

### Attack / Punch (0.4s one-shot)
```
Time:  0.0    0.1    0.2    0.3    0.4
R Shoulder X:  0    -60    -90     40      0   (wind up, thrust, recoil)
R Elbow X:     0    -30     -5    -60      0   (extend at impact)
Spine X:       0      5     -8      3      0   (lean into punch)
Hips Y:        0      0    0.02     0      0   (slight lift)
```

### Death (0.3s one-shot → ragdoll)
```
Time:  0.0    0.1    0.2    0.3
Spine X:   0     20     30      → ragdoll
Hips Y:    0   -0.05  -0.10     → ragdoll
Neck X:   0     -30    -50      → ragdoll
Arms:   rest   flail   flail    → ragdoll
```
After 0.3s, convert to ragdoll physics (same system as current CommanderRagdoll).

### Celebrate (1.5s loop)
```
Both arms: alternate pumping up (Shoulder Z: -90 → 0, repeat)
Hips: bouncing (Y: sin(t*8) * 0.03)
Head: looking up (Neck X: -20)
```

### Flinch (0.25s one-shot)
```
Spine X:    sharp +15 then decay
Neck X:     sharp -20 then decay
Shoulders:  retract inward (Z: +15)
Hips Y:     -0.01 then return
```

## Commander vs Troop Differences

| Property | Commander | Infantry Troop | Demolisher | Scout |
|----------|-----------|---------------|------------|-------|
| Grid | 8x14x6 | 6x10x4 | 7x11x5 | 5x9x4 |
| Voxel Size | 0.08m | 0.06m | 0.07m | 0.05m |
| Total Height | 1.12m | 0.60m | 0.77m | 0.45m |
| Head:Body Ratio | 40% | 35% | 30% | 40% |
| Accessories | Helmet+badge+epaulettes | Helmet+rifle slung | Hard hat+drill | Goggles+backpack |
| Speed | Stationary | 2.5 m/s | 2.0 m/s | 4.0 m/s |
| Walk Cycle | N/A (stationary) | Standard 1.0s | Heavy 1.2s | Quick 0.6s |

## Implementation Plan

1. **VoxelCharacterBuilder.cs** - New file that constructs the skeleton hierarchy from a body definition
2. **VoxelAnimator.cs** - Applies animation clips (keyframe interpolation) to the skeleton
3. **CharacterDefinition** - Data class with voxel grids per body part + joint positions
4. **AnimationClip** - Data class with keyframes per joint per animation

The system should be testable via a standalone viewer scene before integrating into gameplay.
