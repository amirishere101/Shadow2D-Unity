# Changelog

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
If you previously copied the `Assets/SleepyheadStudios/Shadow2D` folder into a
project, delete that copy before installing the package, and note that your
project generated its own GUIDs for those scripts — existing scenes will need
their Shadow2D components rebound (the serialized `shadowObject` references and
field values survive if you use Unity's script remapping, or you can recreate
the shadows with the multi-select Create button).

## [1.0.0]
- Initial release: static/dynamic split, config asset, marker component,
  shadow/caster shaders.
