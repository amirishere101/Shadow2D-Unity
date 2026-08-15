# Shadow2D

Drop shadows for 2D sprites, split into two components because most objects in a scene never move.

A shadow here is a child GameObject carrying its own SpriteRenderer. It copies the caster's sprite, matches flipX and flipY, tints itself dark, and sits underneath rotated 12.5 degrees and squashed to 90% height. Those defaults live in a config asset, and they're what sells the effect as light arriving at an angle rather than a decal stuck to the floor.

Copying the sprite is what makes animation work. When the player's walk cycle advances a frame, the shadow picks up that same frame, so the silhouette on the ground matches the one in the air. `Shadow2DDynamic` does this in `LateUpdate`, after the Animator has run.

A fence post has no walk cycle.

`Shadow2DStatic` writes the shadow once in `Start` and then stops: no `Update` in the class, no `LateUpdate`, no per-frame method of any kind, so a static shadow costs one extra SpriteRenderer in the draw loop and zero managed calls per frame. Inari's Kitchen (the farming sim this came out of) runs outdoor scenes holding a few hundred shadow-casting props against maybe a dozen things that animate, which is the ratio the split was built for.

Which component to use is a decision you make when you place the object, and nothing tries to infer it. Guess wrong toward "static" and you get a shadow that quietly stops matching its sprite; a heuristic's wrong guess is much harder to track down than your own.

## Installation

Via the Package Manager: **Window > Package Manager > + > Add package from git URL** and paste

```
https://github.com/amirishere101/Shadow2D-Unity.git
```

Or add it to `Packages/manifest.json` yourself:

```json
"com.sleepyheadstudios.shadow2d": "https://github.com/amirishere101/Shadow2D-Unity.git"
```

Unity 2021.3 or newer, built-in 2D packages only. The shaders are written against the built-in render pipeline; see Limitations if you're on URP.

## Setup

Add `Shadow2DStatic` or `Shadow2DDynamic` to a GameObject that has a SpriteRenderer, then press **Create Shadow** in the inspector. Both inspectors handle multi-selection (forty fence posts, one action), creation and deletion register with `Undo`, and the scene is marked dirty only when something actually changed. That last part matters if you've ever had an editor tool hand you unsaved changes you didn't make.

Defaults come from a `Shadow2DConfig` asset. The package ships one (in its `Resources` folder) so everything works out of the box; to change the defaults project-wide, create your own via **Assets > Create > SleepyHead Studios > Shadow2D Config**, name it `Shadow2DConfig`, and put it in any `Resources` folder — it takes precedence over the packaged one. This is better than the alternative: forty shadows authored with forty slightly different offsets and no obvious reason why.

```csharp
var shadow = GetComponent<Shadow2DStatic>();

shadow.SetShadowActive(false);            // toggle without destroying
shadow.OverrideSprite = customSilhouette; // custom shape, refreshes immediately
shadow.ShadowColor = new Color(0, 0, 0.1f, 0.4f);
shadow.UpdateShadow();                    // resync after changing the sprite yourself
```

Both components resolve their source renderer by checking the root first, then falling back to children. That exists because crops keep their visible sprite on a `CropVisual` child while the shadow component sits on the root, and the obvious `GetComponent<SpriteRenderer>()` found a disabled renderer with a null sprite and drew nothing.

## Sorting

The shadow has to sit behind its caster without breaking whatever depth scheme the scene already uses, and 2D projects disagree about what that scheme is. With Y-position sorting on, the shadow takes the caster's exact `sortingOrder` and lets its Y coordinate settle the depth. Without it, the shadow drops one order behind. Both paths copy the caster's `sortingLayerID`, because a shadow stranded on a different layer gives you the bug where a character walks behind a wall and their shadow doesn't.

## Overlapping shadows

Two half-transparent shadows overlapping used to produce a darker patch where they crossed — put two trees next to each other and the seam gives the trick away. The shadow shader now claims a stencil value per pixel, so once one shadow has drawn there, others skip it: overlaps merge into one uniform patch. If you use `SpriteMask` (which also uses the stencil buffer) and see interference, change **Stencil Ref** on the shadow material, or set **Stencil Comparison** to *Always* to turn the merging off entirely.

## Custom shapes

Copying the sprite is right most of the time, and wrong for anything with a hole through it, anything very tall, or anything whose art already bakes in its own shading. Set `OverrideSprite` and the component draws your silhouette instead.

The **Shadow Brush** is how you make one — press **Edit Shadow Shape (Brush)** in the inspector. It clones the source sprite into a writable texture under `Assets/ShadowEdits`, assigns it as the override, and opens a window where you paint and erase with an adjustable brush size and opacity, with the result visible live in the Scene view. Only the alpha channel matters; the shadow tint supplies the colour. If it finds an existing `_ShadowEdit` texture it picks that up rather than starting over from the original sprite, so reopening the tool on something you edited last week carries on from where you stopped. Paint strokes aren't undoable — **Revert** reloads the last saved file, and **Reset From Source** re-clones the caster's sprite.

## The marker component

Every generated shadow gets a `ShadowColorEnforcer`. No fields, no methods, no runtime behaviour of any kind. Other systems check for it before they touch a renderer's colour, which is the entire reason it exists.

The game's target highlighter tints whatever the player is aiming at, and before the marker existed it tinted the shadows too. Exactly as bad as it sounds. A tag string would have worked and cost a string comparison on every check; a type lookup can't be misspelled.

## Materials

Shadows share a single packaged material (`Shadow2D_Shadow`), so they batch with each other and the shader can't be stripped from builds. Creating a shadow also swaps the caster's material for `Shadow2D_Caster` — but only when the caster is still on `Sprites/Default`; a custom material is never touched, and the whole behaviour can be turned off with `replaceCasterMaterial` on the config. Per-object overrides go in the component's **Shadow Material** field or the config's material slots.

## Limitations

This is a tinted, squashed, rotated copy of a sprite. No occlusion, no light direction beyond the fixed offset in the config, no soft edges, no interaction with Unity's 2D lighting. If you want shadows that answer to a moving light source, use URP's 2D shadow casters; this solves a cheaper problem.

The shaders target the built-in render pipeline. On URP the components still work — sprite copying, sorting, and the override system don't care about the pipeline — but assign your own URP-compatible materials via the config, and you lose the stencil overlap merging unless your material provides it.

Each shadow adds a SpriteRenderer. An object carrying one costs two draw calls where it used to cost one, and while atlasing and batching still apply as normal, the count doubles.

## Known issues

There are no automated tests. Behaviour is almost entirely SpriteRenderer state-copying, which wants a Unity harness rather than plain NUnit, so it hasn't been worth building yet.

The performance argument above is structural rather than measured. Static shadows demonstrably execute no per-frame code, but this repo doesn't yet publish a frame-time comparison at a realistic prop count.

## Requirements

Unity 2021.3 or newer. Nothing beyond the built-in 2D packages.

MIT.
