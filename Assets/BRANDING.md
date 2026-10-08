# DiscShelf logo

The owner supplied the blue PCX DISC badge. The silver optical DVD sits inside the blue rounded square, behind the aqua lettering.

- `discshelf-logo.png`: full-resolution artwork, embedded in the header and boot screen.
- `discshelf.ico`: Windows executable/window icon, with transparent 16, 24, 32, 48, 64, 128, and 256 pixel frames.
- Run `powershell -STA -File scripts/MakeIcon.ps1` from the source repository root after replacing the PNG to regenerate the icon. This only scales and packages the artwork.

## Generation

Created with the built-in imagegen tool, editing the owner's supplied original reference image. No API keys or generation service are needed to run DiscShelf.

Final prompt:

> Use case: precise-object-edit / compositing. Edit target: supplied original blue rounded-square PCX DISC logo. Correct DVD placement: add one silver optical DVD INSIDE the blue rounded square, BEHIND THE TEXT, completely contained within the square's boundaries. Layer order: blue gradient square background at back; centered circular DVD in the middle; original opaque aqua PCX and DISC lettering in front. The DVD is a large circular silver disc with a center hole, hub rings, and restrained blue/cyan iridescent reflections, visible around and between the letter shapes. Leave enough blue gradient border around the disc so its entire circle is contained within the square. Preserve the exact custom letter shapes, large PCX top line and smaller DISC bottom line, original relative positions and bright aqua palette. Keep the text highly legible with a restrained dark blue shadow if needed. Preserve the original upright rounded-square silhouette and blue-to-navy background. Absolutely no DVD protruding above, below, beside, or outside the square. No floating disc behind the entire badge. No extra words or emblems. True transparency ONLY outside the blue rounded square. Clean polished square Windows app logo.
