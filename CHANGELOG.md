# Changelog

## [1.9.6] - 2026-08-15

### Fixed
- Two props at exactly the same Y could have one prop's shadow drawn over the other
  prop's body. Shadows sort from their caster's sort point, so at identical Y every
  caster and every shadow shared a sorting layer, a sorting order and a sort-axis value -
  a complete tie, which Unity resolves by internal index. Whichever way it fell was
  arbitrary and looked like a shadow landing on a caster it had no business touching.
  The self-cast mask can't cover this: it excludes a shadow's own caster, and here the
  victim is the neighbour.

  Shadows now sit `0.001` further up the sort axis than their caster, purely to lose
  ties. Nothing else changes - a prop genuinely behind a shadow is still further up the
  axis, one in front is still lower - and the pivot correction cancels the offset
  visually. Duplicates pasted at the same height, which is most of them, now resolve the
  same way every time.

## [1.9.5] - 2026-08-15

### Fixed
- Duplicating or copy-pasting a caster could leave the duplicate's shadow list still
  referencing the **original's** shadow object. Two components then drove one shadow,
  each computing its transform, sprite and self-cast mask against a different caster -
  which presents as the *original's* shadow breaking, since it is the one being fought
  over, and clears up the moment the duplicates are deleted.

  Ownership is now checked and repaired: any shadow that isn't a child of its own
  component is re-pointed at that component's own copy, and its cached renderer and
  pivot-corrected sprites are dropped with it. Runs on `Awake`, when a component is
  first inspected, and on scene load. A caster carrying several Shadow2D components
  won't hand the same child to two of them.

### Added
- **Debug show mask** toggle on the shadow materials. Renders the self-cast mask as flat
  colour instead of a shadow - red where a pixel falls inside the caster's sprite rect,
  green where the caster is opaque there, so yellow is what the mask discards. Off by
  default. It exists because "the mask isn't working" has several possible causes that
  look identical, and this separates them in one glance.

## [1.9.4] - 2026-08-15

### Fixed
- A moved or resized shadow flipped between its old and new position depending on which
  code path last synced it - jumping back on Start Painting and forward again on Stop.
  Two different answers to "where does this shadow sit" were in play: the live transform
  read back through `sortShift`, and the `offset` field. A Scene view drag updated the
  first and not the second, so whichever ran last won.

  `offset` is now the single source of truth. Every sync draws the position from it, and
  finishing a drag reads the new position, rotation and scale back into the fields.

### Changed — breaking
- `Shadow2DInstance.overrideTransform` is gone, along with the **Control From Inspector**
  toggle and the **Capture From Shadow** button. Offset, rotation and scale are always
  live and always editable; dragging in the Scene view writes back to them, which is what
  Capture used to do by hand. Existing shadows keep their transform - the field is simply
  no longer consulted.

## [1.9.3] - 2026-08-15

### Fixed
- A shadow vanished after you stopped painting it, and came back if you nudged it in the
  Scene view. Saving a silhouette reimports the texture, and a reimport replaces the
  Texture2D **object** while leaving the source Sprite reference intact. The
  pivot-corrected sprite each shadow draws is built from that texture object and cached
  against the source sprite, so the cache kept handing back a sprite whose texture had
  been destroyed - which draws nothing. Moving the shadow changed its sort shift,
  invalidated the cache, and rebuilt it from the new texture, which is why it reappeared.

  The cache now checks that a hit's texture still matches the source's, and the brush
  releases the derived sprite explicitly at every point that reimports: save, revert,
  reset from source, first-stroke creation, and adopting an existing silhouette.

## [1.9.2] - 2026-08-15

### Added
- Moving, rotating or resizing a shadow by hand resyncs it once the drag finishes.
  Dragging a shadow changes the position its sprite has to keep, which changes the pivot
  correction that puts its sort point on the caster - so until something resynced it, a
  hand-placed shadow sorted from the wrong spot. Dragging the caster resyncs too, since
  its own sort point can move.

  The resync waits for the drag to end rather than running mid-drag, where it would fight
  the transform handles: it triggers when the transform has stopped changing and nothing
  is holding the GUI's hot control. The shadow stays exactly where it was dropped - only
  the invisible sort point moves.

## [1.9.1] - 2026-08-15

