# Adding DiscShelf themes

Open Settings → Appearance. Choose one of the eight built-in themes, or use Import theme to select a JSON file. The supplied `Themes/example-theme.json` is an editable example.

For a new theme, choose Create theme template. Change its name, ID, and colours in a text editor, then choose Reload themes. You can also place JSON files directly in `%LOCALAPPDATA%\DiscShelf\themes`. Select the theme and Save settings. Theme selection previews immediately; Cancel restores your previous appearance.

```json
{
  "Id": "my-theme",
  "Name": "My theme",
  "Author": "You",
  "BackgroundTop": "#0C2148",
  "BackgroundMiddle": "#164B8B",
  "BackgroundBottom": "#071B37",
  "Panel": "#163B64",
  "Accent": "#B4DDFF",
  "Text": "#F0F5FF",
  "Muted": "#A3BFDC",
  "Wave": "#9ECFFF"
}
```

Use `#RRGGBB` or `#AARRGGBB` colours. IDs may use letters, numbers, dashes, and underscores. Choose a unique ID; a matching ID overrides an existing theme. Keep theme JSON below 64 KB. Invalid files are skipped and reported in Settings. Theme files contain colours and metadata, not scripts or XAML.

Appearance settings separately control wave animation and brightness, particles, clock display and format, tile size, cover fit, and interface font. Startup settings control the `discshelfv1` boot screen and its duration. `Themes.cs` defines the built-in palettes and maps theme values to WPF resources if you want to extend the theme format in source.
