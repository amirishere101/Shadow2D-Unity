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
"com.dryflystudio.shadow2d": "https://github.com/amirishere101/Shadow2D-Unity.git"
```

Unity 2021.3 or newer, built-in 2D packages only. Shaders ship for both the built-in render pipeline and URP, and the right pair is picked automatically.

## Setup

Add `Shadow2DStatic` or `Shadow2DDynamic` to a GameObject that has a SpriteRenderer. That's the whole setup — the shadow appears immediately, the Scene view jumps to it with the Rect tool active so you can nudge it, and the Shadow Brush opens pointed at it in case you want to reshape it. Remove the component and the shadow goes with it.

There is no Create button, because there is no state where you have the component and want no shadow. There's no Delete button either: delete the shadow object in the Hierarchy like any other object. When you do, the inspector says so and offers **Recreate Shadow**, which starts over from the caster's sprite and discards the silhouette you painted. That's the one destructive action in the package, and it's behind a button you have to go looking for.

Both inspectors handle multi-selection (forty fence posts, one action), and every change registers with `Undo`.

If you pick the wrong component, **Convert To Dynamic** / **Convert To Static** swaps the type in place, carrying every setting and the shadow object itself across — you don't lose the silhouette you painted. Putting `Shadow2DStatic` on something with an Animator raises a warning in the inspector with that button attached, because a static shadow on an animated sprite fails quietly: it holds the first frame forever while the sprite above it keeps walking.

Defaults come from a `Shadow2DConfig` asset. The package ships one (in its `Resources` folder) so everything works out of the box; to change the defaults project-wide, create your own via **Assets > Create > DryFly Studio > Shadow2D Config**, name it `Shadow2DConfig`, and put it in any `Resources` folder — it takes precedence over the packaged one. This is better than the alternative: forty shadows authored with forty slightly different offsets and no obvious reason why.

```csharp
var caster = GetComponent<Shadow2DStatic>();

caster.SetShadowActive(false);                  // hide them all, without destroying
caster.SetShadowActive(false, 1);               // or just the second one

Shadow2DInstance shadow = caster.GetShadow(0);
shadow.color = new Color(0, 0, 0.1f, 0.4f);
shadow.overrideSprite = customSilhouette;       // custom shape
shadow.rotationZ = 30f;                         // needs overrideTransform
caster.UpdateShadow();                          // push the changes

