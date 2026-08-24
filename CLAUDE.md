# CLAUDE.md

Guidance for Claude Code (claude.ai/code) when working in this repository.

## What this is

A reimplementation of Diablo II 1.14d's item description engine, reconstructed from the
disassembly. A C++ producer captures a unit's stat lists as JSON; a C# consumer renders the exact
tooltip the game would draw. See `README.md` for the split and `docs/writers.md` for the spec.

Correctness here means **byte-identical to the original**, including its oddities. This is not a
reimplementation "in the spirit of" the game.

## The rules that matter

### The disassembly is the only truth

Not IDA's names, not its function bounds, not the C# code, not comments, not D2MOO, not this file.
Verify against the binary before changing behaviour.

**Never fix a bug by pattern-matching the symptom.** A parity failure tells you *that* something is
wrong, never *what*. Read the function in IDA, work out what it actually does, and implement that —
then cite the address. A change that makes the output match without a traced explanation is a guess
that happens to fit today's sample, and it will be wrong on the next one. This applies to captured
game output too: real data proves a divergence exists, it does not tell you which side is right.

If the disassembly is unavailable, say so and stop, rather than reasoning from plausibility.

Concretely, in this binary:

- **IDA function names are frequently lies.** `ITEMS_nullsub` is not a nullsub — it is the
  level-requirement fold. `szPrintfBuffer` is the weapon-damage writer. `ModifyPotionValueByDifficulty`
  never reads difficulty. `ITEMMODS_CalculateRepairCost` is a by-time interpolator.
- **IDA mis-reports `start_ea`.** It has been wrong at least four times here — verify the prologue
  (`55 8b ec`, usually after `0xCC` padding) rather than trusting the reported entry.
- **D2MOO can merge two distinct functions into one.** It decompiles `STATLIST_CalcCombinedStatValue`
  cases 1 and 13 through a single helper whose 1.14d counterparts are different functions reading
  different arrays. Following it there makes a correct fix look impossible.
- **The decompiler loses register arguments** on `__usercall`/`__fastcall` and drops varargs. Use
  `disasm` and read the pushes for anything involving argument passing or colour markers.

### Addresses

Every `0x...` is an offset into the **merged single-binary 1.14d build** — one `0x400000`-based
`.text`. They are **not** 1.10f DLL-relative. D2MOO annotates the same offsets as `1.14d: Game.0x...`.

### Game data

Comes from an MPQ extraction of the **1.14d** MPQs. Never a CASC extraction (those are D2R-era) and
never a re-exported or modded tree. The embedded copies under `data/` are byte-identical to that
extraction, and `tools/DataSmoke` takes an extraction path so you can check they have not drifted.

`STRUCT_CreateBinFieldExcelAndFillData` (real entry `0x6bd640`) deletes any row whose first cell is
exactly `Expansion`. Most `.bin` files therefore hold one fewer record than the `.txt`, and **every
literal row index in the binary is post-splice**. Verify per file — `ItemStatCost.txt` has no such
row, `ItemTypes.txt` does.

### Reachability is where this project goes wrong

Across eight audit rounds, the *mechanism* claims were reliable and the *"can a player see it"*
claims were wrong in **every single round**, in both directions:

- over-claimed three times (inflated affix counts, cell counts passed off as row counts)
- under-claimed once — a bug dismissed as unreachable for four rounds turned out to affect fifteen
  real items, because "every class-restricted shield is a paladin shield" was simply false

So: **count rows against the shipped files, not cells, and not from memory.** A dismissal is where
the next bug hides. If something is unreachable, say why, with the count.

### Comments

Only for genuinely non-obvious behaviour, and cite the address it models. No narrative comments, no
banners, no restating what the code plainly does. Much of this code looks odd because the original
is odd — that is what earns a comment; ordinary code does not.

### Parsers

Any data-parsing helper is written in **C#**, not PowerShell.

## Working method

The find → verify → fix loop is how every bug here was found, and the verify step caught something
in **every** round: a finding refuted outright, an expected string wrong in three ways, a proposed
fix of the wrong shape, a stated cause that concealed a larger defect underneath.