### Fixed
- Shadows were drawing on objects standing in front of the object casting them. Each
  shadow's sort point now sits exactly on its caster's sort point, so it sorts wherever
  the caster sorts: drawn over everything the caster is drawn over, behind everything the
  caster is behind, with no third answer possible.

  1.9.0 placed the sort point 10% up from the shadow's lowest point, which is *below* the
  caster - and lower means drawn later under Y-sorting, so the shadow started winning
  against props between it and the camera. Any sort point below the caster reintroduces
  this; matching the caster is the only position that can't.

### Removed
- `sortPointHeightFraction` on Shadow2DConfig. The sort point is the caster's, not a
  measurement of the shadow, so there is nothing left to tune.

## [1.9.0] - 2026-08-15

### Changed
- Each shadow's sort point is now placed near the bottom of the shape it actually draws,
  10% of its height up from the lowest point, measured after rotation and squash.
  1.8.0 switched shadows to `SpriteSortPoint.Pivot`, which only helps when the art's
  pivot is at its base - for centre-pivoted sprites the sort point still landed mid-rect
  and shadows kept sorting as though standing upright.

  SpriteRenderer offers no arbitrary sort point, only Center or Pivot, so each shadow now
  draws a pivot-corrected copy of its sprite with the pivot moved onto the sort point.
  Moving a pivot shifts where the sprite draws, so the shadow object is nudged by exactly
  the opposite amount and the result is visually identical. The nudge is recorded per
  shadow and subtracted on the next sync rather than compounding.
- `sortPointHeightFraction` on Shadow2DConfig tunes the 10%. Zero puts the sort point at
  the very lowest pixel.

### Performance
- The corrected sprites are cached per shadow, keyed by source sprite, so a dynamic
  shadow allocates one per animation frame and then stops. They are destroyed when the
  shadow is removed, when the component is destroyed, and whenever the shadow's rotation
  or scale changes and invalidates them.

### Note
The alternative mechanism would be a SortingGroup per shadow. Unity's documentation
doesn't state whether a SortingGroup sorts by its own transform or by its children's
combined bounds under custom-axis sorting, and the difference decides whether the
approach works at all, so it wasn't used.

## [1.8.0] - 2026-08-15

### Changed
- Shadow renderers are forced to `SpriteSortPoint.Pivot`, enforced on every sync
  alongside sorting layer and order. Unity's default sort point is the bounds centre,
  which for a shadow half a sprite tall sits well above the ground - so it sorted as
  though standing upright and slid behind props it should have been lying in front of.
  Pivot puts the sort point at the shadow's own origin, which for the bottom-pivot art a
  Y-sorted project uses is as close to its lowest point as Unity's API allows.

### Note
`SpriteSortPoint` only offers Center or Pivot; an arbitrary sort point isn't available
without a SortingGroup per shadow. Casters want the same treatment for the same reason,
but that's a property of your art rather than of this package - see the project-side
scene tool for a bulk fix.

## [1.7.1] - 2026-08-15

### Fixed
- A gap between a caster and its shadow, worst furthest from the pivot. The self-cast
  mask normalised the position by `Sprite.rect` (the untrimmed rect) but sampled through
  `Sprite.textureRect` (where the pixels actually live after the importer trims
  transparent borders). On any trimmed sprite that stretched the mask across the full
  rect, cutting a hole larger than the sprite. The mask now maps straight to page UV
  including `textureRectOffset`, and the shader bounds-tests against the sprite's rect on
  that page. Untrimmed sprites were unaffected, which is why it only showed on some
  objects.
- Shadows drew on top of objects standing in front of their caster. With Y-sorting a
  shadow now shares the caster's `sortingOrder` again and lets its own Y position settle
  the rest, so it is drawn over the props behind it and hidden by the ones in front.
  Rendering at `sortingOrder + 1` in 1.6.0 was a misdiagnosis - shadows were being forced
  behind everything by the `Transparent-1` render queue, fixed separately in 1.5.0, not
  by their sorting order.

### Known limitation
The self-cast mask assumes the caster's sprite is not rotated within its atlas page.
Sprite Atlases with "Allow Rotation" enabled can pack a sprite turned 90 degrees, which
this mapping does not account for.


## [1.7.0] - 2026-08-14

### Added
- A caster can now have **any number of shadows**, each with its own name, colour,
  silhouette, material and transform - two light directions, or a soft pool plus a harder
  shape beside it. New `Shadow2DInstance` type holds one shadow's settings; the inspector
  draws them as a list with **Add Shadow** and a per-entry delete.
- Deleting one shadow takes its silhouette with it only when no *other* shadow wants that
  shape - including sibling shadows on the same caster, which the previous
  whole-component exclusion would have missed.
