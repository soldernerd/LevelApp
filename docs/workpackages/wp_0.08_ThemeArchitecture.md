# Work Package 0.08 — Theme Architecture

> Target version: **v0.8.0**
> Prerequisite: WP0.07 complete (v0.7.0) ✓

---

## Goal

Introduce a proper WinUI 3 resource dictionary theming system. The app must
support **Light** and **Dark** themes, user-selectable and persisted across
sessions. No hardcoded colours, font sizes, or font weights anywhere in View
XAML after this work package is complete.

This work package is structural — it establishes the architecture for
consistent look and feel. The actual visual design / colour palette is
deliberately left for a subsequent work package once the theming scaffolding
is in place and working.

---

## New folder and files: `LevelApp.App/Styles/`

Create three new `ResourceDictionary` XAML files. These are the only files
that may contain colour values, font sizes, or font weights anywhere in the
project going forward.

### `Styles/ThemeColors.xaml`

All colour and brush tokens, defined independently for Light and Dark.
Keep app-specific tokens minimal — lean on WinUI 3 semantic brushes
(`CardBackgroundFillColorDefaultBrush`, `TextFillColorSecondaryBrush`, etc.)
wherever the built-in token is appropriate.

```xml
<ResourceDictionary>
  <ResourceDictionary.ThemeDictionaries>

    <ResourceDictionary x:Key="Light">
      <!-- Brand accent — overrides Windows system accent for this app -->
      <Color x:Key="AppAccentColor">#005B9A</Color>

      <!-- Surface plot: height-map node colours (blue→green→red scale) -->
      <SolidColorBrush x:Key="PlotLowBrush"      Color="#2980B9"/>
      <SolidColorBrush x:Key="PlotMidLowBrush"   Color="#27AE60"/>
      <SolidColorBrush x:Key="PlotMidBrush"      Color="#F1C40F"/>
      <SolidColorBrush x:Key="PlotMidHighBrush"  Color="#E67E22"/>
      <SolidColorBrush x:Key="PlotHighBrush"     Color="#C0392B"/>

      <!-- Measurement grid canvas -->
      <SolidColorBrush x:Key="GridCanvasBackgroundBrush" Color="#F8F8F8"/>
      <SolidColorBrush x:Key="GridStepArrowBrush"        Color="#444444"/>
      <SolidColorBrush x:Key="GridCurrentStepBrush"      Color="#005B9A"/>
      <SolidColorBrush x:Key="GridCompletedStepBrush"    Color="#27AE60"/>
      <SolidColorBrush x:Key="GridPendingStepBrush"      Color="#BBBBBB"/>
      <SolidColorBrush x:Key="GridFlaggedStepBrush"      Color="#C0392B"/>

      <!-- Closure loop fill (MeasurementsGrid) -->
      <SolidColorBrush x:Key="LoopOkBrush"       Color="#2020B020"/>
      <SolidColorBrush x:Key="LoopWarnBrush"     Color="#20E67E22"/>
      <SolidColorBrush x:Key="LoopErrorBrush"    Color="#20C0392B"/>
    </ResourceDictionary>

    <ResourceDictionary x:Key="Default"> <!-- Dark mode -->
      <Color x:Key="AppAccentColor">#3A9FD8</Color>

      <SolidColorBrush x:Key="PlotLowBrush"      Color="#5DADE2"/>
      <SolidColorBrush x:Key="PlotMidLowBrush"   Color="#2ECC71"/>
      <SolidColorBrush x:Key="PlotMidBrush"      Color="#F4D03F"/>
      <SolidColorBrush x:Key="PlotMidHighBrush"  Color="#EB984E"/>
      <SolidColorBrush x:Key="PlotHighBrush"     Color="#E74C3C"/>

      <SolidColorBrush x:Key="GridCanvasBackgroundBrush" Color="#1E1E1E"/>
      <SolidColorBrush x:Key="GridStepArrowBrush"        Color="#CCCCCC"/>
      <SolidColorBrush x:Key="GridCurrentStepBrush"      Color="#3A9FD8"/>
      <SolidColorBrush x:Key="GridCompletedStepBrush"    Color="#2ECC71"/>
      <SolidColorBrush x:Key="GridPendingStepBrush"      Color="#555555"/>
      <SolidColorBrush x:Key="GridFlaggedStepBrush"      Color="#E74C3C"/>

      <SolidColorBrush x:Key="LoopOkBrush"       Color="#202ECC7120"/>
      <SolidColorBrush x:Key="LoopWarnBrush"     Color="#20EB984E"/>
      <SolidColorBrush x:Key="LoopErrorBrush"    Color="#20E74C3C"/>
    </ResourceDictionary>

  </ResourceDictionary.ThemeDictionaries>
</ResourceDictionary>
```

