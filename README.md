# D2ItemToolkit

[![NuGet](https://img.shields.io/nuget/v/D2ItemToolkit.svg)](https://www.nuget.org/packages/D2ItemToolkit)
[![npm](https://img.shields.io/npm/v/d2itemtoolkit.svg)](https://www.npmjs.com/package/d2itemtoolkit)

Diablo II 1.14d's item description engine, reimplemented from the disassembly. Hand it a unit and
it renders the tooltip the game would draw for it — the same text, the same order, the same
colours.

Available for **C#** (`D2ItemToolkit`) and **TypeScript** (`d2itemtoolkit`). The game's tables are
embedded in both packages, so there is nothing to download or point at.

```
Vigorous Large Shield of Absorption
Defense: 300
Chance to Block: 30%
Smite Damage: 2 to 4
Durability: 40 of 62
Required Strength: 34
Required Level: 24
+150% Enhanced Defense
Fire Resist +25%
30% Better Chance of Getting Magic Items
```

That is the [quick start](#quick-start) below, with the game's colour markers stripped for
readability — see [Render](#render) for what the raw string contains.

Useful for trade sites, loot filters, stash viewers, bots, drop notifiers — anywhere you have item
data and need to show it the way a player expects to see it.

> **Pre-1.0.** The public surface may still move between minor versions.

> **Written by Claude, without human supervision.** Every line of both implementations, the tests,
> and the docs were produced by an AI agent working from the 1.14d disassembly. Nobody has read it
> line by line. What holds it together is mechanical rather than editorial: a differential test that
> requires the two implementations to agree byte for byte across ~940 generated cases and ~12,000
> hostile ones, plus captured game output. So treat the *behaviour* as tested and the *source* as
> unreviewed — it is dense, it comments oddities at the address that causes them, and it will not
> read like code a human wrote for other humans.

## Install

```bash
dotnet add package D2ItemToolkit     # netstandard2.0
npm install d2itemtoolkit            # ESM only, Node 18+ and browsers
```

> The MIT licence covers the source. Both packages also embed Diablo II's shipped data tables,
> which are Blizzard's property and are not covered by it — see [Licence](#licence).

## Quick start

Two inputs: the **item**, and optionally the **viewer** — the player looking at it. The viewer
decides requirement colours, class gates, attack speed, block chance, smite damage and every
level-scaled line. Pass `null` and you get the item rendered with no player context, which is a
legal call that produces fewer lines.

Both are unit documents of the same shape, because an item and a player are the same struct in the
game. A socket filler is another one nested inside — and so is a player's carried gear.

### C#

```csharp
using D2ItemToolkit;

Unit item = Unit.FromJson(itemJson);
Unit player = Unit.FromJson(playerJson);

Tooltip tip = TooltipEngine.Embedded.Render(item, player);

Console.WriteLine(tip.Text);
```

### TypeScript

```ts
import { TooltipEngine, unitFromJson } from 'd2itemtoolkit';

const item = unitFromJson(itemJson);
const player = unitFromJson(playerJson);

const tip = TooltipEngine.embedded.render(item, player);

console.log(tip.text);
```

Same call chain, camelCased. `TooltipEngine.Embedded` / `.embedded` parses the tables once and
caches them — hold onto it rather than building an engine per item. Rendering is read-only and safe
to share between threads.

The tables are inflated **synchronously**, which is what keeps the whole API non-async.

## Building a unit in code

You do not need JSON. Everything required to author a record is public.

```csharp
var item = new Unit();
item.UnitType = 4;
item.ClassId = 330;                              // Large Shield
item.Quality = ItemQualityNo.Magic;
item.ItemFlags = ItemRecordFlags.Identified;

// 1-based indices into the CONCATENATED [magicsuffix][magicprefix][automagic] array, so 962 is
// past the 747 suffix rows and lands in the prefix table.
item.MagicPrefix[0] = 962;
item.MagicSuffix[0] = 121;

item.StatsLists.Add(
    new UnitStatList(0, ItemStatListFlags.Extended)   // the base array
        .Add(31, 120).Add(72, 40).Add(73, 62));
item.StatsLists.Add(
    new UnitStatList(0, ItemStatListFlags.Magic)      // what the item itself grants
        .Add(16, 150).Add(39, 25).Add(80, 30));

Tooltip tip = TooltipEngine.Embedded.Render(item);
```

In TypeScript use `createUnit`, which defaults every field you do not set. There is no fluent
statlist builder — write them as object literals:

```ts
const item = createUnit({
  unitType: 4,
  classId: 330,
  quality: 4,
  itemFlags: 16,
  magicPrefix: [962, 0, 0],
  magicSuffix: [121, 0, 0],
  statsLists: [
    { stateNo: 0, flags: 0x80000000, stats: [{ id: 31, value: 120 }, { id: 72, value: 40 }] },
    { stateNo: 0, flags: 0x40, stats: [{ id: 16, value: 150 }, { id: 39, value: 25 }] },
  ],
});
```

**`IUnit` is the contract; `Unit` is one implementation of it.** Every C# entry point takes the
interface, so if you already hold unit state in your own shape — a live client, another DTO, a
database row — implement `IUnit` over it instead of copying. In TypeScript `Unit` is an interface,
but every one of its fields is required, so `createUnit` is the practical way in.

## What the engine offers

```
IUnit (item) ──────┐
                   ├── TooltipEngine ──┬── Render            → the tooltip
IUnit (viewer) ────┘                   ├── Breakdown         → where each modifier came from
    optional                           ├── Ranges            → what each stat could have rolled
                                       ├── RangesForViewer   → the same, plus the viewer's set tiers
                                       ├── MergedStats       → what the stats add up to
                                       ├── SocketFillerStats → what one gem or rune grants its host
                                       ├── Appearance        → inventory sprite and palette shift
                                       ├── Requirements      → strength / dexterity / level / class
                                       └── ClassIdsOfType    → every classId under a type code
```

It also exposes the parsed game tables — see [Reading the tables](#reading-the-tables). The section
writers and the description engine stay internal.

### Render

`Tooltip.Lines` comes back in display order, top row first.

| member | what it gives you |
|---|---|
| `tip.Kind` | which builder produced this — `Generic`, `Book` or `IdentifiedSetItem` |
| `tip.Lines` | one entry per row, each with `Text`, `Color` and originating `Section` |
| `tip.Text` | the rows joined with newlines |
| `tip.ColoredText` | the same, plus the game's per-line `U+00FF 'c' N` colour marker |

`ItemTooltipKind` also declares `ShopTransaction` and `Transmogrify`. Nothing currently produces
them — they belong with the transaction-cost gap in [what is not
implemented](#what-is-not-implemented-and-why).

**Neither string form is marker-free.** Some section writers embed a marker mid-line, and the game
embeds those too, so they survive in `Text`. The same shield as above:

```
Vigorous Large Shield of Absorption
Defense: ÿc3300
ÿc0Chance to Block: ÿc330%
Smite Damage: 2 to 4
...
```

Two markers on the block line is not a bug: `INV_FormatBlockChanceText` prepends colour 0 to its
own label buffer and `LoadItemDesc` prepends the section's on top. If you want plain text, strip
them — `Regex.Replace(tip.Text, "ÿc.", "")` in C#, `tip.text.replace(/ÿc./g, '')` in
TypeScript. Prefer `Lines` if you are rendering yourself: you get the colour per row without
parsing markers out of a string.

> **Do not join `line.Text` yourself.** Both string forms spend the game's 1023-character budget
> across the rows before joining, so a long tooltip truncates where the game truncates. (Set-item
> tooltips are exempt — that path has no limit.) Each `line.Text` also ends with its own `\n`.

In TypeScript `line.text` is typed `string | null`, so narrow it before use.

### Options

Every knob on `TooltipOptions`. The defaults reproduce the game exactly; the three marked **beyond
the game** deliberately do not, and none of them changes the output unless you set it.

| option | default | what it does |
|---|---|---|
| `Difficulty` | `0` | `GetDificulity()`. Only a quest item with `questdiffcheck` reads it |
| `ShopMode` | `0` | 0 outside a shop. Any non-zero value suppresses both usage lines; 1–9 also admit the transaction-cost line, which only the set-item path fills today (see the gap below) |
| `ClientPlayer` | `null` | The character, when the viewer is a **mercenary** — see below |
| `Sockets` | `Merged` | *`Excluded` and `Separated` go beyond the game.* What the render does with the socket fillers |
| `Ranges` | `null` | *Beyond the game.* Non-null writes each stat's roll span inline. `Format` chooses the wording, `Color` the colour (grey by default, so a span reads as an annotation rather than as part of the line; -1 inherits the line's) |
| `ShowItemLevel` | `false` | *Beyond the game.* Appends ` [ilvl 67]` after the item's name, in the same grey a span uses. Silently absent when the record carries no level (-1) |
| `ApplyWornSetDiscard` | `true` | *False goes beyond the game.* Renders a worn set piece as though its fillers still applied — see below |

`Sockets` is one value rather than several booleans because the alternatives are mutually
exclusive: `Merged` is what the game draws, `Excluded` renders the item as if nothing were socketed
in it, and `Separated` moves each filler's mods into its own block below the item.

```csharp
var options = new TooltipOptions();
options.ShopMode = 1;                              // 1-9 mark a shop context (see the gap below)
options.Sockets = SocketMode.Separated;            // one block per gem, below the item
options.Ranges = new RangeDisplay();               // "+175% Enhanced Damage [150-200]"
options.Ranges.Color = ItemTooltipColor.White;     // override the grey default
options.ClientPlayer = character;                  // see below
```

`ClientPlayer` exists for exactly one case: a **mercenary's** panel. Requirements, class
restriction, block chance and the smite gate all use the viewer — who is the merc — but the attack
speed line is timed against the *character*. If you are rendering a merc's equipment, set this to
the player. Leave it null everywhere else.

The TypeScript names are the same in camelCase, and it is a plain object literal, so you only pass
what you set: `{ sockets: 'separated', ranges: { color: 0 } }`.

### Separating the sockets

The game merges a filler's mods into the item's own block, so you cannot tell which gem did what.
`Sockets = Separated` moves them out — the item shows only its own, and each filler gets a
block below headed by its name:

```
merged (what the game draws)          Sockets = Separated
────────────────────────────          ───────────────────────────
Gemmed Crystal Sword                  Crystal Sword
One-Hand Damage: 6 to 18              One-Hand Damage: 5 to 15
Durability: 20 of 20                  Durability: 20 of 20
+20% Enhanced Damage                  Socketed (3)
+40 to Attack Rating
Adds 20-50 fire damage                Ral Rune
Socketed (3)                          Adds 5-30 fire damage

                                      Perfect Ruby
                                      Adds 15-20 fire damage

                                      Jagged Jewel
                                      +20% Enhanced Damage [10-20]
                                      +40 to Attack Rating
```

The blocks carry `ItemTooltipSection.SocketContribution` and are separated by a blank row, so three
gems do not read as one list. Nothing is dropped — the fillers are moved, which is why the item's
own damage line drops back to its unsocketed value.

Combined with a non-null `Ranges`, a **jewel** is ranged from its own affixes, which is the case that
actually rolls. A gem or rune shows no span, and that is correct rather than missing: no gems.txt
cell rolls at all. The three whose min differs from their max — `dmg-fire`, `dmg-ltng` and
`dmg-cold` on Ral, Ort and Thul — are the two fixed *ends* of a damage range, not a roll.

### Breakdown — where each modifier came from

The game draws one blue block. `Breakdown` splits it by source, which is what a "hold shift" view
wants:

```csharp
TooltipBreakdown b = TooltipEngine.Embedded.Breakdown(item, player);
```

For the shield above with a **Perfect Ruby** socketed:

| | |
|---|---|
| `b.Base` | `+120 Defense` |
| `b.Magic` | `+150% Enhanced Defense`, `Fire Resist +25%`, `30% Better Chance of Getting Magic Items` |
| `b.Sockets` | `Fire Resist +40%` |
| `b.SetBonuses` | *(none)* |

`Render` merges those into one `Fire Resist +65%`, because that is what the game does.
`Sockets = SocketMode.Excluded` gives `Fire Resist +25%` instead.

`Breakdown` takes the same `TooltipOptions`, so a non-null `Ranges` annotates each bucket with the
span that matches ITS numbers — the item's own for `Base`, `Magic` and `SetBonuses`, the fillers'
for `Sockets`. See [Ranges](#ranges--what-each-stat-could-have-rolled).

**Caveat:** the game never draws these separately, so unlike `Render` this cannot be checked
against the original — it is the one part of the API with no ground truth. Every line is still
produced by the same writers; only the stat selection differs.

### Ranges — what each stat could have rolled

To show `+120 Defense (98–141)` you need the span the roll came from. `Ranges` rebuilds the
properties the item's own sources would have rolled and reports each stat's low and high:

```csharp
ItemRollRanges ranges = TooltipEngine.Embedded.Ranges(item);

foreach (RolledStatRange r in ranges.Stats)
{
    if (r.IsRange)
    {
        Console.WriteLine($"stat {r.StatId}: {r.Low}..{r.High} from {r.Sources}");
    }
}
```

Every rendered line also tells you which stat it came from — `line.StatId` and `line.Layer`, or -1
for a line that shows no stat — so you can pair the two yourself and decide whether a range becomes
`(98–141)`, a bar, or a colour.

Some lines speak for more than one stat: `Adds 1-4 fire damage` is `firemindam` and `firemaxdam`
together, `+2 to All Attributes` stands for four. Those carry `line.Aggregated` and
`line.ShownStats` — every stat the line shows a number for, in the order the numbers appear.
`ShownStats` is null when `StatId` is the whole story, so the two cases can be handled uniformly:

```csharp
int[] stats = line.ShownStats ?? new[] { line.StatId };
```

Range annotation is written into `line.Text` as each line is built, so the annotated output
survives rendering from `Lines` — you do not have to go through `Text` or `ColoredText` to get it.

**A span always matches the number beside it.** That is the rule the three views follow, and it is
why they disagree. Take a rare armour rolling Fire Resist 11–20, socketed with a jewel rolling
Fire Resist 5–10:

| view | line | span | why |
|---|---|---|---|
| `Render` (default) | `Fire Resist +28%` | `[16-30]` | one line holding the SUM, so the span sums both: 11+5 to 20+10 |
| `Render` + `Sockets = Separated` | `Fire Resist +20%` | `[11-20]` | the fillers are moved out, so the line and its span are the item's own |
| " " (the jewel's block) | `Fire Resist +8%` | `[5-10]` | the jewel's own affix roll |
| `Breakdown.Magic` | `Fire Resist +20%` | `[11-20]` | the item's own mods, sockets excluded |
| `Breakdown.Sockets` | `Fire Resist +8%` | `[5-10]` | what the fillers add |

Ranges work in **both** `Render` and `Breakdown` — pass the same `Ranges` either way.

If you just want it written inline, set the flag:

```csharp
var options = new TooltipOptions { Ranges = new RangeDisplay() };

foreach (ItemTooltipLine line in TooltipEngine.Embedded.Render(item, player, options).Lines)
{
    Console.WriteLine(line.Text);
}
```

```
The Eye of Etlich                       Superior Crystal Sword
Amulet                                  One-Hand Damage: 5 to 16
Required Level: 15                      Durability: 20 of 22
+1 to All Skills                        Required Strength: 43
Adds 1-4 cold damage                    +7% Enhanced Damage [0-15]
5% Life stolen per hit [3-7]            +2 to Attack Rating [1-3]
+25 Defense vs. Missile [10-40]         Increase Maximum Durability 12% [10-15]
+3 to Light Radius [1-5]
```

A line that prints **two** numbers gets two spans, positionally:

```
Adds 1-4 cold damage [(1-2)-(3-5)]      the first number rolled 1-2, the second 3-5
+175% Enhanced Damage [150-200]         one number, so one span
+2 to all Attributes                    four stats sharing one number, all fixed — nothing to say
```

`Ranges.Color` paints the spans distinctly. The annotation is wrapped in a colour marker and a second
one restoring the line's own, so nothing after it is repainted and the following line is unaffected.

The format is yours — `Ranges.Format` receives one `RolledStatRange` per STAT the line shows, in
print order, and returning null or an empty string suppresses the annotation, which is how you show
ranges for some stats and not others. Stats shown, not numbers printed: the `+2 to all Attributes`
line above prints one number and hands the callback four ranges, which is why the built-in format
has a rule for collapsing them.

Three things it deliberately leaves alone:

| left alone | why |
|---|---|
| `+2 to All Skills` when it could only be 2 | a degenerate range reads as a range |
| `Required Level: 15` | not a stat |
| a partly-resolved multi-number line | one span against two numbers belongs to neither |

**Packed values are decoded, not skipped.** A charged-skill stat stores
`(maxCharges << 8) + current`, so its raw span reads `[2306-2313]`; `DisplayLow`/`DisplayHigh` give
the charge count instead, and the line comes out `Level 13 Cloak of Shadows (5/9 Charges) [2-9]`.
`IsPackedEncoding` tells you when the raw ends are an encoding rather than a magnitude. The by-time
stats are packed too but never *roll* — the property's own min and max go in verbatim — so there is
nothing to show for them either way.

Sources are resolved from the record alone: the affix ids it stores, its UniqueItems or SetItems
row, its runeword name, its superior modifier, its socket fillers, and the base Defense roll. Set
bonuses are excluded by default because they belong to the worn set rather than to the item; pass
`earnedSetIds` to fold them in.

Three cases return something other than a plain span, and each says so rather than guessing:

| | |
|---|---|
| `LayerVaries` | the roll picked the *skill*, not the value — Ormus' Robes is always +3, to one of 25 sorceress skills |
| `CraftedRecipeUnknown` | a crafted item's record does not name the cube recipe that made it, and this one could not be worked out, so the recipe's fixed mods stay unattributed |
| `ItemLevelDependent` | a few properties derive their value from the item's level. Supply `itemLevel` on the record and they become exact |

`OutOfRange` lists stats whose recorded value falls outside the span computed for them. For a record
the game produced it is empty; if it is not, the reconstruction is wrong and says so.

#### Crafted items

A crafted item's record does not say which cube recipe made it, but usually that can be worked out.
The 36 crafted recipes are four families over nine equipment slots, one per pair, so the item's slot
leaves four candidates, and the one whose every fixed mod the record actually carries is the answer.
`CraftedRecipe` gives the resulting `cubemain.txt` row, and the recipe's mods then carry real spans
instead of sitting in `Unattributed`:

```csharp
TooltipEngine engine = TooltipEngine.Embedded;
ItemRollRanges ranges = engine.Ranges(craftedCrown);

if (ranges.CraftedRecipe >= 0)
{
    // e.g. "magic crown + jewel + rune 06 + perfect emerald -> safety helm"
    Console.WriteLine(engine.Data.CubeMain.GetString(ranges.CraftedRecipe, "description"));
}
```

It declines rather than guesses. Two families can both fit when the item's own affixes happen to
supply the other's stats; a base under none of the nine crafted slots — a charm or a quiver, say —
reaches no recipe at all; and a class-specific shield (a paladin auric shield, a necromancer voodoo
head) resolves to no slot either, because those sit beside the ordinary shield type rather than
under it. Each leaves `CraftedRecipe` at `-1` and
`CraftedRecipeUnknown` true. On the shipped tables it can fail to name a recipe, but it does not
name the wrong one.

**Caveat:** as with `Breakdown`, the game never computes this, so it has no ground truth to be
checked against. What it is checked against is the tables' own min/max columns, the item's own
recorded values, and the other implementation over every corpus case.

### MergedStats — what the stats add up to

`Render` answers "what does the game draw". `MergedStats` answers "what does this item give", which
is the question a stored item has to answer — and the raw statlists cannot, for three reasons:

- **A gem or rune carries no stats at all.** Its mods live in gems.txt keyed by the host's type, so
  an Um in a helm contributes `All Resistances +15` that appears nowhere in the record.
- **An item's own stats are split across its lists.** A Tal Rasha's Horadric Crest holds `31 = 76`
  on the base array and `31 = 45` on the affix list. The tooltip prints `Defense: 121`; nothing in
  the chain holds 121.
- **Op 13 is unapplied.** `+120% Enhanced Defense` and the base Defense are separate stats until the
  op resolves them.

```csharp
ItemMergedStats merged = TooltipEngine.Embedded.MergedStats(item);

foreach (MergedStat stat in merged.Stats)
{
    Console.WriteLine($"stat {stat.StatId} layer {stat.Layer} = {stat.Value}");
}
```

| member | what it gives you |
|---|---|
| `Stats` | one entry per non-zero `(StatId, Layer)`, ordered by LAYER then stat |
| `FillersIgnoredBecauseWorn` | true when these totals deliberately differ from `Render` — see below |
| `ExcludedPackedStats` | stat ids left out because their value is a packed encoding |

Values are **raw**, in the encoding the record carries: `+60 to Life` comes back as `60 << 8`,
pre-`nValShift`. That is deliberate — a consumer's search bounds derive from the same itemstatcost
scale, and a display-scaled value would need a second scale beside it.

An op-13 **percent survives** beside the target it resolved onto: you get both `item_armor_percent`
and the Defense it contributed to, because the tooltip draws the percent as its own line. Summing
both would double count.

**Packed encodings are excluded rather than summed** — stat 204 packs `(maxCharges << 8) + current`,
and the by-time stats pack a triple. Adding two packed words produces a number that looks real and
is not. They are reported in `ExcludedPackedStats` and are *absent* from `Stats` rather than zero,
because a zero would satisfy every "at most N" bound. `RolledStatRange.IsPackedStat(statId)` is the
same test, so a caller need not derive its own.

Set **bonuses** are excluded by default, the same rule `Ranges` follows; `IncludeSetBonuses` folds
in the tiers the record already carries. Pass an **item**, not a wearer: `IUnit.Items` carries two
relations, and this reads it as socket fillers.

#### Where it deliberately disagrees with Render

An equipped **set** piece is the one case. `ITEM_RecalcAllEquippedItems` detaches a worn set item's
stat list and rebuilds it without re-applying the fillers, so the game really does grant a worn Tal
Rasha's Horadric Crest with an Um in it `All Resistances +15` rather than 30 — and `Render`
reproduces that.

`MergedStats` ignores the discard, because the useful question about a stored item is what it
*would* give: an item must not drop out of a search because something equipped it. When the two
views disagree, `FillersIgnoredBecauseWorn` says so, so a caller can flag it rather than look wrong.
If you want the render to agree, set `ApplyWornSetDiscard = false`.

### SocketFillerStats — one filler's contribution

What a single gem, rune or jewel grants the host it sits in, so a caller can attribute stats to the
socket rather than only to the total.

```csharp
IReadOnlyList<MergedStat> um = TooltipEngine.Embedded.SocketFillerStats(filler, host);
```

The host matters: an Um is `All Resistances +22` in a shield and `+15` in a helm, and the difference
is gems.txt `gemapplytype`. A **jewel** carries its own affixes instead, and those are what come
back. Note the slot comes from `gemapplytype`, and a row that takes no sockets still reads 0 there,
so a non-empty result is not evidence that the host is socketable — ask `Items` for `gemsockets` if
that is the question.

### Appearance — drawing the item in a grid

```csharp
ItemAppearance look = TooltipEngine.Embedded.Appearance(item);

string image = look.Image;   // "rin3"  — sprite name, fetched as image + ".dc6"
int color = look.Color;      // 0-20 palette shift, -1 for none
int invTrans = look.InvTrans;// which transform table the shift indexes; 0 means no tint
bool tinted = look.IsTinted; // Color >= 0 && InvTrans != 0
```

`Image` is **not** the item code, so don't shortcut it. Exceptional and elite tiers share the base
tier's art (`xap`, the exceptional Cap, resolves to `cap`), set and unique items get their own, and
the four types with a random inventory graphic append the rolled variant — a ring is
`rin1`..`rin5`, which is the only thing `gfxIndex` in the record is for.

### Requirements

```csharp
ItemRequirements req = TooltipEngine.Embedded.Requirements(item, player);

int strength = req.Strength;
int level = req.Level;
int classRestriction = req.ClassRestriction;   // 0-6, or 7 for unrestricted
bool metStrength = req.MetStrength;
bool metLevel = req.MetLevel;
```

The numbers do not depend on the viewer, but the flags do. **With no viewer `MetStrength` and
`MetDexterity` read false** — a null unit's stats read as 0 and the test is `available > 0 &&
available >= required`, so even a zero requirement fails. `MetLevel` has no `> 0` guard, so it reads
TRUE whenever the required level is 0, which is the ordinary case; `MetClass` is true unless the
item is class-restricted. Pass a viewer if you care about the flags.

(That is only the API. The rendered tooltip does not paint anything red without a viewer.)

### Finding items by type

```csharp
IReadOnlyList<int> swords = TooltipEngine.Embedded.ClassIdsOfType("swor");
```

Every classId whose type chains up to `swor`, including exceptional and elite tiers and the
class-specific sword types.

### Set items

An identified set item uses a different builder, and `Render` routes to it automatically. Which
sibling pieces count — and which of those raise a bonus tier — is worked out from the viewer, so
there is nothing to assemble:

```csharp
Tooltip tip = TooltipEngine.Embedded.Render(item, player);
```

All it needs is that the player's `Items` carry their `Location` and, when equipped, their `X`:

```csharp
var player = new Unit();
player.UnitType = 0;
player.Items.Add(halo);      // halo.Location = 1 (equipped), halo.X = 6  (ring slot)
player.Items.Add(mantle);    // mantle.Location = 1,          mantle.X = 3 (torso)
player.Items.Add(wings);     // wings.Location = 3 (inventory)
```

That gives the piece list its colours — carried pieces green, the rest red — selects the partial
tiers, and renders the full-set block. **A piece on the alternate weapon set is deliberately not the
same as a worn one:** it still colours green, because the game counts it as carried, but it lights
no bit and raises no tier. That distinction is exactly why this is derived rather than handed over
as a mask; body locations 11 and 12 are the swap pair, and counting them is the easiest way to show
one bonus tier too many.

`RangesForViewer` takes the same viewer and folds in whichever set tiers it has earned:

```csharp
ItemRollRanges ranges = TooltipEngine.Embedded.RangesForViewer(item, player);
```

For a "what if I equipped the last piece" preview, hand it a viewer carrying the piece you are
imagining. There is no separate hypothetical-state API — a copy of the player with one more item in
`Items` *is* the hypothesis, and it goes through exactly the same derivation as the real one.

### Reading the tables

The engine hands you the parsed game tables for lookups it does not do for you. Every one is walked
the same way — `RowCount` for the bound, `RowAt(index)` for the row:

```csharp
TooltipEngine engine = TooltipEngine.Embedded;

for (int classId = 0; classId < engine.Items.RowCount; ++classId)
{
    ItemRow item = engine.Items.RowAt(classId);
    Console.WriteLine($"{item.Code} tier={item.Tier} lvl={item.RequiredLevel}");
}
```

`RowAt` returns null past the end rather than throwing. The same shape holds for `engine.Types`,
`engine.Data.ItemStatCost`, `engine.Data.Skills`, `engine.Data.Classes`, and the `ColorTable`,
`GemTable` and `PropertiesTable` you build yourself from `engine.Data`.

`MagicAffixTable`, `MissileTable` and `SkillDamage` do **not** follow it — they are keyed lookups
rather than row walks, so reach for their own accessors instead of `RowAt`.

Two tables have two row spaces and name their accessors after them instead:

```csharp
engine.Sets.SetCount     / engine.Sets.SetAt(i)      // sets.txt
engine.Sets.PieceCount   / engine.Sets.PieceAt(i)    // setitems.txt — the ids a set item needs

engine.Data.MonsterTypes.MonsterCount     / .MonsterAt(i)
engine.Data.MonsterTypes.MonsterTypeCount / .MonsterTypeAt(i)
```

`TxtFile` is the raw column reader underneath all of them, and keeps `RowCount` with
`GetString(row, column)` / `GetInt` / `GetBool` — a row there has no fixed shape.

TypeScript is identical, camelCased: `rowCount`, `rowAt`, `setAt`, `pieceAt`, `monsterAt`.

### Loading tables from disk

If you would rather read a real MPQ extraction than use the embedded copy — a mod with altered
tables, or a different locale:

```csharp
TooltipEngine fromDisk = TooltipEngine.FromFiles(excelDir, localeDir, globalDir);
TooltipEngine fromTables = TooltipEngine.FromData(data);   // tables you already hold
```

TypeScript has `TooltipEngine.fromFiles` (Node-only) and `fromData`. Note the loader names differ:
C# has `D2DataFiles.LoadEmbedded()` and `D2DataFiles.Load(dirs...)`; TypeScript has
`D2DataFiles.load()` for both.

## The record format

A unit document is self-similar. `items` is what a unit contains: on an ITEM those are its socket
fillers, and **position in the array is the socket index**; on a WEARER they are the items it
carries, each with a `location` and — when equipped — an `x` giving the body slot.

That second form is what lets the library work out set state for you, so nothing has to hand it a
bit mask:

```json
{
  "unitType": 4,
  "classId": 442,
  "statsLists": [
    { "stateNo": 0,   "flags": 2147483648, "stats": [ { "id": 31, "value": 445 } ] },
    { "stateNo": 0,   "flags": 64,         "stats": [ { "id": 39, "value": 40 } ] },
    { "stateNo": 165, "flags": 8256,       "stats": [ { "id": 0, "value": 20 } ] }
  ],
  "items": [
    { "unitType": 4, "classId": 620 }
  ]
}
```

`flags` and `stateNo` are copied verbatim from the statlist node and the consumer derives
everything from them — there is deliberately no "source" or classification field for you to fill
in, because getting it wrong is how a real bug got in.

| bit | meaning |
|---|---|
| `0x80000000` `STATLIST_EXTENDED` | the base array |
| `0x40` `STATLIST_MAGIC` | everything the item itself grants |
| `0x2000` `STATLIST_SET` | the node sits on the `pMyStats` chain contributing nothing |

Set tiers are identified by `stateNo` 165-170, not by that bit.

A socket filler needs no stats of its own: a real client never instantiates them, so the engine
rebuilds a gem's or rune's contribution from `gems.txt` given the host it sits in.

## Fidelity

Correctness here means byte-identical to the original, including its oddities — where the game is
inconsistent, this reproduces the inconsistency rather than tidying it up. Every behaviour is
traced to the instruction that causes it, and the code cites the address.

| | |
|---|---|
| differential corpus cases, C# vs TypeScript, agreeing layer by layer | **939** |
| hostile producer-legal inputs, both engines agreeing | **11,972** |
| tests | **1066** C# / **1082** TypeScript |
| captured client tooltips reproduced byte-identically | **64 / 64** |

The capture set is a private `captures.db` of real client tooltips and is not part of this
repository, so that last row is the one number you cannot reproduce from a clone.

## What is not implemented, and why

Everything below is a known gap rather than a bug. None of it throws; the output is simply absent
or English-only.

**Transaction-cost text.** `ShopMode` 1-9 is accepted and suppresses the book usage lines, but no
price line is produced on the generic path at all — the routine that computes the price has not
been decompiled. On the set-item path you get the "cannot be traded here" refusal, which is real.

**Only English.** The embedded tables are the ENG locale, and the possessive form used in item
names ("Bob's Hat") is wired for English only — twelve other language cases are identified but not
transcribed. Point `FromFiles` at another locale's tables and the strings change, but the
possessive grammar does not.

**Item level, when the capture omits it.** Three property arms scale with the *item's* level: funcs
11 and 19 when the property's max is non-positive, and func 14's socket cap. The record carries
`itemLevel` for exactly this, but it is optional, and −1 means "not captured" rather than 0 — the
game's own floor is 1. Without it those arms floor at level 1 and say so through
`ItemRollRanges.ItemLevelDependent` rather than reporting a number they cannot know. Supply
`itemLevel` and they become exact. Note the example C++ producer does not emit it yet, so records
from it will always report these.

**One property function is unimplemented.** Func 9, and no shipped table carries it — a test walks
them and fails the build if that stops being true. Every other function is implemented and on the
live path: `Ranges` re-applies them to reconstruct roll spans.

**The C++ producer is unfinished.** An optional capture half that reads a live game's memory. You
do not need it to use the library — only to generate records from a running client.

**No Diablo II: Resurrected support.** This models 1.14d; D2R's tables and item format differ.

## Contributing

Source, tests and the differential harness live at
[github.com/ResurrectedTrader/D2ItemToolkit](https://github.com/ResurrectedTrader/D2ItemToolkit).
`dotnet test` and `npm test` run the two suites; the repository's `CLAUDE.md` documents how the two
implementations are kept in agreement and what the working rules are.

## Licence

Source code is
[MIT](https://github.com/ResurrectedTrader/D2ItemToolkit/blob/main/LICENSE).

`data/` contains tables extracted from Diablo II 1.14d and embedded in the published packages so
the library works without a game install. Those files are the property of Blizzard Entertainment
and are **not** covered by the MIT licence. Diablo II is a trademark of Blizzard Entertainment,
Inc. This project is not affiliated with, endorsed by, or sponsored by Blizzard Entertainment.