When changing behaviour:

1. Trace it in the disassembly yourself and cite the addresses.
2. Establish reachability by counting against the shipped data.
3. Fix, and pin it with a test asserting the **exact** rendered string.
4. Change **both** implementations, or the differential will fail — and it is the only thing
   holding them together.
5. Update `docs/writers.md` — it is the living spec, and a stale line there has caused bugs.

After changing C#, run the ReSharper inspection and read the SARIF. After changing TypeScript, run
ESLint **and** Prettier — they are separate gates, and `npm run lint` does not include formatting.

Be sceptical of a passing test suite. Four separate times a test was found to be asserting the bug
rather than catching it: the modifier-block assertions, a gems loop starting at row 1, a stubbed
`GetPlayerStatValue` returning 0, and a durability expectation pinning an unresolved value.

## Building

**`dotnet run` forwards unrecognised flags to the APPLICATION.** `dotnet run --project tools/Corpus
-c Release --nologo -v q -- in.json out.json` hands `--nologo -v q` to the tool as `args[0..2]`, so
the corpus generator wrote to a file literally named `--nologo` and left `corpus.json` untouched —
and the same slip against `tools/Reference` clobbered a corpus input. Put nothing between
`--project <path>` and `--` except `-c Release`. Both generators are byte-deterministic, so the
recovery is to re-run them and diff, not to restore from git: HEAD's `adversarial.json` is an older,
larger corpus.

```bash
# C#
dotnet test                                  # 1041 tests
dotnet run --project tools/RecordSmoke
dotnet run --project tools/DataSmoke         # optional: -- <excelDir> <localeDir>

# TypeScript (npm workspaces, rooted at the repository)
npm ci                                       # also generates the embedded data blob
npm test                                     # 1057 tests, including the differential
npm run test:adversarial                     # 11,972 producer-legal hostile cases, opt-in
npm run typecheck
npm run lint                                 # ESLint, type-aware; --max-warnings 0
npm run format                               # Prettier; format:check to verify only

# ReSharper (tool version pinned in .config/dotnet-tools.json)
dotnet tool restore
dotnet jb inspectcode D2ItemToolkit.sln --output=obj/inspect.sarif --format=Sarif --severity=WARNING
```

