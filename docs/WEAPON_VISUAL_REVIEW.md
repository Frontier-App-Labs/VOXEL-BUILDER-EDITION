# Weapon Visual Differentiation Review

## Current State

All 5 weapons are defined in `src/Art/WeaponModelGenerator.cs` at 0.15m voxels.
They already have distinct designs but may look similar in-game because:
1. All share the same team-colored rectangular base platform
2. Most are similar heights (6-8 voxels tall)
3. At game camera distance, color differences are hard to distinguish
4. No emissive/glow effects to make energy weapons pop

## Weapon Comparison

| Weapon | Grid | Silhouette | Key Visual | Problem |
|--------|------|------------|------------|---------|
| Cannon | 8x6x10 | Low, wide, long barrel | Grey barrel on platform | Generic artillery, barrel is just a rectangle |
| Mortar | 6x8x6 | Tall, narrow | Stepped tube, bipod legs | Good shape but steps look blocky |
| Railgun | 8x5x12 | Long, flat, twin rails | Cyan energy channel | Best design - glowing channel is distinctive |
| Missile | 8x7x8 | Boxy, tall | 4 tube openings | Box shape blends with fortress walls |
| Drill | 6x7x10 | Long with bulky rear | Orange motor housing | Orange makes it stand out well |

## Recommendations for Visual Variety

### 1. CANNON - Make it Clearly a Cannon
- **Add wheels** (2-3 voxels) on each side for classic artillery look
- **Round the barrel** more - use stepped cylinder approximation (current is flat)
- **Add a fuse/ignition detail** at the rear (bright orange/red glow voxel)
- **Tilt the barrel up** slightly by offsetting front voxels higher
- Base color: Keep dark iron/grey but add weathered spots (lighter grey patches)

### 2. MORTAR - Emphasize the Angle
- **Make the tube more clearly angled** - remove stepping, use diagonal voxels
- **Add ammo shells** next to the base (small colored cylinders)
- **Wider base plate** with sandbag-like voxels around it (tan/brown bumps)
- **Sight scope** on the side should be more prominent (bright lens voxel)
- Base color: Keep olive but add camo pattern (alternating dark/light greens)

### 3. RAILGUN - Already Good, Enhance the Glow
- **Add pulsing emissive** to the energy channel (shader or animated color)
- **Extend the rails further** for a sleeker profile
- **Add capacitor coils** on top (small rings of dark metal)
- **Sparks at the muzzle** end (bright white voxels)
- Consider making this one slightly larger to show its power tier

### 4. MISSILE LAUNCHER - Break the Box
- **Angled launch tubes** - tilt the whole launcher back 15 degrees
- **Add exhaust vents** on the back (dark slots)
- **Visible missile tips** in the tubes (red/white nosecones poking out)
- **Control panel** on one side (small screen with green dot)
- **Camo netting** effect on top (irregular dark green voxels)
- Make it wider than tall to differentiate from fortress walls

### 5. DRILL - Great Already, Minor Polish
- **Animated drill bit** (rotate the mesh slightly each frame)
- **Sparks at the tip** when idle (particle effect)
- **Warning stripes** more prominent (black/yellow chevrons)
- **Exhaust pipe** on top blowing dark particles
- The orange color already makes it pop — maintain this advantage

## Size Differentiation Strategy

Scale weapons differently to show power tiers:
- **Drill** (Tier 1, cheapest): 0.12m voxels (smaller, less threatening)
- **Cannon** (Tier 1): 0.14m voxels (medium)
- **Mortar** (Tier 2): 0.15m voxels (standard)
- **Missile** (Tier 3): 0.17m voxels (bigger = scarier)
- **Railgun** (Tier 3): 0.18m voxels (largest, most imposing)

## Color Strategy - Make Each Weapon Instantly Identifiable

Each weapon should have a **signature accent color** visible from any angle:
- Cannon: **Brass/gold** accents (already has this, enhance)
- Mortar: **Olive/camo green** with bright sight lens
- Railgun: **Cyan glow** (already has this, make brighter)
- Missile: **Red/yellow warning** markings (enhance existing)
- Drill: **Safety orange** (already has this)

All weapons keep the team-colored base platform for player identification.
