# Army & Troops System - Design Document

## Overview

A deployable army system where players purchase and command small voxel troops. Troops navigate the battlefield, enter/exit buildings through doors, and attempt to breach enemy fortresses through structural damage holes.

## Core Concept

- Troops are small animated voxel soldiers (2-3 voxels tall)
- Purchased during the **build phase** using budget (like weapons/powerups)
- Deployed as a combat action during the **combat phase**
- Troops are autonomous once deployed — pathfind to objective, attack, or retreat

## Troop Lifecycle

### 1. Purchase (Build Phase)
- Player buys troops from the Build UI (new "Army" section)
- Troops appear inside the player's fortress, idle until deployed
- Each troop costs budget (e.g., 50-150 gold per soldier depending on type)
- Can be removed/refunded before combat starts (same as powerups)

### 2. Deployment (Combat Phase)
- Player selects "Deploy Troops" as a combat action (uses their turn)
- Choose target: an enemy player's fortress
- All purchased troops march out of the player's fortress through doors

### 3. Movement & Pathfinding
- Troops walk across the open battlefield toward the target fortress
- Use A* or navmesh pathfinding on the voxel world surface
- Walk speed ~2-3 m/s (takes several seconds to cross the arena)
- Troops avoid falling into holes, walk around obstacles

### 4. Entry Rules

**Own Base - Doors:**
- During build phase, player places "Door" blocks (new build tool, 1x3 opening)
- Troops can only exit/enter their own base through doors
- If all doors are destroyed, troops inside are trapped

**Enemy Base - Holes Only:**
- Troops CANNOT enter enemy bases through doors or intact walls
- Troops can ONLY enter through holes created by weapon damage
- If no holes exist in the enemy fortress, troops cannot breach
- Troops will attempt to find any opening (hole >= 1x2 voxels)

### 5. Retreat Behavior
- If troops cannot find a breach point after reaching the enemy fortress, they retreat
- Retreat = walk back to their original position inside their own base
- Retreated troops can be re-deployed on a future turn
- Troops inside an enemy base flee back if the hole they entered through gets repaired (RepairKit)

## Troop Types (Future Expansion)

| Type | Cost | HP | Speed | Special |
|------|------|----|-------|---------|
| Infantry | 50 | 3 | 2.5 m/s | Basic soldier, 1 damage to commander per turn in range |
| Demolisher | 100 | 5 | 2.0 m/s | Can damage walls (1 HP/turn to adjacent voxels), slower |
| Scout | 75 | 2 | 4.0 m/s | Reveals fog in radius, fast but fragile |
| Medic | 100 | 3 | 2.5 m/s | Heals nearby troops 1 HP/turn |
| Shield Bearer | 150 | 8 | 1.5 m/s | Absorbs damage for nearby troops, tanky |

## Combat Interactions

### Troops vs Commander
- If troops reach the enemy commander, they deal damage each turn
- Infantry: 1 damage/turn to commander
- This creates a secondary win condition beyond just weapons

### Troops vs Troops
- Enemy troops can fight each other in the open field
- Simple melee: 1 damage per turn to nearest enemy troop
- Troops prioritize reaching the enemy base over fighting

### Troops vs Weapons/Explosions
- Troops caught in weapon blast radius take damage and can die
- Creates interesting tactical tension: bombing near your own troops

### Troops vs Doors
- Demolishers can break down doors (both friendly and enemy)
- Adds door-placement strategy to the build phase

## Animations

Each troop needs these animation states:
- **Idle** — standing in base, shifting weight
- **Walk** — marching across terrain
- **Attack** — punching/hitting commander or enemy troop
- **Death** — ragdoll/fall apart into voxel debris
- **Celebrate** — victory pose if commander killed

Animations built procedurally with VoxelModelBuilder:
- Simple limb articulation (arms/legs swing during walk cycle)
- Voxel-style: chunky, toy-like, matches game aesthetic
- Color-coded to team color (helmet/uniform matches player color)

## Door System

### Build Phase
- New build tool: "Door" — places a 1-wide, 3-tall opening in a wall
- Doors are visually distinct (different color trim or archway)
- Doors count as solid for enemy weapon damage calculations
- Doors are transparent to own troops only

### Rules
- Must be placed on the edge of a wall (auto-validates)
- Each player should have at least 1 door (warn if 0 doors when readying up)
- Doors can be destroyed by weapons like any other block
- Destroyed door = hole = enemies can enter too

## Pathfinding Implementation

### Approach: Surface Voxel A*
- Build a walkable surface graph from the voxel world
- Each voxel surface tile is a node
- Edges connect adjacent walkable tiles (flat or 1-step height difference)
- Recompute paths when terrain changes (explosions)

### Key Considerations
- Troops walk on TOP of voxels (y + 1 surface)
- Can step up 1 voxel height, fall down any height (take fall damage if > 3)
- Cannot walk through solid voxels
- Hole detection: check for opening >= 1 wide, >= 2 tall in enemy walls
- Path invalidation: when terrain changes, re-pathfind affected troops

## File Organization

```
src/Army/
  TroopBase.cs          - Base troop class (Node3D + animations)
  TroopTypes.cs         - Enum + definitions (cost, HP, speed, abilities)
  TroopController.cs    - Per-troop AI (pathfinding, state machine)
  ArmyManager.cs        - Per-player troop management (purchase, deploy, track)
  TroopPathfinder.cs    - A* pathfinding on voxel surface
  DoorSystem.cs         - Door placement validation and tracking

src/Art/
  TroopModelGenerator.cs - Procedural voxel troop models with team colors

src/Building/
  DoorTool.cs           - Build tool for placing doors (extends build system)
```

## UI Integration

### Build Phase
- New "ARMY" section in BuildUI left panel (below powerups)
- Shows troop types with costs, buy/sell like powerups
- "Door" tool added to build tool modes
- Warning if 0 doors placed when clicking Ready

### Combat Phase
- "DEPLOY" button in CombatUI (like a weapon selection)
- Shows troop count and types owned
- Click to deploy → troops march out
- Mini health bars above active troops in the world

## Implementation Priority

1. Door system (build tool + validation)
2. Basic Infantry troop model generation
3. TroopBase + idle/walk animations
4. Surface pathfinding (A* on voxel terrain)
5. Deploy mechanic (troops walk to enemy base)
6. Hole detection + breach entry
7. Retreat behavior
8. Troop vs commander damage
9. Troop vs troop combat
10. Additional troop types
11. UI polish (deployment panel, troop health bars)

## Open Questions

- Should troops persist between rounds or reset each round?
- Can troops be deployed multiple times per game or once?
- Should there be a max troop count per player? (Suggest: 20)
- Do troops block weapon fire? (Probably not — too complex)
- Can troops be targeted directly by weapons? (Yes — blast radius hits them)
