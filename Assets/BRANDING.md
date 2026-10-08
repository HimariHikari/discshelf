# DiscShelf logo

The owner supplied the blue PCX DISC badge. The updated logo adds a silver optical DVD behind that badge and has a transparent background.

- `discshelf-logo.png`: full-resolution artwork, embedded in the header and boot screen.
- `discshelf.ico`: Windows executable/window icon, with transparent 16, 24, 32, 48, 64, 128, and 256 pixel frames.
- Run `powershell -STA -File scripts/MakeIcon.ps1` from the repository root after replacing the PNG to regenerate the icon. This only scales and packages the artwork.

## Generation

Created with the built-in imagegen tool, editing the owner's supplied reference image. No API keys or generation service are needed to run DiscShelf.

Final prompt:

> Use case: precise-object-edit / compositing. Asset type: DiscShelf Windows app logo and icon, square PNG with true transparent space outside the emblem. Image 1 is the edit target. Edit this supplied blue rounded-square logo by adding a DVD behind it. Preserve the exact original custom cyan/aqua lettering (top line PCX, bottom line DISC), letter shapes, relative text layout, and bright-blue-to-deep-navy gradient badge. Keep the original logo upright, as an intact foreground badge. Add one clean silver optical DVD behind the badge, slightly offset so a substantial upper and side arc and its central hub are visibly peeking out from behind the badge. The disc must read clearly as an optical DVD: circular polished silver surface, center hole/hub rings, restrained blue/cyan and subtle rainbow reflections, small DVD lettering on the exposed disc only if legible. Balanced centered square app-icon composition with a small safe margin, original foreground badge occupying most of the frame and DVD unmistakably behind it, no cut-off edges. No black background, no checkerboard drawn into the pixels, no extra words or objects, no distortion or redrawing of the source letterforms. Maintain the original blue/cyan palette and polished late-2000s console feel. Transparent outside the combined badge-and-disc silhouette.