- Selecting a shadow in the Hierarchy points the brush at that specific shadow. Resolution
  walks every Shadow2D component on the parent and matches the actual GameObject rather
  than taking the first component it finds.

### Changed — breaking
- Per-shadow settings moved off the component and into the list, so the single-shadow API
  is gone. `ShadowColor`, `OverrideSprite`, `ShadowMaterial`, `OverrideTransform`,
  `ShadowOffset`, `ShadowRotationZ` and `ShadowScale` are now fields on
  `Shadow2DInstance`, reached via `GetShadow(index)`; call `UpdateShadow()` afterwards to
  push them. `CreateShadow()` becomes `AddShadow()`, `DeleteShadow()` becomes
  `RemoveShadow(index)` / `RemoveAllShadows()`. `GetShadowObject()` and
  `GetShadowRenderer()` take an optional index and still default to the first shadow.
  `SetShadowActive(bool)` still affects all of them; pass an index for one.
- Caster-wide settings (`followCasterVisibility`, `followCasterAlpha`, `useYSorting`)
  stay on the component, since they can only have one answer per object.

### Migration
Existing shadows are folded into the list automatically the first time the component is
inspected or a scene loads, keeping their colour, silhouette, material and transform. The
editor marks the object dirty so it persists - save the scene afterwards. Nothing needs
recreating and no silhouettes are lost.

## [1.6.0] - 2026-08-14

### Changed
- With Y-sorting on, a shadow now renders one `sortingOrder` **in front of** its caster
  rather than sharing the caster's order. Sharing it left the Y axis to decide the
  result, so a shadow landed on props behind it and was hidden by anything in front -
  which, in a scene where everything shares one foreground layer, reads as Y-sorting
  not working. Sorting order is a coarser key than Y, so +1 puts the shadow on top of
  that layer every time. Y-sorting off is unchanged: one order behind the caster.
- Removed the **Prevent Self Cast** toggle; the mask is always on. At one order in front
  the shadow covers its caster in every scene rather than some of them, so switching it
  off could only ever produce a bug. The shader still exposes `_SelfMask` for anyone
  driving the material themselves.

### Migration
Existing shadows pick the new order up the next time they sync - on `Start` for static
shadows, immediately for dynamic ones. Nothing needs re-creating. If you had set
`preventSelfCast` to false on a component, that field is gone and masking now applies.

## [1.5.0] - 2026-08-14

### Fixed
- Shadows were forced behind every sprite in the scene. The shadow material sat at
  `Queue = Transparent-1`, and render queue is a coarser sort key than sorting layer, so
  no sorting layer, order or Y-sort setting could put a shadow in front of anything. In a
  project where everything shares one foreground layer and sorts by Y - which is most 2D
  projects - that meant shadows never landed on other objects at all. Shadows now render
  in the ordinary `Transparent` queue and sort like any other sprite.

### Added
- **Prevent Self Cast**, on by default. A shadow that can reach a prop standing in front
  of its caster is necessarily drawn in front of the caster too, so it would paint over
  the object casting it - and no ordering fixes that, since a shadow can be behind its
  caster or in front of the scene but not both. The shadow shader now samples the
  caster's own alpha and discards wherever the caster is opaque, which is exact whatever
  the relative rotation and squash. Available in both the built-in and URP shaders.

### Changed
- The shadow shaders use `ZTest Always`. Depth no longer participates in shadow
  ordering, so a caster material that writes depth can't hide a shadow that should be
  falling across it.
- Caster material replacement is now legacy. It existed to make casters write depth so
  shadows couldn't cross them, which the self-cast mask replaces. Still off by default,
  still there so existing projects don't break, but there is no longer a reason to
  enable it.

### Performance
- Shadows carry a `MaterialPropertyBlock` holding the caster's texture and a transform
  matrix, so they no longer batch into a single draw call. The matrix is rebuilt on each
  sync: once in `Start` for a static shadow, every `LateUpdate` for a dynamic one. Turn
  off Prevent Self Cast to get batching back, at the cost of shadows darkening their own
  casters.

## [1.4.0] - 2026-08-14

