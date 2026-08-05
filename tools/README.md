# Brand and asset tooling

## App icon assets

`src/MyBudget.App/Assets/MyBudgetIconMaster-v2.png` is the committed 1024 px source artwork. `build_app_icons.py` converts it into the multi-resolution ICO and Windows logo PNGs used by the project.

The converter requires Python 3 and Pillow:

```powershell
python -m pip install Pillow
python tools/build_app_icons.py src/MyBudget.App/Assets/MyBudgetIconMaster-v2.png src/MyBudget.App/Assets
```

The ICO contains 16, 20, 24, 32, 40, 48, 64, 96, 128, and 256 px frames. `ApplicationIcon` embeds those frames in `MyBudget.App.exe`, while the published `Assets/MyBudget.ico` file supplies the WinUI title-bar and taskbar icon at runtime.

## README logo reveal

`docs/media/mybudget-logo-reveal.gif` is the looping animation shown at the top of `README.md`. It is rendered from a 1280x720, 24 fps, 10 second master video that ends on the same wallet-and-chart mark as the app icon. The master video is not tracked by Git; keep it with the brand assets.

The conversion requires FFmpeg and produces a 440x248, 12 fps, infinitely looping GIF:

```powershell
ffmpeg -i logo-reveal-master.mp4 -vf "fps=12,scale=440:-2:flags=lanczos,hqdn3d=4:4:6:6,split[a][b];[a]palettegen=max_colors=40:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle" -loop 0 docs/media/mybudget-logo-reveal.gif
```

The frame rate, 440 px width, denoise pass, and 40-color palette keep the file under 4 MB so the README stays quick to load. Raising any of them grows the GIF quickly, because the animated background changes every pixel on every frame.
