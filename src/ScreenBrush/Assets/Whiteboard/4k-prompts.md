# 4K background restoration

2026-09-25. User explicitly approved fallback CLI / Image API. Bundled image_gen.py edit, gpt-image-2, size 3840x2160, quality high, PNG. Each edit references the corresponding original image. All three resulting files verified as 3840x2160. Original inputs preserved in originals/; final app assets are Chalkboard.png, Notebook.png, Dots.png in this directory. API outputs also saved in output/imagegen/*-4k.png.

Earlier built-in attempts returned 1672x941 despite requesting 4K and were not applied.

## Chalkboard

Edit the provided image as a strict high-resolution restoration/upscale, output 3840x2160 pixels (4K landscape) or higher resolution if supported. Preserve exactly the existing background design, color palette, mood, layout, spacing, number and positions of lines/dots, and relative texture scale. Do not redesign, add, remove or rearrange anything. Improve fine detail clarity, clean antialiased edges and texture fidelity for zoomed handwriting background use. No oversharpening halos, no new objects, no text, no borders. This is the Chalkboard existing ScreenBrush background. Only enhance resolution and fidelity.

## Notebook

Edit the provided image as a strict high-resolution restoration/upscale, output 3840x2160 pixels (4K landscape) or higher resolution if supported. Preserve exactly the existing background design, color palette, mood, layout, spacing, number and positions of lines/dots, and relative texture scale. Do not redesign, add, remove or rearrange anything. Improve fine detail clarity, clean antialiased edges and texture fidelity for zoomed handwriting background use. No oversharpening halos, no new objects, no text, no borders. This is the Notebook existing ScreenBrush background. Only enhance resolution and fidelity.

## Dots

Edit the provided image as a strict high-resolution restoration/upscale, output 3840x2160 pixels (4K landscape) or higher resolution if supported. Preserve exactly the existing background design, color palette, mood, layout, spacing, number and positions of lines/dots, and relative texture scale. Do not redesign, add, remove or rearrange anything. Improve fine detail clarity, clean antialiased edges and texture fidelity for zoomed handwriting background use. No oversharpening halos, no new objects, no text, no borders. This is the Dots existing ScreenBrush background. Only enhance resolution and fidelity.

