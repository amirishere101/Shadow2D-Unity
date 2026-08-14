# Shadow2D

Drop shadows for 2D sprites, split into two components because most objects in a scene never move.

A shadow here is a child GameObject carrying its own SpriteRenderer. It copies the parent's sprite, matches flipX and flipY, tints itself dark, and sits underneath rotated 12.5 degrees and squashed to 90% height. Those defaults live in a config asset, and they're what sells the effect as light arriving at an angle rather than a decal stuck to the floor.

Copying the sprite is what makes animation work. When the player's walk cycle advances a frame, the shadow picks up that same frame, so the silhouette on the ground matches the one in the air. `Shadow2DDynamic` does this in `LateUpdate`, after the Animator has run.

A fence post has no walk cycle.

`Shadow2DStatic` writes the shadow once in `Start` and then stops: no `Update` in the class, no `LateUpdate`, no per-frame method of any kind, so a static shadow costs one extra SpriteRenderer in the draw loop and zero managed calls per frame. Inari's Kitchen (the farming sim this came out of) runs outdoor scenes holding a few hundred shadow-casting props against maybe a dozen things that animate, which is the ratio the split was built for.

Which component to use is a decision you make when you place the object, and nothing tries to infer it. Guess wrong toward "static" and you get a shadow that quietly stops matching its sprite; a heuristic's wrong guess is much harder to track down than your own.

## Sorting

The shadow has to sit behind its caster without breaking whatever depth scheme the scene already uses, and 2D projects disagree about what that scheme is. With Y-position sorting on, the shadow takes the parent's exact `sortingOrder` and lets its Y coordinate settle the depth. Without it, the shadow drops one order behind. Both paths copy the parent's `sortingLayerID`, because a shadow stranded on a different layer gives you the bug where a character walks behind a wall and their shadow doesn't.

## Custom shapes

Copying the sprite is right most of the time, and wrong for anything with a hole through it, anything very tall, or anything whose art already bakes in its own shading. Set `overrideSprite` and the component draws your silhouette instead.

`ShadowBrushTool` is how you make one. It clones the source sprite into a writable texture under `Assets/ShadowEdits`, then lets you paint and erase directly in the Scene view with an adjustable brush size and opacity. If it finds an existing `_ShadowEdit` texture it picks that up rather than starting over from the original sprite, so reopening the tool on something you edited last week carries on from where you stopped.

## Setup

Add `Shadow2DStatic` or `Shadow2DDynamic` to a GameObject that has a SpriteRenderer, then press **Create Shadow** in the inspector. Both inspectors handle multi-selection (forty fence posts, one action), creation and deletion register with `Undo`, and the scene is marked dirty only when something actually changed. That last part matters if you've ever had an editor tool hand you unsaved changes you didn't make.

Defaults come from a `Shadow2DConfig` asset in `Resources`. Miss one and the components fall back to built-in values and log a warning telling you how to create it, which is better than the alternative: forty shadows authored with forty slightly different offsets and no obvious reason why.

```csharp
var shadow = GetComponent<Shadow2DStatic>();

shadow.SetShadowActive(false);            // toggle without destroying
shadow.OverrideSprite = customSilhouette; // custom shape, refreshes immediately
shadow.UpdateShadow();                    // resync after changing the sprite yourself
```

`Shadow2DStatic` resolves its source renderer by checking the root first, then falling back to children. That exists because crops keep their visible sprite on a `CropVisual` child while the shadow component sits on the root, and the obvious `GetComponent<SpriteRenderer>()` found a disabled renderer with a null sprite and drew nothing.

## The marker component

Every generated shadow gets a `ShadowColorEnforcer`. No fields, no methods, no runtime behaviour of any kind. Other systems check for it before they touch a renderer's colour, which is the entire reason it exists.

The game's target highlighter tints whatever the player is aiming at, and before the marker existed it tinted the shadows too. Exactly as bad as it sounds. A tag string would have worked and cost a string comparison on every check; a type lookup can't be misspelled.

## Limitations

This is a tinted, squashed, rotated copy of a sprite. No occlusion, no light direction beyond the fixed offset in the config, no soft edges, no interaction with Unity's 2D lighting. If you want shadows that answer to a moving light source, use URP's 2D shadow casters; this solves a cheaper problem.

Each shadow adds a SpriteRenderer. An object carrying one costs two draw calls where it used to cost one, and while atlasing and batching still apply as normal, the count doubles.

## Known issues

`Shadow2DStatic.HasChanged()` and the four cached fields behind it (`lastSprite`, `lastFlipX`, `lastFlipY`, `lastSortingOrder`) are never called, because there's no `Update` in the class to call them. They're left over from a middle design that sat between "every frame" and "never". Either wire them to an opt-in polling mode or delete them, since as written they imply behaviour the component doesn't have.

There are no automated tests. Behaviour is almost entirely SpriteRenderer state-copying, which wants a Unity harness rather than plain NUnit, so it hasn't been worth building yet.

The performance argument above is structural rather than measured. Static shadows demonstrably execute no per-frame code, but this repo doesn't yet publish a frame-time comparison at a realistic prop count. [BENCHMARK: need a scene with N props measured both ways]

## Requirements

Unity 2021.3 or newer. Nothing beyond the built-in 2D packages.

MIT.