Shadow2DInstance second = caster.AddShadow();   // another light direction
caster.RemoveShadow(1);
```

Both components resolve their source renderer by checking the root first, then falling back to children. That exists because crops keep their visible sprite on a `CropVisual` child while the shadow component sits on the root, and the obvious `GetComponent<SpriteRenderer>()` found a disabled renderer with a null sprite and drew nothing.

## Following the caster

A shadow copies more than the sprite. **Follow Caster Visibility** hides it whenever the caster's SpriteRenderer is disabled or its GameObject goes inactive, and **Follow Caster Alpha** multiplies the shadow's alpha by the caster's, so a sprite fading out takes its shadow with it. Both are on by default; both are no-ops until something actually changes, so they cost a comparison.

They only apply as often as the component syncs. `Shadow2DDynamic` re-checks every `LateUpdate`. `Shadow2DStatic` checks once in `Start` — if you disable a static caster's renderer at runtime, call `UpdateShadow()` after, or use the dynamic component.

## More than one shadow

A caster can have as many shadows as you want. **Add Shadow** in the inspector appends
one, and each gets its own entry in the list with its own name, colour, silhouette,
material and transform — two light directions, or a soft pool under a prop plus a harder
shape beside it. The **×** on an entry deletes just that shadow, taking its silhouette
with it if no other shadow is using that shape.

Settings that can only have one answer per object stay above the list: which renderer to
copy, whether to follow the caster's visibility and alpha, and the sorting mode. Selecting
a shadow in the Hierarchy points the brush at that specific one, not at the first in the
list.

Overlapping shadows on the same caster merge into one uniform patch rather than
double-darkening, the same as shadows from separate objects — that's the stencil work
described under Overlapping shadows.

## Positioning the shadow

Each shadow's offset, Z rotation and scale live on its entry in the inspector, and they are always what drives it. Drag the shadow in the Scene view and those fields are updated to match when you let go, so the two never disagree — there's no mode to switch between and nothing to bake in by hand.

**Reset To Config** puts a shadow back to the project defaults.

Moving, rotating or resizing a shadow by hand resyncs it as soon as you let go — the sort point has to be recomputed from wherever you dropped it. It waits for the drag to end rather than fighting the handles mid-drag, and the shadow itself doesn't move; only the invisible sort point does.

## Sorting

2D projects disagree about how depth works, so there are two modes. Both copy the caster's `sortingLayerID`, because a shadow stranded on a different layer gives you the bug where a character walks behind a wall and their shadow doesn't.

**Y-sorting on** (the default, set by `useYSortingByDefault` on the config): the shadow shares the caster's `sortingOrder` and lets its own Y position settle the rest. That is what makes it behave like something lying on the ground — drawn over the props behind it, hidden by the props in front of it.

For one version this was `sortingOrder + 1`, to guarantee shadows landed on other objects. That was the wrong lever: shadows were being forced behind everything by the render queue, not by their sorting order, and +1 put them on top of the entire layer including objects standing in front of the caster.

**Y-sorting off**: the shadow drops one order behind the caster, which is what a project sorting purely by order expects.

Each shadow sorts from exactly the same point as its caster. That is what keeps a shadow off the objects standing in front of the thing casting it: sharing the sort point means the shadow is drawn over everything the caster is drawn over and behind everything the caster is behind, and there is no third answer available. Any sort point *below* the caster brings the bug back, because lower means drawn later under Y-sorting — so the shadow starts winning against props between it and the camera.

Not *exactly* the same point: a shadow sits a thousandth of a unit further up the sort axis, which loses it every tie. Matching the caster exactly left every shadow tied with every caster at that Y, and Unity breaks ties by internal index — so two props at identical height, which is what you get from pasting a duplicate and dragging sideways, had one prop's shadow arbitrarily winning against the other prop's body. Nothing else about the ordering changes.

Getting it there takes some work, because SpriteRenderer has no arbitrary sort point — only the bounds centre or the pivot, neither of which can be aimed somewhere else. So each shadow draws a pivot-corrected copy of its sprite with the pivot moved onto the caster's sort point, and the shadow object is nudged by exactly the opposite amount so the sprite still lands where you put it. Those copies are cached per source sprite, so an animated shadow allocates one per frame of its animation and then stops.

Your *casters* want their sort point at their base too, for the same reason — but that's a property of your art rather than of this package. The project-side scene tool has a bulk fix.

Shadows render in the ordinary transparent queue, so all of that actually applies. Until 1.4 they sat at `Transparent-1`, and because render queue is a coarser sort key than sorting layer, that shoved every shadow behind every sprite regardless of what the sorting settings said — which is wrong if your scene is one foreground layer sorted by Y, as most 2D projects are.

## Not casting on itself

A shadow falls across the props behind it and is occluded by the ones in front, like any other sprite. But with Y-sorting a shadow sits below its caster, so it sorts *in front* of it — and would paint over the object casting it.

Draw order can't fix that. For a shadow to reach a prop standing in front of its caster, it has to be drawn after that prop, and therefore after the caster too. A shadow can be behind its caster or in front of the scene, never both. Every sorting layer, order and Z arrangement runs into the same wall.

So the caster is masked out per pixel instead. The shadow material samples the caster's own alpha and discards wherever the caster is opaque, which is exact regardless of how the two are rotated or squashed relative to each other.

There's no toggle for this. A shadow offset downward always sits at a lower Y than its caster, so under Y-sorting it draws in front of it in every scene rather than some of them — an off switch would only ever produce a bug.

The cost is a `MaterialPropertyBlock` per shadow carrying the caster's texture and a transform matrix, so shadows no longer batch into a single draw call. The matrix is recomputed whenever the shadow syncs — once in `Start` for a static shadow, every `LateUpdate` for a dynamic one.

## Overlapping shadows

Two half-transparent shadows overlapping used to produce a darker patch where they crossed — put two trees next to each other and the seam gives the trick away. The shadow shader now claims a stencil value per pixel, so once one shadow has drawn there, others skip it: overlaps merge into one uniform patch. If you use `SpriteMask` (which also uses the stencil buffer) and see interference, change **Stencil Ref** on the shadow material, or set **Stencil Comparison** to *Always* to turn the merging off entirely.

On URP this depends on the 2D Renderer holding onto its depth-stencil attachment across the light-blend passes, which varies by URP version and renderer setup. Check it in your own project rather than assuming it; if overlaps still double-darken, *Always* and a visible seam is the honest fallback.

## Custom shapes

Copying the sprite is right most of the time, and wrong for anything with a hole through it, anything very tall, or anything whose art already bakes in its own shading. Set `OverrideSprite` and the component draws your silhouette instead.

**Static shadows only.** A dynamic shadow rewrites its silhouette from the caster every `LateUpdate`, and an override sprite wins over that — so painting one doesn't give you a custom shape, it gives you a shadow frozen on whichever animation frame you happened to trace. The brush isn't offered on `Shadow2DDynamic` at all. If the object doesn't really animate, convert it to `Shadow2DStatic` and the brush appears.

Setting `OverrideSprite` directly on a dynamic shadow still works, because a fixed blob under a walking character is a perfectly good technique — but the inspector will point out that the shadow has stopped following the animation, in case that wasn't the plan.

The **Shadow Brush** is how you make one. It opens by itself when a static shadow is created, and from **Edit Shape (Brush)** in the inspector after that.

Opening it doesn't change anything. Press **Start Painting** to take over the Scene view; until you do, the Rect tool and normal selection work as usual. The first stroke is what clones the shadow's current silhouette into a writable texture and assigns it as the override, so opening the brush on a shadow you end up not editing leaves no assets behind.

While the brush is open it follows your selection. Click another shadow — or the object casting it, in the Scene view or the Hierarchy — and whatever you were editing is saved before the brush retargets. A painting session carries across the switch, so you can work through a row of props without touching the window.

Selecting a shadow never *opens* the brush; a window you closed stays closed. Retargeting doesn't steal focus either, and selecting something that isn't a shadow leaves the brush pointed where it was rather than blanking it. Uncheck **Follow Selection** in the window to pin it to one shadow.

What you start from is exactly the shadow you already had — you add to it and erase from it. The canvas is padded out well beyond the original sprite, so strokes don't stop dead at its edge and you can extend a silhouette past the shape that generated it. Only the alpha channel matters; the shadow tint supplies the colour.

| | |
|---|---|
| Left-drag | Paint or erase |
| Shift | Temporarily invert the mode |
| Ctrl/Cmd + scroll | Brush size |
| Ctrl/Cmd + Z | Undo the last stroke |
| Ctrl/Cmd + S | Save the silhouette |

While painting, the brush owns the Scene view: the transform gizmo is hidden and clicks go to strokes rather than to selection, and the full paintable canvas is outlined so you can see how far the padding extends. Press Stop Painting to get the Rect tool back.

The OS cursor is hidden inside the Scene view while painting, so the brush outline is the only pointer you see; it comes back everywhere else in the editor. The outline is drawn in the shadow's own space, squashed and rotated the way the shadow is, so what the circle covers is what the stroke paints.

Undo is per stroke, twenty deep. **Revert** reimports the PNG on disk and drops everything since the last save; **Reset From Source** re-clones the caster's sprite into the same asset, so nothing is orphaned.

Silhouettes are written to a `ShadowEdits` folder inside the package. Delete a shadow and its silhouette goes too — but only after checking that no other shadow, in any loaded scene or any prefab, is still using that shape. If anything else references it, the file stays and the console says why.

## Duplicating a caster

Copy-paste and Ctrl+D work. The one thing to know is that a duplicate can come back still
referencing the original's shadow object rather than its own copy — Unity's reference
remapping doesn't always reach into a serialized list. Two components driving one shadow
looks like the *original* breaking, because it's the one being fought over.

The package checks ownership and repairs it: any shadow that isn't a child of its own
component gets re-pointed at that component's own copy. This happens on `Awake`, when a
component is first inspected, and on scene load, so it's fixed before you'd notice.

## Debugging the mask

The shadow materials carry a **Debug show mask** toggle. It draws the self-cast mask as
flat colour rather than a shadow — red where a pixel lands inside the caster's sprite
rect, green where the caster is opaque there, so yellow marks what's being discarded. If
a shadow is darkening its own caster, that toggle says immediately whether the mask is
aimed at the wrong place or not arriving at all.

## The marker component

Every generated shadow gets a `Shadow2DMarker`. No fields, no methods, no runtime behaviour of any kind.

The package needs it. Resolving which renderer to copy walks the caster's children looking for a sprite, and without a marker it can't tell a nested shadow from a legitimate visual child — so a shadow ends up copying another shadow.

It's also the cheapest hook for your own code. Anything that sweeps renderers and changes their colour — a target highlighter tinting whatever the player is aiming at — will tint the shadows too unless it skips them:

```csharp
if (renderer.GetComponent<Shadow2DMarker>() != null) continue;
```

A tag string would have worked and cost a string comparison on every check; a type lookup can't be misspelled.

## Materials

Shadows share a single packaged material, so they batch with each other and the shader can't be stripped from builds. Which one they get depends on the active render pipeline: `Shadow2D_Shadow` on built-in, `Shadow2D_URP_Shadow` when a Universal Render Pipeline asset is driving rendering. The config has a slot for each; per-object overrides go in the component's **Shadow Material** field, which applies on the next sync rather than only at creation.

Creating a shadow can also swap the *caster's* material for the matching `..._Caster` material, which is the same unlit sprite shader with `ZWrite On`. That existed to stop shadows crossing casters via the depth buffer, and the self-cast mask replaced it — the shadow shader now uses `ZTest Always` precisely so a stray depth write can't hide a shadow that should be falling across something. **It is off by default** (`replaceCasterMaterial` on the config) and there is no longer a good reason to turn it on; it is kept only so existing projects don't break.

Even with it on, only stock unlit sprite materials are ever replaced: `Sprites/Default` and URP's `Sprite-Unlit-Default`. A custom material is never touched, and neither is URP's `Sprite-Lit-Default` — swapping a lit caster for an unlit one would drop it out of 2D lighting, which is a miserable bug to find. When the package declines to replace a caster for that reason, the inspector says so instead of leaving you guessing.

## Limitations

This is a tinted, squashed, rotated copy of a sprite. No occlusion, no light direction beyond the fixed offset in the config, no soft edges, no interaction with Unity's 2D lighting. If you want shadows that answer to a moving light source, use URP's 2D shadow casters; this solves a cheaper problem.

Each shadow adds a SpriteRenderer. An object carrying one costs two draw calls where it used to cost one, and while atlasing and batching still apply as normal, the count doubles.

## Render pipelines

Both pipelines ship with their own shader and material pair, and `Shadow2DConfig` picks between them by reading the active pipeline asset — nothing to configure. The components themselves never cared: sprite copying, sorting, and the override system are pipeline-agnostic.

The URP shaders are deliberately written against `UnityCG.cginc` rather than URP's ShaderLibrary. Those includes only exist when `com.unity.render-pipelines.universal` is installed, and a missing include is a hard compile error in every project that isn't on URP. The `RenderPipeline` tag keeps them out of built-in projects instead, so the package has no URP dependency and still works properly on both.

Both caster materials are unlit, exactly like the originals. This package doesn't do lit shadows.

## Known issues

There are no automated tests. Behaviour is almost entirely SpriteRenderer state-copying, which wants a Unity harness rather than plain NUnit, so it hasn't been worth building yet.

The performance argument above is structural rather than measured. Static shadows demonstrably execute no per-frame code, but this repo doesn't yet publish a frame-time comparison at a realistic prop count.

The URP shaders compile and render, but stencil overlap merging under the 2D Renderer hasn't been verified across URP versions. See Overlapping shadows.

Silhouette reference-checking scans every prefab in the project before deleting a file. On a large project that pause is noticeable, and cancelling it counts as "still in use" — the file is kept. Keeping an orphaned 4KB PNG is a much cheaper mistake than deleting a shape forty prefabs were sharing.

## Requirements

Unity 2021.3 or newer. Nothing beyond the built-in 2D packages.

MIT.
