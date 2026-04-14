# MINEUP - AGENTS.md

## Project goal
This is a small stylized first-person mining game prototype in Unity.
The player starts at the bottom of a cave, mines upward through destructible material, hoovers up the broken chunks as scrap, buys upgrades, reaches the top to get a powerful upgrade/key, then returns downward to escape.

## High-level priorities
1. Keep scope small and finishable.
2. Prefer simple, reliable implementations over clever ones.
3. Do not rewrite working destruction systems unless absolutely necessary.
4. Preserve the satisfying feel of mining and collection.
5. Minimize performance risk from debris and physics.

## Rules
- Make focused changes only.
- Do not redesign the whole project.
- Do not add enemies, crafting trees, procedural generation, or extra systems unless explicitly requested.
- Keep the prototype readable and maintainable.
- Prefer short scripts with clear names.
- Add comments only where they help understanding.
- If changing scene hookups, explain exactly what needs to be assigned in the Inspector.
- If you are unsure about a Unity object reference, create serialized fields rather than hardcoding object lookups.
- Do not remove existing functionality unless required for the requested task.

## Current intended loop
Mine -> chunks spawn -> hoover chunks -> convert to scrap -> upgrade tools -> climb higher.

## Technical preferences
- Unity project
- Use existing BoxCutter demo/destruction setup as the foundation
- New gameplay code should live in Assets/Scripts
- Prefer composition over large manager scripts
- Keep runtime allocations low where practical
- Debris should be temporary and cleaned up quickly

## First prototype goals
- Add hoover collection for destructed chunks
- Convert collected chunks into scrap currency
- Add simple on-screen scrap counter
- Add one basic upgrade path later

## Delivery expectations
For each task:
1. Explain what files you changed
2. Explain what objects/components must be assigned in Unity
3. Explain how to test the feature in play mode
4. Keep the change set small and scoped

## Review guidelines
- Avoid breaking the demo scene startup
- Avoid changes that require large scene rebuilds
- Avoid unnecessary package additions
- Prefer solutions that a solo beginner can maintain