`npm ci` runs the package's `prepare`, which regenerates `src/D2ItemToolkit.Ts/src/Data/
EmbeddedDataBlob.ts` from `data/` — a gzipped container of all 37 files (33 excel, 3 locale, 1
global), **generated and
gitignored**, so the package works from a published install and in a browser. `fflate` is the one
runtime dependency; its inflate is synchronous, which is what lets `D2DataFiles.load()` keep the
signature the whole suite already uses. Regenerate with `npm run generate:data` after touching
`data/`; the generator pins the gzip mtime to 0 so its output is byte-deterministic.

Settings that encode a deliberate deviation, rather than a rule nobody got round to:
`D2ItemToolkit.sln.DotSettings` turns off `CheckNamespace` (folders are for navigation, the namespace
stays flat `D2ItemToolkit`), and `.editorconfig` keeps local constants PascalCase.

`eslint.config.mjs` used to disable the `no-unsafe-*` family and `no-redundant-type-constituents`
because a record document was `unknown` until the readers narrowed it by hand. That is no longer
true — `UnitRecord` is a typed DTO and the narrowing happens only in `unitRecordFromJson`, so those
rules are back on. Do not re-disable them: if they start firing, untyped data has leaked past the
parse boundary, which is the thing they now guard.

**MSBuild does not notice changed embedded resource content** when the glob and project file are
unchanged. After touching anything under `data/`, wipe `obj/` and `bin/` or you will be testing the
old bytes.

**CI targets `D2ItemToolkit.CI.slnf`, not the `.sln`.** The solution carries an `.esproj` for the
TypeScript project whose SDK ships with Visual Studio and cannot be restored on a bare runner. Do
not "simplify" this to a bare `.csproj`: the solution is where `D2ItemToolkit.sln.DotSettings` and
the cross-project usage data live, and inspecting the library project alone reports **54** findings
the solution correctly resolves — 34 `CheckNamespace` (suppressed by the DotSettings) plus 20
`*.Global` ones that only look unused because the tests and tools are not in scope. The filter
gives both: no `.esproj`, full solution context. `build`, `test`, `format` and `jb inspectcode` all
accept it.

`dotnet format --verify-no-changes` is a PR gate, so keep the tree format-clean. Two settings make
that possible rather than a fight: `csharp_indent_case_contents_when_block = false` in
`.editorconfig` (the formatter otherwise re-indents every braced `case` arm), and `while (true)`
instead of `for (;;)` in `CrtQsort` (the formatter rewrites the latter to `for (; ; )`). It earns
its place — on its first run it found three places where a newline had been eaten and two
declarations sat on one line, which compiled fine and neither the 920 tests nor ReSharper noticed.

## Layout

```
data/                     shared game tables — the root, not per-project
docs/                     GITIGNORED — working notes, not shipped. See below.
docs/writers.md           the living spec, addresses throughout
docs/record-format.md     capture format and reader API
docs/audits.md            findings, refuted claims, traced-clean surface
docs/set-item-tooltip.md  spec for the set-item writer
docs/d2bsng-producer.md   build sheet for wiring the producer into d2bsng
producer/                 C++ capture for D2MOO's D2Common
src/D2ItemToolkit.Net/        C# consumer  (namespace and assembly: D2ItemToolkit)
tests/D2ItemToolkit.Net.Tests/
tools/                    smoke programs, not tests
```

Both implementations, and both test trees, use the same five folders — the order a record flows
through: `Data/` (file formats) → `Tables/` (typed views over them) → `Stats/` (the stat model) →
`Description/` (the DescFunc engine) → `Tooltip/` (the section writers). Cross-cutting suites
(end-to-end, regression, differential) and shared fakes stay at the test root.

**The folders are navigation only.** The C# namespace is flat `D2ItemToolkit` regardless of folder, so
moving a file never changes the public API — do not "fix" that to match the directory.

The TypeScript implementation lives at `src/D2ItemToolkit.Ts/` with tests at
`tests/D2ItemToolkit.Ts.Tests/`, sharing `data/` and `docs/`. It is a **peer** implementation — the C#
never consumes its output — so it has no MSBuild coupling, only its own CI job. npm workspaces are
rooted at the repository, which is what lets tests outside the package resolve their dependencies.

**The two implementations are held together by a differential test**, not by discipline:
`tools/Corpus` generates cases from the shipped tables, `tools/Reference` renders them with C#, and
the TypeScript suite requires byte agreement. When changing either engine, regenerate the reference
and expect the differential to tell you what moved.

A branch nothing in the corpus reaches is a branch the differential cannot police. That is not
hypothetical: the corpus once produced 738 colour-3 markers and none of them on a Defense line,
because no case carried an `ac%` modifier — so an injected wrong colour digit there went
undetected until the corpus was extended.

## Design decisions already settled

Do not relitigate these without new evidence:

- **The record carries no classification.** `flags` and `stateNo` are copied verbatim; the consumer
  derives base / set-bonus / item-mods from them. A derived `source` field previously existed and
  caused a real bug — the merge filtered on it where the engine filters on flags and state.
- **The capture stays leaf-per-list; no `FullStats`.** The goal is less data in the document. The
  consequence is accepted: `ItemStatOps.Resolve` re-applies op 13 consumer-side, which is exact for
  shipped data but complete only because ops 6/7 happen to be unspawnable.
- **The player's merged attributes may be summed** from the player's own leaves plus each equipped
  item's resolved value — but *only* for attributes. `damagerelated` is blank on strength,
  dexterity, energy, vitality and level, which bypasses the `STATLIST_DYNAMIC` skip in
  `STATLIST_CalcFullStatFromChildren`. Any `damagerelated` stat does hit that skip. Do not extend
  the summing site to one.
- **`itemLevel` is optional and −1 means absent.** No tooltip WRITER reads it. It exists for the
  property handlers that derive a value from the item's level — funcs 11 and 19 with a non-positive
  max, and func 14's MaxSock tier — which report themselves through `ItemLevelDependent` when it is
  missing rather than guessing. Do not default it to 0: 0 is below the game's own floor of 1 and
  would read as a real level.
- **Roll ranges come from running the handlers twice, not from arithmetic.** `RollEnd` forces every
  range to its low or high end and the reconstruction diffs the two stat dictionaries. That is why
  func 7's truncation (0..15, not 5..15) and func 18's packed triple come out right without special
  cases. Do not "simplify" it into min/max arithmetic over the table columns.
- **A stat written at only one end has value 0 at the other**, because a zero value writes nothing
  (0x65ea63). That is NOT a layer roll. Funcs 12 and 36 are the layer-rolling pair and are detected
  by their func number, reported as a span of layers around one fixed value.
- **A crafted item's recipe is found by SLOT, never by `input 1`'s base code.** That cell is not a
  plain item code: three of the 36 rows name a type with no item of that code (`blun`, `rod`,
  `spea`), `amul` and `ring` are likewise types whose items are `amu` and `rin`, and 24 carry a
  trailing `upg` whose meaning is untraced. `axe` is NOT a fourth type-only case — weapons.txt row 3
  is the Axe, code `axe`, so that recipe resolves through the item-code branch and lands on the same
  `weap` slot the type branch would. Slot matching sidesteps every one of those, because the recipes
  are 4 families × 9 slots with one row per pair, so the answer is the same under any reading of the
  cell. Do not "improve" it into base-code matching — that silently loses every weapon, amulet and
  ring. The candidate filter is **every** stat key the recipe writes, not the first two mods; the
  families being disjoint on their first two is a data-drift canary a test pins, not the mechanism.
  `CraftSlots` uses `shie` and not `shld` deliberately: the four shield recipes name item codes
  whose whole upgrade chain is `shie`, and `ashd` / `head` are SIBLINGS of `shie` under `shld`, so
  no reading of the cell reaches a class shield and `shld` would claim 30 uncraftable items are
  identifiable. The cube's actual `input 1` semantics stay open, but cost nothing — slot derivation
  is tier-invariant, so `upg` cannot change an answer. See `docs/writers.md`.
- **Set state is DERIVED from the viewer, never handed over as masks.** `TooltipEngine.SetStateOf`
  walks `IUnit.Items` once and makes the distinction a caller cannot: GetSetItem takes grid types
  1, 3 and 4 (0x4867d4) so a piece on the alternate weapon set is OWNED and green, while the worn
  mask takes type 3 alone (0x62a3f0) so it lights no bit. A body item's type is
  `(bodyLoc >= 11) ? 4 : 3` (0x63b1e2), and `x` carries that location. `EarnedSetIdsOf` shares the
  same walk so `Ranges` and `Render` cannot disagree about which tiers are earned. `RenderSetItem`
  keeps its explicit input, for forcing a state the viewer does not have.
- **`IUnit.Items` is one FIELD carrying two relations** — socket fillers on an item, carried gear on
  a wearer — and the claim that "nothing has to tell them apart" was wrong. `EnumerateGroups`
  RECURSES, because a filler's stats are part of what the item grants; run a wearer through it and
  every carried item's stats fold into the viewer's attributes, so a charm in the backpack meets a
  requirement the character does not. Anything reading a PLAYER must use `EnumerateOwnGroups`. The
  Horadric Cube is the other case that would break the merge — an item holding items at a filler's
  depth — and is not modelled.
- **`IsEquipped` is derived from `Location`, and that IS a substitution.** The full-set block is
  gated on `dwAnimMode == 1` (0x48d870); the capture has no animMode, so `SetStateOf` uses
  `Location == 1` instead. Whether the two fields always agree is UNTRACED. Do not "correct" this
  back to a refusal — an earlier comment forbade the substitution while the code performed it, and
  following that rule re-breaks the full-set block, which was dead on the Render path until this
  landed. If animMode ever reaches the wire, prefer it.