### Changed
- Shape editing is no longer offered for `Shadow2DDynamic`. A dynamic shadow re-copies
  the caster's sprite every `LateUpdate`, and a painted silhouette is stored as an
  override, which wins over that copy - so painting one froze the shadow on whichever
  animation frame happened to be showing. The brush button is hidden on dynamic
  components, creating a dynamic shadow no longer pops the brush open, selection
  skips dynamic shadows rather than retargeting to a dead end, and the brush drops its
  target if the component is converted to dynamic while open. Calling
  `ShadowBrushTool.Open` on one explains why instead of proceeding.
- `OverrideSprite` is still assignable on a dynamic shadow - a fixed blob under an
  animated character is a legitimate technique - but the inspector now warns when one is
  set, because the shadow will not follow animation frames.

## [1.3.2] - 2026-08-14

### Added
- An open brush follows the selection. Selecting a shadow - or the caster it belongs to,
  in the Scene view or the Hierarchy - saves whatever silhouette was being edited and
  retargets the brush at the new one. A painting session carries across the switch, so
  you can keep painting straight onto the new shadow.
- A **Follow Selection** checkbox in the brush window turns it off, for pinning the brush
  to one shadow while selecting other things.

  Selection never opens the brush and never pulls focus to it: a window you closed stays
  closed, and retargeting leaves you in whatever panel you were working in. The brush is
  still opened deliberately - from **Edit Shape (Brush)**, or automatically when a shadow
  is first created. Selecting anything that isn't a shadow leaves the current target
  alone rather than blanking the window.

## [1.3.1] - 2026-08-14

### Fixed
- Only a small part of the padded canvas could be painted, and strokes that appeared to
  do nothing showed up scattered across the silhouette after pressing Save. Silhouettes
  imported with the default `SpriteMeshType.Tight`, which trims the sprite mesh to the
  opaque pixels - exactly the transparent margin the padding exists to provide.
  `sprite.bounds` then reported the original silhouette rather than the full canvas, so
  the brush divided by a small rect and multiplied by the full texture width. Saving
  forced a reimport, the tight mesh regenerated around the newly painted alpha, and the
  strokes appeared. Silhouettes now import as `FullRect`, and existing ones are repaired
  when the brush next opens them.
- Brush coordinates are derived from the sprite's own rect and pivot instead of
  `sprite.bounds`, so mesh type can't affect where a stroke lands again.
- Clicks were reaching the selection picker and the transform gizmo before the brush.
  The Scene view's default control is now claimed on the Layout event before anything
  else inspects the event, strokes hold `hotControl` for the whole drag, and the
  transform gizmo is hidden while painting - its handles sit directly on top of the
  shadow and were taking the mouse first.
- A stroke could no longer be lost when the mouse ray missed the shadow plane: that
  path used to return before the control was registered, handing the click back to Unity.

### Added
- Ctrl/Cmd + S saves the silhouette while the brush is active. It falls through to
  Unity's own save when nothing has been painted yet, so it can't swallow a scene save.
- The full paintable canvas, padding included, is outlined in the Scene view while
  painting, so the area you can paint in is visible rather than inferred.

## [1.3.0] - 2026-08-14

### Changed — breaking
- Renamed the studio throughout: namespace `SleepyHeadStudios` is now `DryFlyStudio`,
  assemblies are `DryFlyStudio.Shadow2D` / `DryFlyStudio.Shadow2D.Editor`, the package is
  `com.dryflystudio.shadow2d`, menus live under **DryFly Studio**, and shaders are named
  `DryFlyStudio/...`. Update `using SleepyHeadStudios;` to `using DryFlyStudio;` in your
  own scripts. Scene and prefab references survive: every file kept its `.meta`, so GUIDs
  are unchanged, and materials reference shaders by GUID rather than name.
- `ShadowColorEnforcer` is now `Shadow2DMarker`. Same GUID, so existing shadows keep
  resolving; the old name described one game's use of it rather than what it is.
- Create Shadow and Delete Shadow buttons are gone. Adding the component creates the
  shadow, removing the component deletes it. A shadow you delete by hand stays deleted -
  the inspector reports it and offers **Recreate Shadow**, which starts over from the
  caster's sprite and discards the painted silhouette.
- Silhouettes are written to a `ShadowEdits` folder inside the package instead of
  `Assets/ShadowEdits`. When the package is installed from a git URL its folder is
  immutable, so in that case they fall back to `Assets/Shadow2D/ShadowEdits`.
- The brush no longer has an opacity slider. Paint sets alpha to fully opaque, erase to
  fully transparent.

