# App icon assets

`src/MyBudget.App/Assets/MyBudgetIconMaster-v2.png` is the committed 1024 px source artwork. `build_app_icons.py` converts it into the multi-resolution ICO and Windows logo PNGs used by the project.

The converter requires Python 3 and Pillow:

```powershell
python -m pip install Pillow
python tools/build_app_icons.py src/MyBudget.App/Assets/MyBudgetIconMaster-v2.png src/MyBudget.App/Assets
```

The ICO contains 16, 20, 24, 32, 40, 48, 64, 96, 128, and 256 px frames. `ApplicationIcon` embeds those frames in `MyBudget.App.exe`, while the published `Assets/MyBudget.ico` file supplies the WinUI title-bar and taskbar icon at runtime.
