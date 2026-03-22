English | [简体中文](README.md)

# STS2Fatigue - Fatigue Mechanic Mod

A Mod for **Slay the Spire 2** that adds a "Fatigue" mechanic similar to Hearthstone.

## Overview

When the player's draw pile is empty, the discard pile is no longer automatically shuffled. Instead, fatigue damage is dealt.

### Fatigue Rules

1. **Increasing Fatigue Damage**: The nth fatigue deals n damage
2. **Damage Can Be Blocked**: Fatigue damage is normal damage, block can mitigate it
3. **Fatigue Counter Persists Across Turns**: Accumulates throughout the entire battle (within a monster room), resets when the battle ends (after leaving the monster room)
4. **End-of-Turn Shuffle**: At the end of a turn, if the draw pile is empty, the discard pile is shuffled back into the draw pile

---

## Installation (Steam)

1. Right-click the game in your Steam library and select "Browse Local Files". If the `mods` folder doesn't exist, create a new folder and name it `mods`. If it exists, enter the `mods` folder
2. Create a new folder inside the `mods` directory and name it `STS2Fatigue`
3. Download `STS2Fatigue.dll` and `STS2Fatigue.json`
4. Place `STS2Fatigue.dll` and `STS2Fatigue.json` into the `STS2Fatigue` folder
5. Launch the game

---

## Detailed Behavior

### 1. Turn Start Draw

**Rule**: When drawing cards at the start of a turn, if the draw pile has insufficient cards, only draw what's available. **No shuffle, no damage**.

**Example 1**:
- Draw pile: 3 cards
- Discard pile: 10 cards
- Turn start draw: 5 cards
- Result: Draw 3 cards, stop drawing, no shuffle, no damage

---

### 2. Card Effect Draw

**Rule**: When drawing cards via a card effect, if the draw pile is empty, each draw attempt deals fatigue damage. **No shuffle, no cards drawn**.

**Example 1**:
- Draw pile: 0 cards
- Discard pile: 10 cards
- Player uses a "Draw 2 cards" effect
- Result:
  - 1st attempt: Fatigue damage **1**
  - 2nd attempt: Fatigue damage **2**
  - Total: **3 damage**, **0 cards drawn**

**Example 2**:
- Draw pile: 0 cards
- Discard pile: 10 cards
- Player uses a "Draw 5 cards" effect
- Result:
  - 1st attempt: Fatigue damage **1**
  - 2nd attempt: Fatigue damage **2**
  - 3rd attempt: Fatigue damage **3**
  - 4th attempt: Fatigue damage **4**
  - 5th attempt: Fatigue damage **5**
  - Total: **15 damage**, **0 cards drawn**

---

### 3. Fatigue Damage and Block

**Rule**: Fatigue damage is normal damage and can be blocked.

**Example**:
- Player has 6 block
- Draw pile: 0 cards
- Uses a "Draw 3 cards" effect
- Result:
  - 1st fatigue: 1 damage → Consumes 1 block, 0 HP damage
  - 2nd fatigue: 2 damage → Consumes 2 block, 0 HP damage
  - 3rd fatigue: 3 damage → Consumes remaining 3 block, 0 HP damage
  - Total: **6 block consumed**, **0 HP damage**

---

### 4. Both Piles Empty (Special Case)

**Rule**: If both draw pile and discard pile are empty, stop drawing. **No fatigue damage**.

**Example**:
- Draw pile: 0 cards
- Discard pile: 0 cards
- Uses a "Draw 3 cards" effect
- Result: 0 cards drawn, **0 damage**

---

### 5. End-of-Turn Shuffle

**Rule**: At the end of a turn, check each player's piles. If the draw pile is empty and the discard pile has cards, shuffle automatically.

**Example**:
- Player used many draw cards during the turn
- End of turn: Draw pile 0 cards, Discard pile 15 cards
- Result: Auto shuffle, can draw normally at the start of next turn

---

### 6. Fatigue Counter Persists Across Turns

**Rule**: Fatigue counter accumulates throughout the battle, not reset at end of turn.

**Example**:
- Turn 1: Use "Draw 2 cards", draw pile empty → Fatigue 1 + 2 = 3 damage
- Turn 2: Use "Draw 1 card", draw pile empty → Fatigue 3 damage (3rd fatigue)
- Turn 3: Battle ends, fatigue counter resets

---

## Multiplayer Support

- Fatigue counters are calculated independently for each player

---

## Debug Mode

For debugging, change `ENABLE_LOGGING` to `true` in the source code and recompile:

```csharp
public const bool ENABLE_LOGGING = true;
```

The log file will be generated as `STS2Fatigue.log` in the game directory.

---

## Compatibility

- Game Version: Slay the Spire 2 (v0.99.1)
- Framework: HarmonyLib

---

## Development Info

- Language: C# .NET 9.0
- Game Engine: Godot 4.5.1 (.net version)
- Mod Framework: HarmonyLib