### Fixed
- Starting to edit a shadow made the whole shadow disappear. The silhouette was cloned
  with `Graphics.DrawTexture`, which only renders during a Repaint event - and cloning
  runs from a mouse event, so it produced a fully transparent texture every time.
  Replaced with `Graphics.Blit`, which works outside GUI repaints.
- Strokes stopped at the edge of the original sprite, because the canvas was exactly the
  sprite's rect. The silhouette is now padded on every side (half the longest edge,
  clamped to 16-128px) with the pivot shifted to match, so the shadow lands exactly where
  it did before and there is room to paint outside it.
- Removing a Shadow2D component left its shadow behind as an orphan.
- Deleting a shadow left its silhouette PNG behind forever.

### Added
- Undo for brush strokes, twenty deep, on Ctrl/Cmd + Z. The shortcut is consumed while
  the brush is active so Unity's global undo doesn't roll back something unrelated
  mid-stroke.
- Ctrl/Cmd + scroll adjusts brush size in the Scene view.
- The OS cursor is hidden inside the Scene view while painting, leaving the brush outline
  as the only pointer, and restored everywhere else in the editor.
- Creating a shadow selects it, frames it in the Scene view, switches to the Rect tool,
  and points the Shadow Brush at it - opening the window if it isn't up, retargeting it
  if it is.
- **Start Painting** / **Stop Painting** in the brush. The window opening no longer takes
  over the Scene view, so the Rect tool stays usable right after a shadow is created.
- Silhouette textures are only written to disk on the first stroke, so opening the brush
  on a shadow you don't end up editing leaves no assets behind.
- Deleting a shadow deletes its silhouette, but only after confirming no other Shadow2D
  component - in any loaded scene or any prefab - still uses that shape. The prefab scan
  is cancellable, and cancelling keeps the file.

### Performance
- Strokes mutate a cached `Color32[]` and upload once instead of calling `SetPixel` per
  pixel. At the new maximum brush size that is ~125,000 calls per event saved.

## [1.2.0] - 2026-08-14

### Fixed
- Shadows no longer keep drawing when their caster is hidden. Disabling a caster's
  SpriteRenderer (or deactivating the GameObject holding it) left a silhouette on the
  ground with nothing above it. Controlled by `followCasterVisibility`, on by default.
- The **Shadow Material** field did nothing after the shadow was created — it was read
  once at creation and never again, despite the README advertising it as the per-object
  override. It now applies on every sync. The config-resolved material is still only
  applied at creation, so hand-assigning a material on the shadow child still sticks.
- The Shadow Brush painted in the wrong place in a perspective Scene view. It derived
  the paint position from `ray.origin`, which is the camera position under perspective;
  it now intersects the mouse ray with the shadow's own plane. Only orthographic (2D
  mode) Scene views were unaffected.
- Inspector edits to a shadow's appearance weren't marked dirty. Changing Shadow Color
  looked correct until the scene was reloaded, at which point it reverted. Edits that
  touch the generated shadow now record Undo state and dirty the affected objects.
- `Shadow2DDynamic` allocated on the heap every `LateUpdate` whenever the source sprite
  was momentarily null, because the child-renderer search used the array-returning
  `GetComponentsInChildren`. Switched to the `List<T>` overload with a shared buffer.
- `UpdateShadow` resolved the source renderer before checking whether a shadow existed,
  so a dynamic component whose shadow was never created walked its whole child hierarchy
  once per frame to do nothing.
- `Shadow2DConfig`'s static cache survived play sessions with Domain Reload disabled,
  leaving a destroyed ScriptableObject reference behind and resolving every material to
  null on the second play. Cleared per run via `SubsystemRegistration`.
- The built-in-defaults fallback config leaked a `ScriptableObject` on every domain
  reload. It's now `HideAndDontSave`.
- The Shadow Brush matched any asset path containing `_ShadowEdit`, anywhere in the
  project. Now anchored to the `ShadowEdits` folder, and it prefers the component's
  override sprite over the renderer's, which can lag a sync behind.

### Added
- URP shader and material variants (`ShadowSpriteURP`, `SpriteWithShadowBlockURP`),
  selected automatically by reading the active render pipeline asset. Written against
  `UnityCG.cginc` rather than URP's ShaderLibrary, so the package still has no URP
  dependency and doesn't spew compile errors on built-in projects. Config gained
  `urpShadowSpriteMaterial` and `urpCasterMaterial` slots.