> **Note:** The palette values above are placeholders chosen to be functional
> and coherent. They will be revisited when visual design is formally agreed.
> The architecture is what matters here.

---

### `Styles/TextStyles.xaml`

Named `TextBlock` styles. All reference `{ThemeResource}` tokens — no literal
colour values. `BasedOn` chains to the WinUI 3 type-scale styles so the app
stays consistent with Fluent typography.

```xml
<ResourceDictionary>

  <!-- Page / section headings -->
  <Style x:Key="PageTitleStyle" TargetType="TextBlock"
         BasedOn="{StaticResource TitleLargeTextBlockStyle}">
    <Setter Property="FontWeight" Value="SemiBold"/>
  </Style>

  <Style x:Key="SectionHeaderStyle" TargetType="TextBlock"
         BasedOn="{StaticResource SubtitleTextBlockStyle}">
    <Setter Property="FontWeight" Value="SemiBold"/>
  </Style>

  <!-- Result metric display (e.g. "658.7 µm") -->
  <Style x:Key="MetricValueStyle" TargetType="TextBlock">
    <Setter Property="FontSize"   Value="28"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
    <Setter Property="Foreground"
            Value="{ThemeResource TextFillColorPrimaryBrush}"/>
  </Style>

  <!-- Label above a metric value (e.g. "Flatness (peak-to-valley)") -->
  <Style x:Key="MetricLabelStyle" TargetType="TextBlock"
         BasedOn="{StaticResource CaptionTextBlockStyle}">
    <Setter Property="Foreground"
            Value="{ThemeResource TextFillColorSecondaryBrush}"/>
  </Style>

  <!-- Step instruction text in MeasurementView / CorrectionView -->
  <Style x:Key="InstructionTextStyle" TargetType="TextBlock"
         BasedOn="{StaticResource BodyTextBlockStyle}"/>

  <!-- Secondary / helper text -->
  <Style x:Key="HelperTextStyle" TargetType="TextBlock"
         BasedOn="{StaticResource CaptionTextBlockStyle}">
    <Setter Property="Foreground"
            Value="{ThemeResource TextFillColorSecondaryBrush}"/>
  </Style>

</ResourceDictionary>
```

---

### `Styles/ControlStyles.xaml`

Implicit and named control styles. Implicit styles (no `x:Key`) apply
automatically to every matching control in the app.

```xml
<ResourceDictionary>

  <!-- Implicit: all Buttons get a consistent corner radius and min height -->
  <Style TargetType="Button" BasedOn="{StaticResource DefaultButtonStyle}">
    <Setter Property="CornerRadius" Value="6"/>
    <Setter Property="MinHeight"    Value="36"/>
  </Style>

  <!-- Named: card / panel container used throughout all Views -->
  <Style x:Key="CardStyle" TargetType="Border">
    <Setter Property="Background"
            Value="{ThemeResource CardBackgroundFillColorDefaultBrush}"/>
    <Setter Property="BorderBrush"
            Value="{ThemeResource CardStrokeColorDefaultBrush}"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CornerRadius"    Value="8"/>
    <Setter Property="Padding"         Value="16"/>
  </Style>

  <!-- Variant: tighter padding for compact info cards -->
  <Style x:Key="CompactCardStyle" TargetType="Border"
         BasedOn="{StaticResource CardStyle}">
    <Setter Property="Padding" Value="12"/>
  </Style>

</ResourceDictionary>
```

---

## Changes to existing files

### `App.xaml` — merge all style dictionaries

Add the three new dictionaries to `MergedDictionaries` **after**
`XamlControlsResources` (order matters — app tokens must override defaults):

```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls"/>
      <ResourceDictionary Source="Styles/ThemeColors.xaml"/>
      <ResourceDictionary Source="Styles/TextStyles.xaml"/>
      <ResourceDictionary Source="Styles/ControlStyles.xaml"/>
    </ResourceDictionary.MergedDictionaries>
  </ResourceDictionary>
</Application.Resources>
```

---

### `ISettingsService.cs` — add theme property

```csharp
ElementTheme AppTheme { get; set; }
```

---

### `SettingsService.cs` — persist theme choice

Add to the JSON settings bag alongside the existing default-folder preference:

```csharp
public ElementTheme AppTheme
{
    get => _settings.TryGetValue("appTheme", out var v)
           && Enum.TryParse<ElementTheme>(v?.ToString(), out var t)
           ? t : ElementTheme.Default;
    set
    {
        _settings["appTheme"] = value.ToString();
        Save();
    }
}
```

Default (`ElementTheme.Default`) means "follow the Windows system theme".

---

### `MainWindow.xaml.cs` — apply theme on startup and expose setter

