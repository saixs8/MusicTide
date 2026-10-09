# Application icon

- `AppIcon.png`: original image created with the built-in image_gen tool; transparent corners, 1254 × 1254.
- `AppIcon.ico`: 32-bit PNG icon frames at 16, 20, 24, 32, 40, 48, 64, 96, 128 and 256 pixels.
- Rebuild format packaging: run `./Tools/BuildAppIcon.ps1` from the project root.
- The project embeds this ICO in the Windows executable and as a managed resource for the main form.
- Published in v0.8.2. Build completed without errors or warnings. The published executable icon was extracted into `Docs/UiPreview/AppIcon_Executable.png`; all 14 existing user data files were unchanged during publishing.

## Generation prompt

Create a polished Windows desktop application icon for a music spectrum and stock candlestick visualization app. Square 1024x1024, one single centered icon, not a presentation or mockup. Dark graphite rounded-square tile occupying 90% of canvas, genuine transparent outside rounded corners. A bold minimal integrated symbol: four thick cyan audio equalizer bars of varied heights, the right two morph into coral-red stock candlesticks with short thick wicks, and a small crisp ivory musical note integrated in upper left. Strong recognizable silhouette, generous clean padding, flat geometric precision with very subtle premium gradients only, no thin details, no grid, no text, no letters, no watermark, no outer shadow. Cyan and coral contrasting with charcoal like a professional Chinese stock terminal. Beautiful readable at 16 and 32 pixels.