- **Control From Inspector** for the shadow transform: offset, Z rotation and scale
  fields on the caster drive the shadow object, so multi-select finally works for
  positioning. Off by default, so hand-placed shadows are never moved. With
  **Capture From Shadow** to bake a hand-placed transform into the fields, and
  **Reset To Config**.
- **Convert To Dynamic** / **Convert To Static** buttons. Swaps the component type in
  place, carrying every serialized field across including the hidden shadow reference,
  so the existing shadow and any painted silhouette are adopted rather than orphaned.
- Inspector diagnostics: an error when no SpriteRenderer exists anywhere in the
  hierarchy, a warning (with the convert button attached) when `Shadow2DStatic` sits on
  an object with an Animator, and a note when caster material replacement declines to
  touch a custom or lit material.
- `followCasterAlpha` — the shadow's alpha is multiplied by the caster's, so fading a
  sprite out fades its shadow with it. On by default; a no-op at full opacity.
- **Revert** and **Reset From Source** in the Shadow Brush. Both were documented in the
  1.1.0 README and neither existed. Reset From Source overwrites the same asset instead
  of allocating a new unique path, so it no longer orphans textures in `ShadowEdits`.
- `ShadowMaterial`, `OverrideTransform`, `ShadowOffset`, `ShadowRotationZ`,
  `ShadowScale` properties and `GetShadowRenderer()` on `Shadow2DBase`.

### Changed
- Inspector rewritten from `DrawDefaultInspector` into collapsible Appearance / Sorting
  / Shadow Transform sections with persisted foldout state, a status line for partial
  multi-selections, and Create/Delete disabled when they'd do nothing. The `[Header]`
  attributes moved out of the runtime fields, since the editor supplies the grouping.
- Caster material replacement now recognises URP's `Sprite-Unlit-Default` as a stock
  material it may replace, and explicitly refuses `Sprite-Lit-Default` — replacing a lit
  caster with an unlit one silently dropped it out of URP 2D lighting.
- README corrected: it described caster material replacement as on-by-default and
  disableable. It has shipped off by default since 1.1.0.

## [1.1.0] - 2026-08-14

### Added
- Proper UPM package layout: `package.json`, assembly definitions, and committed
  `.meta` files. Installable straight from the git URL via the Package Manager.
- `OverrideSprite` — serialized field and public property for custom silhouettes.
  Documented before; now it actually exists.
- **Shadow Brush** (`Editor > Edit Shadow Shape` button on both components):
  clones the caster's sprite into a writable texture under `Assets/ShadowEdits`,
  lets you paint and erase alpha in a dedicated window, and assigns the result
  as the override sprite. Picks up an existing `_ShadowEdit` texture rather than
  starting over.
- Source-renderer resolution: the root is checked first, then children, so the
  component works on objects that keep their visible sprite on a child.
- Multi-selection support in both inspectors; creation and deletion register
  with Undo.
- Packaged default config (`Resources/Shadow2DConfigDefault`) plus shared
  material assets. A user-created `Shadow2DConfig` in any `Resources` folder
  still takes precedence.
- Stencil-based overlap handling in the shadow shader: overlapping shadows now
  merge into one uniform patch instead of double-darkening. Configurable (or
  disabled) per material.
- Config's `useYSortingByDefault` and `defaultShadowColor` are now actually
  applied (on component `Reset`, i.e. when first added).

### Changed
- `Shadow2DStatic` and `Shadow2DDynamic` now share a `Shadow2DBase` class
  instead of duplicating ~200 lines each. Serialized field names are unchanged,
  so existing scenes keep their data.
- Shadow materials are shared assets referenced by the config instead of being
  instantiated per shadow via `Shader.Find` (which also broke in builds when
  nothing referenced the shaders).
- `RequireComponent(SpriteRenderer)` dropped in favour of the root-then-children
  renderer lookup; a clear error is logged if no sprite is found anywhere.
- Removed the unverified "70% faster" claim from the inspector help box.

### Removed
- Dead change-tracking code in `Shadow2DStatic` (`HasChanged` and the four
  cached fields) that implied polling behaviour the component never had.

### Migration
If you previously copied the `Assets/DryFlyStudio/Shadow2D` folder into a
project, delete that copy before installing the package, and note that your
project generated its own GUIDs for those scripts — existing scenes will need
their Shadow2D components rebound (the serialized `shadowObject` references and
field values survive if you use Unity's script remapping, or you can recreate
the shadows with the multi-select Create button).

## [1.0.0]
- Initial release: static/dynamic split, config asset, marker component,
  shadow/caster shaders.