```csharp
// Called once during initialisation, after DI container is built
private void ApplyPersistedTheme()
{
    var settings = App.Services.GetRequiredService<ISettingsService>();
    rootFrame.RequestedTheme = settings.AppTheme;
}

// Called by PreferencesDialog (via MainViewModel or direct service call)
// when the user changes the theme selector
public void ApplyTheme(ElementTheme theme)
{
    rootFrame.RequestedTheme = theme;
}
```

`rootFrame` is the root `Frame` element in `MainWindow.xaml`.

---

### `PreferencesDialog.xaml` — add theme selector

The existing Preferences dialog already houses the default-project-folder
setting. Add a theme selector group directly below it:

```xml
<StackPanel Spacing="8">
  <TextBlock Text="Theme"
             Style="{StaticResource SectionHeaderStyle}"/>
  <RadioButtons SelectedIndex="{x:Bind ViewModel.ThemeIndex, Mode=TwoWay}">
    <x:String>Follow system</x:String>
    <x:String>Light</x:String>
    <x:String>Dark</x:String>
  </RadioButtons>
</StackPanel>
```

`ThemeIndex` maps: 0 → `ElementTheme.Default`, 1 → `ElementTheme.Light`,
2 → `ElementTheme.Dark`.

---

### `PreferencesDialog.xaml.cs` (or its ViewModel)

Add `ThemeIndex` as an `[ObservableProperty]` and wire it to
`ISettingsService.AppTheme` plus a call to
`MainWindow.ApplyTheme(ElementTheme)` on change.

---

### View XAML audit — all four Views and all Dialogs

For each of `ProjectSetupView.xaml`, `MeasurementView.xaml`,
`ResultsView.xaml`, `CorrectionView.xaml`, and all files under `Views/Dialogs/`:

| Find | Replace with |
|---|---|
| Hardcoded `Foreground="#..."` | `{ThemeResource TextFillColorPrimaryBrush}` or a named style |
| Hardcoded `Background="#..."` | `{ThemeResource ...}` or `{StaticResource CardStyle}` |
| Inline `FontSize="..."` | Named style from `TextStyles.xaml` |
| Inline `FontWeight="..."` | Named style from `TextStyles.xaml` |
| Ad-hoc card/panel `Border` styling | `Style="{StaticResource CardStyle}"` |

**Rule going forward:** View XAML contains no literal colour values,
font sizes, or font weights. Every visual token is a `{ThemeResource}` or
`{StaticResource}` reference.

---

### Canvas / code-behind renderers

The display modules that draw directly to a `Canvas` in C#
(`SurfacePlot3DDisplay`, `MeasurementsGridRenderer`, `StrategyPreviewRenderer`,
`ParallelWaysDisplay`) currently use hardcoded `Color` literals. These must be
updated to resolve colours from the resource dictionary at render time:

```csharp
// Helper to resolve a theme brush from the resource dictionary
private static Color GetThemeColor(FrameworkElement element, string resourceKey)
{
    if (element.Resources.TryGetValue(resourceKey, out var res)
        || Application.Current.Resources.TryGetValue(resourceKey, out res))
    {
        return res is SolidColorBrush brush ? brush.Color : Colors.Gray;
    }
    return Colors.Gray;
}
```

Then replace hardcoded `Colors.Red`, `Colors.Blue`, etc. with calls to
`GetThemeColor(canvas, "PlotHighBrush")` etc., passing the owning
`FrameworkElement` so the correct theme dictionary is queried.

The renderers must also subscribe to `ActualThemeChanged` on their parent
element (or the `Window`) and re-render when the theme changes, so a live
theme switch updates the canvas immediately.

---

## What this work package explicitly does NOT do

- Change the visual design, spacing, or layout of any View
- Change the colour palette (placeholder values are used — final palette
  comes after the design discussion)
- Add Mica / Acrylic backdrop (can be added in a later cosmetic pass)
- Change navigation or data flow

---

## Acceptance criteria

1. `LevelApp.App/Styles/` exists with `ThemeColors.xaml`, `TextStyles.xaml`,
   and `ControlStyles.xaml`; all three are merged in `App.xaml`
2. No View XAML file contains a literal colour value, font size, or font weight
3. All canvas renderers resolve colours from `{ThemeResource}` tokens
4. Switching theme in Preferences immediately updates the entire UI — including
   canvas renderers — without restart
5. Theme choice persists across app restarts
6. Both Light and Dark themes are visually coherent at runtime
7. Existing unit tests (`LevelApp.Tests`) still pass — this work package
   touches only the App layer

---

## Version bump

Set `AppVersion.Patch` → `0`, `AppVersion.Minor` → `8` in `AppVersion.cs`
before committing. Commit message:

```
[v0.8.0] WP0.08: theme architecture — resource dictionaries, light/dark, theme persistence
```
